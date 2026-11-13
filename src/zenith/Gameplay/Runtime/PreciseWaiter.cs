using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Zenith.Gameplay.Runtime;

/// <summary>Waits until a <see cref="Stopwatch"/> timestamp. Returns early (without throwing) when cancelled.</summary>
interface ITickWaiter : IDisposable
{
    void WaitUntil(long deadlineTimestamp, CancellationToken cancellationToken);
}

/// <summary>
/// Sub-millisecond waiting without depending on the OS timer tick. <c>Task.Delay</c> and <c>Thread.Sleep</c> are only as exact as the
/// system timer (about 15.6 ms on Windows unless something raised it for the whole machine; ~1 ms elsewhere), which at 50 ms per tick
/// is a 30% error. So the wait is split in two: a cancellable coarse sleep for everything except the last <see cref="SpinMargin"/>, then
/// a spin on <see cref="Stopwatch"/> up to the deadline. The coarse sleep uses the best primitive the platform has:
/// <list type="bullet">
/// <item>Windows 10 1803+: a high-resolution waitable timer (<c>CREATE_WAITABLE_TIMER_HIGH_RESOLUTION</c>, ~0.5 ms), scoped to this
/// timer only, no machine-wide timer-period change.</item>
/// <item>Older Windows: <c>timeBeginPeriod(1)</c> for the life of the waiter, then a millisecond wait.</item>
/// <item>Linux / macOS: a millisecond wait (their sleep is already ~1 ms accurate).</item>
/// </list>
/// The spin costs at most <see cref="SpinMargin"/> of CPU per tick (a few percent of one core at 20 TPS) and is what buys the precision.
/// </summary>
sealed class PreciseWaiter : ITickWaiter
{
    /// <summary>How long before the deadline the coarse sleep hands over to the spin; covers the sleep primitive's worst wake-up error.</summary>
    public TimeSpan SpinMargin { get; }

    private readonly long _spinMarginTicks;
    private readonly WindowsHighResolutionTimer? _timer;
    private readonly bool _periodRaised;

    public PreciseWaiter()
    {
        if (OperatingSystem.IsWindows())
        {
            _timer = WindowsHighResolutionTimer.TryCreate();
            if (_timer is null)
                _periodRaised = WindowsNative.timeBeginPeriod(1) == 0;
        }

        // High-resolution timer: ~0.5 ms error. Anything else: a 1 ms-granular wait that can overshoot by ~2 ms.
        SpinMargin = _timer is not null ? TimeSpan.FromMilliseconds(1.5) : TimeSpan.FromMilliseconds(2.5);
        _spinMarginTicks = (long)(SpinMargin.TotalSeconds * Stopwatch.Frequency);
    }

    /// <summary>Which coarse-sleep primitive is in use, for diagnostics.</summary>
    public string Mode => _timer is not null ? "high-resolution-timer" : _periodRaised ? "timer-period-1ms" : "millisecond-wait";

    public void WaitUntil(long deadlineTimestamp, CancellationToken cancellationToken)
    {
        var spin = new SpinWait();
        while (!cancellationToken.IsCancellationRequested)
        {
            var remaining = deadlineTimestamp - Stopwatch.GetTimestamp();
            if (remaining <= 0) return;

            if (remaining > _spinMarginTicks)
            {
                CoarseSleep(remaining - _spinMarginTicks, cancellationToken);
                continue;
            }

            // Last stretch: poll the clock. SpinOnce(-1) never yields the thread, so the wake-up is not at the scheduler's mercy.
            spin.SpinOnce(sleep1Threshold: -1);
        }
    }

    private void CoarseSleep(long ticks, CancellationToken cancellationToken)
    {
        if (_timer is not null)
        {
            _timer.Wait(ticks, cancellationToken);
            return;
        }

        var milliseconds = (int)(ticks * 1000 / Stopwatch.Frequency);
        if (milliseconds > 0) cancellationToken.WaitHandle.WaitOne(milliseconds);
    }

    public void Dispose()
    {
        _timer?.Dispose();
        if (_periodRaised) _ = WindowsNative.timeEndPeriod(1);
    }
}

internal static class WindowsNative
{
    public const uint CreateWaitableTimerHighResolution = 0x00000002;
    public const uint TimerAllAccess = 0x1F0003;

    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWaitableTimerEx(IntPtr attributes, string? name, uint flags, uint desiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completionRoutine, IntPtr completionArgument, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [DllImport("winmm.dll")]
    public static extern uint timeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll")]
    public static extern uint timeEndPeriod(uint milliseconds);
}

/// <summary>A Windows high-resolution waitable timer that can also be woken by a cancellation token.</summary>
internal sealed class WindowsHighResolutionTimer : IDisposable
{
    private readonly SafeWaitHandle _handle;
    private readonly WaitHandle _wait;
    private readonly WaitHandle[] _waitSet = new WaitHandle[2]; // reused: no allocation per tick

    private sealed class TimerWaitHandle : WaitHandle
    {
        public TimerWaitHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    private WindowsHighResolutionTimer(SafeWaitHandle handle)
    {
        _handle = handle;
        _wait = new TimerWaitHandle(handle);
    }

    /// <summary>Null when the OS has no high-resolution timers (before Windows 10 1803).</summary>
    public static WindowsHighResolutionTimer? TryCreate()
    {
        var raw = WindowsNative.CreateWaitableTimerEx(
            IntPtr.Zero, null, WindowsNative.CreateWaitableTimerHighResolution, WindowsNative.TimerAllAccess);
        if (raw == IntPtr.Zero) return null;
        return new WindowsHighResolutionTimer(new SafeWaitHandle(raw, ownsHandle: true));
    }

    /// <summary>Sleeps for <paramref name="stopwatchTicks"/>, or until <paramref name="cancellationToken"/> fires.</summary>
    public void Wait(long stopwatchTicks, CancellationToken cancellationToken)
    {
        // Relative due time: negative, in 100 ns units.
        var due = -Math.Max(1, stopwatchTicks * 10_000_000 / Stopwatch.Frequency);
        if (!WindowsNative.SetWaitableTimer(_handle.DangerousGetHandle(), ref due, 0, IntPtr.Zero, IntPtr.Zero, resume: false))
            return;
        _waitSet[0] = _wait;
        _waitSet[1] = cancellationToken.WaitHandle;
        WaitHandle.WaitAny(_waitSet);
    }

    public void Dispose()
    {
        _wait.Dispose();
        _handle.Dispose();
    }
}
