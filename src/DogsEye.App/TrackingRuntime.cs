using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using DogsEye.Core;

namespace DogsEye.App;

internal sealed record RuntimeSnapshot(TrackingTelemetry Tracking, string Device, double Rate, bool LiveOutput, string? Error)
{
    public string StreamStatus { get; init; } = "Waiting for SDK";
    public bool KeyHeld { get; init; }
    public bool LongPressTriggered { get; init; }
    public double KeyHeldMs { get; init; }
    public double PollingRate { get; init; }
    public double MouseEventRate { get; init; }
    public int RequestedRate { get; init; }
    public string TimerMode { get; init; } = "Starting";
}

internal sealed class TrackingRuntime : IDisposable
{
    private readonly ConcurrentQueue<Action> pending = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Thread thread;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private TrackingController controller;
    private InputCommandService commands;
    private AppSettings settings;
    private bool liveOutput, keyboardSuspended;
    private bool activationKeyDown, keyboardDispatch;
    private int outputBlocked;
    private RuntimeSnapshot latest = new(new(), "Detecting Tobii…", 0, false, null);
    private readonly string logPath;
    private readonly bool diagnosticsOnly;
    public RuntimeSnapshot Latest => Volatile.Read(ref latest);
    public TrackingRuntime(AppSettings settings, string logPath, bool diagnosticsOnly = false)
    {
        this.settings = settings;
        this.logPath = logPath;
        this.diagnosticsOnly = diagnosticsOnly;
        controller = MakeController();
        commands = MakeCommands();
        thread = new Thread(Run) { IsBackground = true, Name = "DogsEye tracking" };
        thread.Start();
    }
    private TrackingController MakeController()
    {
        var result = new TrackingController(settings, liveOutput ? new GatedMouseOutput(this) : new PreviewMouseOutput());
        result.Feedback += Feedback;
        return result;
    }
    private InputCommandService MakeCommands()
    {
        var result = new InputCommandService(settings.HoldDurationMs);
        // Raw Input supplies explicit edges; the capture service handles release
        // gating on rebind. Do not discard the first physical key-down at startup.
        result.Reset(false);
        result.Command += command =>
        {
            Log($"{(keyboardDispatch ? "Keyboard" : "UI")} command: {command}");
            switch (command)
            {
                case InputCommand.Toggle: Interlocked.Exchange(ref outputBlocked, 0); controller.Toggle(); break;
                case InputCommand.Recenter: controller.Recenter(clock.Elapsed.TotalSeconds); break;
                case InputCommand.Disable: controller.Disable(); break;
            }
        };
        return result;
    }
    public void Send(InputCommand command)
    {
        if (command == InputCommand.Disable) Interlocked.Exchange(ref outputBlocked, 1);
        pending.Enqueue(() => commands.Send(command));
    }
    public void SuspendKeyboard(bool suspend)
    {
        if (suspend) Interlocked.Exchange(ref outputBlocked, 1);
        pending.Enqueue(() => { keyboardSuspended = suspend; activationKeyDown = false; commands.Reset(false); if (suspend) controller.Disable(); });
    }
    public void SetActivationKey(bool down)
    {
        double received = clock.Elapsed.TotalSeconds;
        pending.Enqueue(() =>
        {
            if (keyboardSuspended || diagnosticsOnly) return;
            activationKeyDown = down;
            keyboardDispatch = true;
            try { commands.Update(down, received); }
            finally { keyboardDispatch = false; }
        });
    }
    public void Configure(AppSettings value, bool outputEnabled)
    {
        Interlocked.Exchange(ref outputBlocked, 1);
        pending.Enqueue(() =>
        {
            controller.Disable();
            settings = value;
            liveOutput = outputEnabled && !diagnosticsOnly;
            controller = MakeController();
            commands = MakeCommands();
            activationKeyDown = false;
        });
    }
    public void UpdateSettings(AppSettings value)
    {
        value.Validate();
        pending.Enqueue(() =>
        {
            bool timingChanged = settings.HoldDurationMs != value.HoldDurationMs;
            settings = value;
            controller.Configure(value);
            if (timingChanged)
            {
                commands = MakeCommands();
                commands.Reset(activationKeyDown);
            }
        });
    }
    private sealed class GatedMouseOutput(TrackingRuntime owner) : IMouseOutput
    {
        private readonly WindowsSendInputMouseOutput windows = new();
        public bool Move(int x, int y) => Volatile.Read(ref owner.outputBlocked) == 0 && !owner.stop.IsCancellationRequested && windows.Move(x, y);
    }
    private void Feedback(string value)
    {
        Log(value);
        if (!settings.AudioEnabled || value == nameof(TrackingState.Calibrating)) return;
        _ = Task.Run(() =>
        {
            switch (value)
            {
                case nameof(TrackingState.Active): NativeMethods.Beep(1100, 65); NativeMethods.Beep(1400, 65); break;
                case nameof(TrackingState.Disabled): NativeMethods.Beep(440, 100); break;
                case "Recentered": NativeMethods.Beep(900, 65); break;
                case nameof(TrackingState.TrackingLost): NativeMethods.Beep(330, 90); break;
            }
        });
    }
    private void Log(string message)
    {
        try { File.AppendAllText(logPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    private void Run()
    {
        using var tobii = new TobiiService();
        using var timer = new TrackingLoopTimer(stop.Token.WaitHandle);
        bool initialized = false;
        double nextRetry = 0, rateStart = 0;
        int samples = 0, ticks = 0, mouseEvents = 0;
        long? lastStamp = null;
        double rate = 0, pollingRate = 0, mouseEventRate = 0;
        string device = "Detecting Tobii…";
        string? error = null;
        string streamStatus = "Waiting for SDK";
        while (!stop.IsCancellationRequested)
        {
            double now = clock.Elapsed.TotalSeconds;
            try
            {
                if (!initialized && now >= nextRetry)
                {
                    tobii.Initialize();
                    initialized = true;
                    error = null;
                }
                if (initialized)
                {
                    var snapshot = tobii.Poll();
                    device = snapshot.Connected == 0 ? "Tobii disconnected" : string.IsNullOrWhiteSpace(snapshot.Model) ? "Tobii connected" : snapshot.Model;
                    if (snapshot.Connected != 0 && snapshot.HeadSupported == 0) device += " · head stream unavailable";
                    else if (snapshot.Connected != 0 && snapshot.Enabled == 0) device += " · disabled in Tobii software";
                    streamStatus = $"Connected={snapshot.Connected}; Enabled={snapshot.Enabled}; Present={snapshot.Present}; HeadSupported={snapshot.HeadSupported}; HasPose={snapshot.HasPose}";
                    controller.Update(snapshot.Pose, snapshot.Available, now);
                    if (snapshot.Available && snapshot.HasPose != 0 && snapshot.Timestamp != lastStamp) { samples++; lastStamp = snapshot.Timestamp; }
                }
                else controller.Update(null, false, now);
                // Process user commands after the current pose; recenter never uses an old frame.
                while (pending.TryDequeue(out var action)) action();
                if (!keyboardSuspended && !diagnosticsOnly)
                {
                    keyboardDispatch = true;
                    try { commands.Update(activationKeyDown, clock.Elapsed.TotalSeconds); }
                    finally { keyboardDispatch = false; }
                }
                controller.AdvanceOutput(clock.Elapsed.TotalSeconds);
                ticks++;
                if (controller.Telemetry.DeltaX != 0 || controller.Telemetry.DeltaY != 0) mouseEvents++;
                if (now - rateStart >= 1)
                {
                    double elapsed = now - rateStart;
                    rate = samples / elapsed;
                    pollingRate = ticks / elapsed;
                    mouseEventRate = mouseEvents / elapsed;
                    samples = ticks = mouseEvents = 0;
                    rateStart = now;
                }
                Volatile.Write(ref latest, new(controller.Telemetry, device, rate, liveOutput, error ?? controller.Telemetry.Error)
                {
                    StreamStatus = streamStatus, KeyHeld = commands.IsHeld,
                    PollingRate = pollingRate, MouseEventRate = mouseEventRate,
                    RequestedRate = settings.PollingRateHz, TimerMode = timer.Mode,
                    LongPressTriggered = commands.LongPressTriggered, KeyHeldMs = commands.HeldMilliseconds(clock.Elapsed.TotalSeconds)
                });
            }
            catch (Exception e)
            {
                controller.Disable();
                initialized = false;
                nextRetry = now + 3;
                error = $"{e.GetType().Name}: {e.Message}";
                Log(error);
                try { tobii.Dispose(); } catch { /* Missing native library can also prevent shutdown. */ }
                Volatile.Write(ref latest, new(controller.Telemetry, "Tobii unavailable · retrying", 0, liveOutput, error));
            }
            timer.Wait(clock, settings.PollingRateHz);
        }
        controller.Disable();
    }
    public void Dispose()
    {
        Interlocked.Exchange(ref outputBlocked, 1);
        stop.Cancel();
        if (thread.Join(2000)) stop.Dispose();
    }
}
