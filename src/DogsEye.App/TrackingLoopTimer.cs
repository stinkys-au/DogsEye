using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DogsEye.App;

// Local, interruptible timer: no spinning, global timer-resolution changes or priority changes.
internal sealed class TrackingLoopTimer : IDisposable
{
    private sealed class TimerHandle(SafeWaitHandle handle) : WaitHandle
    {
        public void Initialize() => SafeWaitHandle = handle;
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", SetLastError = true)]
    private static extern SafeWaitHandle CreateTimer(nint attributes, nint name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period,
        nint completion, nint argument, [MarshalAs(UnmanagedType.Bool)] bool resume);

    private readonly TimerHandle? timer;
    private readonly WaitHandle stop;
    private readonly WaitHandle[] handles;
    private double deadline;
    public string Mode { get; private set; }

    public TrackingLoopTimer(WaitHandle stop)
    {
        this.stop = stop;
        var handle = CreateTimer(0, 0, 2, 0x100002);
        Mode = "High-resolution timer";
        if (handle.IsInvalid)
        {
            handle.Dispose();
            handle = CreateTimer(0, 0, 0, 0x100002);
            Mode = "Standard timer";
        }
        if (!handle.IsInvalid)
        {
            timer = new(handle);
            timer.Initialize();
            handles = [stop, timer];
        }
        else
        {
            handle.Dispose();
            Mode = "Standard wait";
            handles = [stop];
        }
    }
    public void Wait(Stopwatch clock, int rateHz)
    {
        double interval = 1.0 / rateHz, now = clock.Elapsed.TotalSeconds;
        deadline += interval;
        if (deadline <= now || deadline > now + interval * 2) deadline = now + interval;
        double remaining = deadline - now;
        long due = -Math.Max(1, (long)(remaining * 10_000_000));
        if (timer is not null && SetWaitableTimer(timer.SafeWaitHandle, ref due, 0, 0, 0, false))
            WaitHandle.WaitAny(handles);
        else
        {
            Mode = "Standard wait";
            stop.WaitOne(TimeSpan.FromMilliseconds(Math.Max(1, remaining * 1000)));
        }
    }
    public void Dispose() => timer?.Dispose();
}
