using Zenith.Event;
using Xunit;

namespace Zenith.Tests;

public class EventBusTests
{
    private sealed class ProbeEvent
    {
        public int Value { get; init; }
    }

    private sealed class OtherEvent;

    [Fact]
    public void Publish_with_no_listeners_is_a_no_op()
    {
        var bus = new EventBus();
        bus.Publish(new ProbeEvent { Value = 1 }); // must not throw
    }

    [Fact]
    public void Publish_calls_all_listeners_in_subscribe_order()
    {
        var bus = new EventBus();
        var order = new List<int>();
        bus.Subscribe<ProbeEvent>(_ => order.Add(1));
        bus.Subscribe<ProbeEvent>(_ => order.Add(2));
        bus.Subscribe<ProbeEvent>(_ => order.Add(3));

        bus.Publish(new ProbeEvent { Value = 42 });

        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    public void Publish_only_reaches_listeners_of_the_published_type()
    {
        var bus = new EventBus();
        var probeHits = 0;
        var otherHits = 0;
        bus.Subscribe<ProbeEvent>(_ => probeHits++);
        bus.Subscribe<OtherEvent>(_ => otherHits++);

        bus.Publish(new ProbeEvent { Value = 1 });

        Assert.Equal(1, probeHits);
        Assert.Equal(0, otherHits);
    }

    [Fact]
    public void Publish_isolates_a_throwing_listener_so_later_listeners_still_run()
    {
        var bus = new EventBus();
        var laterRan = false;
        bus.Subscribe<ProbeEvent>(_ => throw new InvalidOperationException("boom"));
        bus.Subscribe<ProbeEvent>(_ => laterRan = true);

        bus.Publish(new ProbeEvent { Value = 1 }); // must not throw / must not stop dispatch

        Assert.True(laterRan);
    }

    [Fact]
    public void Publish_passes_the_same_event_instance_to_every_listener()
    {
        var bus = new EventBus();
        var seen = new List<int>();
        bus.Subscribe<ProbeEvent>(e => seen.Add(e.Value));
        bus.Subscribe<ProbeEvent>(e => seen.Add(e.Value));

        bus.Publish(new ProbeEvent { Value = 7 });

        Assert.Equal([7, 7], seen);
    }

    [Fact]
    public void Subscribing_during_publish_does_not_affect_the_in_flight_dispatch()
    {
        var bus = new EventBus();
        var lateSubscriberHits = 0;
        var firstListenerRan = false;

        bus.Subscribe<ProbeEvent>(_ =>
        {
            firstListenerRan = true;
            bus.Subscribe<ProbeEvent>(_ => lateSubscriberHits++);
        });

        bus.Publish(new ProbeEvent { Value = 1 });
        Assert.True(firstListenerRan);
        Assert.Equal(0, lateSubscriberHits); // registered too late for this Publish call

        bus.Publish(new ProbeEvent { Value = 2 });
        Assert.Equal(1, lateSubscriberHits); // fires on the next Publish
    }
}
