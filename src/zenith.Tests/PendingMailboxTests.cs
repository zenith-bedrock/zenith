using Zenith.Player;
using Xunit;

namespace Zenith.Tests;

/// <summary>
/// Phase XII — leaf tests for the thread→GameLoop handoff primitives extracted from Player's
/// ~15 hand-written Submit/TryConsume pairs. Mechanism only: no gameplay rules belong here.
/// </summary>
public class PendingMailboxTests
{
    [Fact]
    public void PendingSignal_submit_then_consume_returns_true_once()
    {
        var signal = new PendingSignal();

        signal.Submit();

        Assert.True(signal.TryConsume());
        Assert.False(signal.TryConsume());
    }

    [Fact]
    public void PendingSignal_consuming_with_nothing_pending_returns_false()
    {
        var signal = new PendingSignal();

        Assert.False(signal.TryConsume());
    }

    [Fact]
    public void PendingSignal_multiple_submits_before_consume_still_signal_only_once()
    {
        var signal = new PendingSignal();

        signal.Submit();
        signal.Submit();
        signal.Submit();

        Assert.True(signal.TryConsume());
        Assert.False(signal.TryConsume());
    }

    [Fact]
    public void PendingValue_latest_submission_wins_over_an_earlier_unconsumed_one()
    {
        var value = new PendingValue<int>();

        value.Submit(1);
        value.Submit(2);

        Assert.True(value.TryConsume(out var consumed));
        Assert.Equal(2, consumed);
        Assert.False(value.TryConsume(out _));
    }

    [Fact]
    public void PendingValue_TryConsume_with_nothing_pending_reports_default()
    {
        var value = new PendingValue<int>();

        Assert.False(value.TryConsume(out var consumed));
        Assert.Equal(0, consumed);
    }

    [Fact]
    public void PendingValue_TryConsume_with_nothing_pending_reports_the_supplied_fallback()
    {
        var value = new PendingValue<string>();

        Assert.False(value.TryConsume("fallback", out var consumed));
        Assert.Equal("fallback", consumed);
    }

    [Fact]
    public void PendingValue_consuming_clears_it_so_a_second_consume_reports_empty()
    {
        var value = new PendingValue<int>();
        value.Submit(5);

        Assert.True(value.TryConsume(out _));
        Assert.False(value.TryConsume(-1, out var second));
        Assert.Equal(-1, second);
    }

    /// <summary>Phase XVIII — added for entity-interact routing: several candidate owners may each ask "is this mine?" before one claims it.</summary>
    [Fact]
    public void PendingValue_TryConsumeIf_leaves_a_non_matching_value_for_another_owner_to_check()
    {
        var value = new PendingValue<int>();
        value.Submit(42);

        Assert.False(value.TryConsumeIf(v => v == 7, out _));
        Assert.True(value.TryConsumeIf(v => v == 42, out var consumed));
        Assert.Equal(42, consumed);
        Assert.False(value.TryConsumeIf(_ => true, out _));
    }

    [Fact]
    public void PendingValue_TryConsumeIf_with_nothing_pending_reports_false_without_invoking_the_predicate()
    {
        var value = new PendingValue<int>();
        var predicateCalled = false;

        var result = value.TryConsumeIf(_ => { predicateCalled = true; return true; }, out _);

        Assert.False(result);
        Assert.False(predicateCalled);
    }

    [Fact]
    public void PendingMailbox_drains_in_fifo_order()
    {
        var mailbox = new PendingMailbox<int>(capacity: 8);

        Assert.True(mailbox.Submit(1));
        Assert.True(mailbox.Submit(2));
        Assert.True(mailbox.Submit(3));

        Assert.True(mailbox.TryConsume(out var first));
        Assert.True(mailbox.TryConsume(out var second));
        Assert.True(mailbox.TryConsume(out var third));
        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(3, third);
    }

    [Fact]
    public void PendingMailbox_rejects_submissions_once_at_capacity_and_keeps_the_already_accepted_ones()
    {
        var mailbox = new PendingMailbox<int>(capacity: 2);

        Assert.True(mailbox.Submit(1));
        Assert.True(mailbox.Submit(2));
        Assert.False(mailbox.Submit(3)); // overflow rejects the newest, not the oldest

        Assert.True(mailbox.TryConsume(out var first));
        Assert.True(mailbox.TryConsume(out var second));
        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.False(mailbox.TryConsume(out _));
    }

    [Fact]
    public void PendingMailbox_consuming_with_nothing_queued_reports_default_and_false()
    {
        var mailbox = new PendingMailbox<string>(capacity: 4);

        Assert.False(mailbox.TryConsume(out var value));
        Assert.Null(value);
    }

    [Fact]
    public void PendingMailbox_freeing_capacity_by_consuming_allows_further_submissions()
    {
        var mailbox = new PendingMailbox<int>(capacity: 1);

        Assert.True(mailbox.Submit(1));
        Assert.False(mailbox.Submit(2));

        Assert.True(mailbox.TryConsume(out _));
        Assert.True(mailbox.Submit(2));
    }
}
