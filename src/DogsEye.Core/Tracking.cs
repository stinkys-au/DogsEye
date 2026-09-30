namespace DogsEye.Core;

public enum TrackingState { Disabled, Calibrating, Active, TrackingLost }
public readonly record struct HeadPose(long Timestamp, double Yaw, double Pitch, double Roll)
{
    public bool IsFinite => double.IsFinite(Yaw) && double.IsFinite(Pitch) && double.IsFinite(Roll);
}
public interface IMouseOutput { bool Move(int deltaX, int deltaY); }
public sealed class PreviewMouseOutput : IMouseOutput { public bool Move(int deltaX, int deltaY) => true; }
public sealed record TrackingTelemetry
{
    public TrackingState State { get; init; }
    public HeadPose? Raw { get; init; }
    public bool PoseValid { get; init; }
    public double? NeutralYaw { get; init; }
    public double? NeutralPitch { get; init; }
    public int CentreVersion { get; init; }
    public double RelativeYaw { get; init; }
    public double RelativePitch { get; init; }
    public double ProcessedX { get; init; }
    public double ProcessedY { get; init; }
    public double TargetX { get; init; }
    public double TargetY { get; init; }
    public int VirtualX { get; init; }
    public int VirtualY { get; init; }
    public int DeltaX { get; init; }
    public int DeltaY { get; init; }
    public string? Error { get; init; }
}

public sealed class HeadTrackingProcessor(AppSettings settings)
{
    public void Configure(AppSettings value) => settings = value;
    private double filteredYaw, filteredPitch;
    private readonly AdaptiveTrackingFilter yawFilter = new(), pitchFilter = new();
    public void Reset()
    {
        filteredYaw = filteredPitch = 0;
        yawFilter.Reset();
        pitchFilter.Reset();
    }
    public (double X, double Y) Process(double yaw, double pitch, double elapsed)
    {
        double alpha = settings.Smoothing == 0 ? 1 : 1 - Math.Exp(-Math.Clamp(elapsed, .001, .05) / (settings.Smoothing * .2));
        double Axis(double angle, double deadzone, double negative, double positive, ref double filtered, AdaptiveTrackingFilter adaptive, LookResponseCurve curve)
        {
            double trimmed = Math.Sign(angle) * Math.Max(0, Math.Abs(angle) - deadzone);
            trimmed = Math.Clamp(trimmed, -(negative - deadzone), positive - deadzone);
            // Centre is an exact zero, without a filter tail drifting through the deadzone.
            if (trimmed == 0) { filtered = 0; adaptive.Reset(); }
            else filtered = settings.AdaptiveSmoothing
                ? adaptive.Process(trimmed, elapsed, settings.Smoothing, settings.AdaptiveResponsiveness)
                : filtered + alpha * (trimmed - filtered);
            double n = filtered / ((filtered >= 0 ? positive : negative) - deadzone);
            double magnitude = Math.Clamp(Math.Abs(n), 0, 1);
            return Math.Sign(n) * curve.Evaluate(magnitude);
        }
        return (Axis(yaw, settings.YawDeadzone, settings.HeadLeft, settings.HeadRight, ref filteredYaw, yawFilter, settings.LookCurveX),
            Axis(pitch, settings.PitchDeadzone, settings.HeadDown, settings.HeadUp, ref filteredPitch, pitchFilter, settings.LookCurveY));
    }
}

// Called exclusively from the tracking thread. Time is monotonic seconds.
public sealed class TrackingController
{
    private AppSettings settings;
    private readonly IMouseOutput output;
    private readonly HeadTrackingProcessor processor;
    private readonly List<(double Time, HeadPose Pose)> calibration = [];
    private HeadPose? raw;
    private long? lastTimestamp;
    private double lastFresh = double.NegativeInfinity, previousSample;
    private double neutralYaw, neutralPitch;
    private int virtualX, virtualY;
    private bool calibrated, hasCentre;
    private int centreVersion;
    private double rampX, rampY, goalX, goalY, rampStart, rampDuration;
    private double? previousOutput;
    private double rateRemainder;
    public const double StaleSeconds = .15;
    public TrackingState State { get; private set; }
    public TrackingTelemetry Telemetry { get; private set; } = new();
    public event Action<string>? Feedback;

    public TrackingController(AppSettings settings, IMouseOutput output)
    {
        settings.Validate();
        this.settings = settings;
        this.output = output;
        processor = new(settings);
    }
    // Preserve calibration, filter history and emitted position when tuning live.
    // Cancel the old target; the next fresh sample computes the new bounded target.
    public void Configure(AppSettings value)
    {
        value.Validate();
        settings = value;
        processor.Configure(value);
        CancelMovement();
    }
    private void SetState(TrackingState state)
    {
        if (State == state) return;
        State = state;
        Feedback?.Invoke(state.ToString());
    }
    private void ResetPosition()
    {
        processor.Reset();
        virtualX = virtualY = 0;
        CancelMovement();
        Telemetry = new() { State = State, Raw = raw, PoseValid = Telemetry.PoseValid,
            NeutralYaw = hasCentre ? neutralYaw : null, NeutralPitch = hasCentre ? neutralPitch : null,
            CentreVersion = centreVersion };
    }
    private void CancelMovement()
    {
        rampX = goalX = virtualX;
        rampY = goalY = virtualY;
        rampDuration = 0;
        previousOutput = null;
        rateRemainder = 0;
    }
    private (double X, double Y) InterpolatedTarget(double now)
    {
        double fraction = rampDuration == 0 ? 1 : Math.Clamp((now - rampStart) / rampDuration, 0, 1);
        return (rampX + (goalX - rampX) * fraction, rampY + (goalY - rampY) * fraction);
    }
    public void Disable()
    {
        SetState(TrackingState.Disabled);
        calibrated = false;
        // Keep the visual centre while output is off. Activation still calibrates afresh.
        calibration.Clear();
        ResetPosition();
    }
    public void Toggle()
    {
        if (State != TrackingState.Disabled) { Disable(); return; }
        calibrated = false;
        calibration.Clear();
        SetState(TrackingState.Calibrating);
        ResetPosition();
    }
    public bool Recenter(double now)
    {
        if (raw is not { } pose || now - lastFresh > StaleSeconds || !Telemetry.PoseValid) return false;
        neutralYaw = pose.Yaw;
        neutralPitch = pose.Pitch;
        hasCentre = true;
        centreVersion++;
        calibration.Clear();
        if (State != TrackingState.Disabled) { calibrated = true; SetState(TrackingState.Active); }
        previousSample = now;
        ResetPosition();
        Feedback?.Invoke("Recentered");
        return true;
    }
    private static double AngleDifference(double angle, double neutral) => ((angle - neutral + 540) % 360) - 180;
    public void Update(HeadPose? sample, bool trackingAvailable, double now)
    {
        bool finite = sample is { IsFinite: true };
        bool fresh = finite && sample!.Value.Timestamp != lastTimestamp;
        if (fresh)
        {
            raw = sample;
            lastTimestamp = sample!.Value.Timestamp;
            lastFresh = now;
        }
        // A null sample with an available device means no NEW packet this poll, not
        // an invalid pose. Output may finish movement toward a measured target. Explicit invalid
        // values or unavailable tracking fail immediately; silence expires by age.
        bool valid = trackingAvailable && (sample is null || finite) && raw is not null && now - lastFresh <= StaleSeconds;
        Telemetry = Telemetry with { Raw = raw, PoseValid = valid, DeltaX = 0, DeltaY = 0, State = State };
        if (State == TrackingState.Disabled)
        {
            // Keep the response preview alive, but never queue or emit mouse movement.
            if (valid && fresh)
            {
                UpdateResponse(sample!.Value, now - previousSample, now, false);
                previousSample = now;
            }
            return;
        }
        if (!valid)
        {
            calibration.Clear();
            CancelMovement();
            SetState(TrackingState.TrackingLost);
            Telemetry = Telemetry with { State = State };
            return;
        }
        if (!fresh) return;
        var current = sample!.Value;
        double elapsed = now - previousSample;
        previousSample = now;
        if (!calibrated)
        {
            SetState(TrackingState.Calibrating);
            if (calibration.Count > 0 && now - calibration[^1].Time > StaleSeconds) calibration.Clear();
            calibration.Add((now, current));
            if (calibration.Count >= 6 && now - calibration[0].Time >= .06)
            {
                // Use circular offsets to keep samples around +/-180 degrees together.
                var anchor = calibration[0].Pose;
                var yaws = calibration.Select(p => AngleDifference(p.Pose.Yaw, anchor.Yaw)).Order().ToArray();
                var pitches = calibration.Select(p => AngleDifference(p.Pose.Pitch, anchor.Pitch)).Order().ToArray();
                neutralYaw = anchor.Yaw + yaws[yaws.Length / 2];
                neutralPitch = anchor.Pitch + pitches[pitches.Length / 2];
                calibrated = true;
                hasCentre = true;
                centreVersion++;
                calibration.Clear();
                SetState(TrackingState.Active);
                ResetPosition();
            }
            Telemetry = Telemetry with { State = State };
            return;
        }
        if (State == TrackingState.TrackingLost)
        {
            // Keep the neutral and virtual position. Freeze the first recovered frame;
            // subsequent output catches up at a bounded rate without extrapolation.
            SetState(TrackingState.Active);
            Telemetry = Telemetry with { State = State };
            return;
        }
        UpdateResponse(current, elapsed, now, true);
    }
    private void UpdateResponse(HeadPose current, double elapsed, double now, bool queueOutput)
    {
        double yaw = AngleDifference(current.Yaw, neutralYaw), pitch = AngleDifference(current.Pitch, neutralPitch);
        var (x, y) = processor.Process(yaw, pitch, elapsed);
        double tx = x * (x < 0 ? settings.CameraLeft : settings.CameraRight) * settings.YawSafetyFactor;
        double ty = -y * (y < 0 ? settings.CameraDown : settings.CameraUp) * settings.PitchSafetyFactor;
        if (queueOutput && (tx != goalX || ty != goalY))
        {
            (rampX, rampY) = InterpolatedTarget(now);
            goalX = tx;
            goalY = ty;
            rampStart = now;
            // Spread a measured change across one sample interval; never predict a future pose.
            rampDuration = Math.Clamp(elapsed, .004, .05);
        }
        Telemetry = Telemetry with { State = State, RelativeYaw = yaw, RelativePitch = pitch,
            ProcessedX = x, ProcessedY = y, TargetX = tx, TargetY = ty };
    }
    // Called after user commands on every output tick, including between fresh SDK packets.
    public void AdvanceOutput(double now)
    {
        Telemetry = Telemetry with { DeltaX = 0, DeltaY = 0 };
        if (State != TrackingState.Active || !Telemetry.PoseValid) return;
        if (now - lastFresh > StaleSeconds)
        {
            CancelMovement();
            SetState(TrackingState.TrackingLost);
            Telemetry = Telemetry with { State = State, PoseValid = false };
            return;
        }
        double elapsed = previousOutput is { } last ? Math.Clamp(now - last, 0, .025) : 0;
        previousOutput = now;
        var (tx, ty) = InterpolatedTarget(now);
        // Quantise the absolute target, not every small difference: fractional movement is retained.
        // Fractional rate allowance carries forward; unused whole counts never accumulate.
        double allowance = 6000 * elapsed + rateRemainder;
        int step = (int)Math.Floor(allowance + 1e-9);
        rateRemainder = Math.Max(0, allowance - step);
        int dx = Math.Clamp((int)Math.Round(tx) - virtualX, -step, step);
        int dy = Math.Clamp((int)Math.Round(ty) - virtualY, -step, step);
        if ((dx != 0 || dy != 0) && !output.Move(dx, dy))
        {
            Disable();
            Telemetry = Telemetry with { Error = "Windows rejected mouse input. Tracking disabled; no movement was recorded." };
            return;
        }
        virtualX += dx;
        virtualY += dy;
        Telemetry = Telemetry with { VirtualX = virtualX, VirtualY = virtualY, DeltaX = dx, DeltaY = dy };
    }
}
