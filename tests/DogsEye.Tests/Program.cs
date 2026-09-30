using DogsEye.Core;

var tests = new (string Name, Action Run)[]
{
    ("Recenter while off zeros the preview and never enables mouse output", () =>
    {
        var h = new Harness(new() { Smoothing = 0 });
        h.Feed(15, -12, 10);
        Check(h.Controller.Recenter(h.Time));
        int version = h.Controller.Telemetry.CentreVersion;
        Check(h.Controller.State == TrackingState.Disabled && version > 0);
        Near(h.Controller.Telemetry.RelativeYaw, 0); Near(h.Controller.Telemetry.RelativePitch, 0);
        Near(h.Controller.Telemetry.ProcessedX, 0); Near(h.Controller.Telemetry.ProcessedY, 0);
        h.Feed(25, -20, 20);
        Near(h.Controller.Telemetry.RelativeYaw, 10); Near(h.Controller.Telemetry.RelativePitch, -8);
        Check(h.Controller.Telemetry.ProcessedX > 0 && h.Controller.Telemetry.ProcessedY < 0);
        Check(h.Output.Moves.Count == 0 && h.Controller.State == TrackingState.Disabled);
        Check(h.Controller.Recenter(h.Time));
        Check(h.Controller.Telemetry.CentreVersion == version + 1);
        Near(h.Controller.Telemetry.ProcessedX, 0); Near(h.Controller.Telemetry.ProcessedY, 0);
        h.Time += .2; Check(!h.Controller.Recenter(h.Time));
    }),
    ("Neutral response follows head movement and an early raised point increases mouse output", () =>
    {
        var neutral = new HeadTrackingProcessor(new() { Smoothing = 0, YawDeadzone = 0, PitchDeadzone = 0 });
        var raised = new HeadTrackingProcessor(new() { Smoothing = 0, YawDeadzone = 0, PitchDeadzone = 0,
            LookCurveX = new() { Head = .2, Turn = .5 }, LookCurveY = new() { Head = .2, Turn = .5 } });
        var n = neutral.Process(6, 4, .01); var r = raised.Process(6, 4, .01);
        Near(n.X * 30, 6); Near(n.Y * 20, 4);
        Near(r.X, .5); Near(r.Y, .5);
        Check(r.X > n.X && r.Y > n.Y);
        for (int i = 1; i < 100; i++)
        {
            double x = i / 100.0;
            Near(new LookResponseCurve().Evaluate(x), x);
            Check(new LookResponseCurve { Head = .2, Turn = .5 }.Evaluate(x) > x);
        }
    }),
    ("Live settings retain calibration and position without disabling or immediate output", () =>
    {
        var initial = new AppSettings { Smoothing = 0 };
        var h = new Harness(initial);
        h.Controller.Toggle(); h.Feed(8, -5, 10);
        Near(h.Controller.Telemetry.NeutralYaw!.Value, 8);
        Near(h.Controller.Telemetry.NeutralPitch!.Value, -5);
        h.Feed(20, -15, 80);
        int count = h.Output.Moves.Count;
        int x = h.Controller.Telemetry.VirtualX, y = h.Controller.Telemetry.VirtualY;
        var updated = initial with { HeadDown = 40, LookCurveX = new() { Turn = .7 } };
        h.Controller.Configure(updated);
        Check(h.Controller.State == TrackingState.Active && h.Output.Moves.Count == count);
        Check(h.Controller.Telemetry.VirtualX == x && h.Controller.Telemetry.VirtualY == y);
        h.Tick(.004); Check(h.Output.Moves.Count == count);
        h.Feed(20, -15, 80);
        Near(h.Controller.Telemetry.RelativeYaw, 12); Near(h.Controller.Telemetry.RelativePitch, -10);
        var expected = new HeadTrackingProcessor(updated).Process(12, -10, .01);
        Near(h.Controller.Telemetry.ProcessedX, expected.X); Near(h.Controller.Telemetry.ProcessedY, expected.Y);
        h.Controller.Disable(); h.Controller.Configure(initial);
        Check(h.Controller.State == TrackingState.Disabled);
        Near(h.Controller.Telemetry.NeutralYaw!.Value, 8);
        Near(h.Controller.Telemetry.NeutralPitch!.Value, -5);
    }),
    ("Head ranges measure from centre and larger ranges reduce sensitivity", () =>
    {
        var small = new HeadTrackingProcessor(new() { Smoothing = 0, HeadDown = 15, PitchDeadzone = 2 });
        var large = new HeadTrackingProcessor(new() { Smoothing = 0, HeadDown = 90, PitchDeadzone = 2 });
        Near(small.Process(0, -2, .01).Y, 0);
        Near(small.Process(0, -15, .01).Y, -1);
        Check(Math.Abs(large.Process(0, -15, .01).Y) < Math.Abs(small.Process(0, -15, .01).Y));
        Near(small.Process(0, 10, .01).Y, large.Process(0, 10, .01).Y);
    }),
    ("Single-point S curves pass through the point and remain smooth and bounded", () =>
    {
        foreach (double head in new[] { .1, .25, .5, .75, .9 })
        foreach (double turn in new[] { .1, .25, .5, .75, .9 })
        {
            var curve = new LookResponseCurve { Head = head, Turn = turn };
            Near(curve.Evaluate(0), 0); Near(curve.Evaluate(1), 1);
            Near(curve.Evaluate(head), turn);
            double previous = 0;
            for (int i = 0; i <= 1000; i++)
            {
                double current = curve.Evaluate(i / 1000.0);
                Check(current >= previous - 1e-12 && current <= 1); previous = current;
            }
            const double epsilon = 1e-6;
            double left = (turn - curve.Evaluate(head - epsilon)) / epsilon;
            double right = (curve.Evaluate(head + epsilon) - turn) / epsilon;
            Near(left, right, .001);
        }
        var standard = new LookResponseCurve();
        Near(standard.Evaluate(.25), .25); Near(standard.Evaluate(.75), .75);
    }),
    ("X and Y curve points independently control signed head response", () =>
    {
        var processor = new HeadTrackingProcessor(new() { Smoothing = 0,
            LookCurveX = new() { Head = .5, Turn = .3 }, LookCurveY = new() { Head = .5, Turn = .7 } });
        var half = processor.Process(16.25, 11, .01);
        Near(half.X, .3); Near(half.Y, .7);
        var negative = processor.Process(-16.25, -11, .01);
        Near(negative.X, -.3); Near(negative.Y, -.7);
        var centre = processor.Process(2, 1, .01); Near(centre.X, 0); Near(centre.Y, 0);
        var limit = processor.Process(90, -90, .01); Near(limit.X, 1); Near(limit.Y, -1);
    }),
    ("Curve points validate and older settings adopt default S curves", () =>
    {
        var legacy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"LookTurnCurve\":1.65,\"CameraLeft\":2450}")!;
        Check(legacy.LookCurveX == new LookResponseCurve() && legacy.LookCurveY == new LookResponseCurve() && legacy.CameraLeft == 2450);
        foreach (double value in new[] { 0, .09, .91, double.NaN, double.PositiveInfinity })
        foreach (var curve in new[] { new LookResponseCurve { Head = value }, new LookResponseCurve { Turn = value } })
        foreach (var settings in new[] { new AppSettings { LookCurveX = curve }, new AppSettings { LookCurveY = curve } })
        {
            bool rejected = false;
            try { settings.Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected);
        }
    }),
    ("Startup stays off and emits nothing", () =>
    {
        var h = new Harness(); h.Feed(30, 20, 100);
        Check(h.Controller.State == TrackingState.Disabled && h.Output.Moves.Count == 0);
    }),
    ("Calibration needs fresh samples and emits nothing", () =>
    {
        var h = new Harness(); h.Controller.Toggle();
        for (int i = 0; i < 30; i++) { h.Time += .01; h.Controller.Update(new(1, 12, 8, 0), true, h.Time); }
        Check(h.Controller.State != TrackingState.Active);
        h.Feed(12, 8, 8); Check(h.Controller.State == TrackingState.Active);
        Check(h.Output.Moves.Count == 0); h.Feed(12, 8); Check(h.Controller.Telemetry.TargetX == 0);
    }),
    ("Calibration tolerates an outlier", () =>
    {
        var h = new Harness(); h.Controller.Toggle(); h.Feed(0, 0, 3); h.Feed(55, -40); h.Feed(0, 0, 6);
        Check(h.Controller.State == TrackingState.Active && h.Controller.Telemetry.TargetX == 0 && h.Controller.Telemetry.TargetY == 0);
    }),
    ("Deadzone is an exact zero", () =>
    {
        var h = Harness.Active(); h.Feed(2.4, -1.9, 50); Check(h.Output.Moves.Count == 0);
    }),
    ("Directional limits and pitch sign", () =>
    {
        var h = Harness.Active(new() { Smoothing = 0, CameraLeft = 1000, CameraRight = 2000, CameraUp = 900, CameraDown = 700 });
        h.Feed(60, 60, 100); Near(h.Controller.Telemetry.TargetX, 1840); Near(h.Controller.Telemetry.TargetY, -810);
        h.Feed(-60, -60, 100); Near(h.Controller.Telemetry.TargetX, -920); Near(h.Controller.Telemetry.TargetY, 630);
    }),
    ("Holding a maximum target has no continuing movement", () =>
    {
        var h = Harness.Active(); h.Feed(50, 40, 400); int count = h.Output.Moves.Count;
        h.Feed(70, 60, 500); Check(h.Output.Moves.Count == count);
    }),
    ("Returning to neutral returns virtual target to zero", () =>
    {
        var h = Harness.Active(); h.Feed(20, 15, 100); h.Feed(0, 0, 100);
        Check(h.Controller.Telemetry.VirtualX == 0 && h.Controller.Telemetry.VirtualY == 0);
        Check(h.Output.Moves.Sum(m => m.X) == 0 && h.Output.Moves.Sum(m => m.Y) == 0);
    }),
    ("Recenter resets without corrective output", () =>
    {
        var h = Harness.Active(); h.Feed(20, 10, 100); int before = h.Output.Moves.Count;
        Check(h.Controller.Recenter(h.Time)); Check(h.Output.Moves.Count == before);
        Near(h.Controller.Telemetry.NeutralYaw!.Value, 20);
        Near(h.Controller.Telemetry.NeutralPitch!.Value, 10);
        Check(h.Controller.Telemetry.VirtualX == 0 && h.Controller.Telemetry.ProcessedX == 0);
        h.Feed(20, 10, 50); Check(h.Output.Moves.Count == before);
    }),
    ("Disable emits no centering and activation uses a fresh zero", () =>
    {
        var h = Harness.Active(); h.Feed(20, 10, 100); int before = h.Output.Moves.Count;
        h.Controller.Disable(); h.Feed(-20, -10, 30); Check(h.Output.Moves.Count == before);
        h.Controller.Toggle(); h.Feed(-20, -10, 15); Check(h.Output.Moves.Count == before);
        Check(h.Controller.State == TrackingState.Active);
    }),
    ("Loss immediately stops output and retains session", () =>
    {
        var h = Harness.Active(); h.Feed(10, 5, 80); double target = h.Controller.Telemetry.TargetX;
        int before = h.Output.Moves.Count;
        h.Time += .01; h.Controller.Update(null, false, h.Time);
        Check(h.Controller.State == TrackingState.TrackingLost); Check(!h.Controller.Recenter(h.Time));
        h.Feed(10, 5); Check(h.Controller.State == TrackingState.Active && h.Output.Moves.Count == before);
        h.Feed(10, 5, 40); Near(h.Controller.Telemetry.TargetX, target, .01);
    }),
    ("Normal gaps between SDK packets do not reset calibration", () =>
    {
        var h = new Harness(); h.Controller.Toggle();
        for (int i = 0; i < 10; i++)
        {
            h.Feed(0, 0); h.Time += .005; h.Controller.Update(null, true, h.Time);
        }
        Check(h.Controller.State == TrackingState.Active && h.Output.Moves.Count == 0);
        h.Time += .2; h.Controller.Update(null, true, h.Time);
        Check(h.Controller.State == TrackingState.TrackingLost);
    }),
    ("Stale timestamps cannot keep tracking active", () =>
    {
        var h = Harness.Active(); var sample = new HeadPose(10000, 10, 0, 0);
        h.Controller.Update(sample, true, h.Time);
        int count = h.Output.Moves.Count;
        h.Time += .2; h.Controller.Update(sample, true, h.Time);
        Check(h.Controller.State == TrackingState.TrackingLost && h.Output.Moves.Count == count);
    }),
    ("Non-finite samples stop output", () =>
    {
        var h = Harness.Active(); h.Time += .01; h.Controller.Update(new(10000, double.NaN, 0, 0), true, h.Time);
        Check(h.Controller.State == TrackingState.TrackingLost && h.Output.Moves.Count == 0);
    }),
    ("Recovery movement is rate bounded", () =>
    {
        var h = Harness.Active(new() { Smoothing = 0 }); h.Controller.Update(null, false, h.Time);
        h.Feed(30, 20); Check(h.Output.Moves.Count == 0);
        h.Feed(30, 20); Check(h.Output.Moves.All(m => Math.Abs(m.X) <= 61 && Math.Abs(m.Y) <= 61));
    }),
    ("Output rejection disables and does not advance virtual position", () =>
    {
        var h = Harness.Active(); h.Output.Accept = false; h.Feed(20, 0);
        Check(h.Controller.State == TrackingState.Disabled && h.Controller.Telemetry.VirtualX == 0 && h.Controller.Telemetry.Error is not null);
    }),
    ("Sub-count target changes accumulate", () =>
    {
        var h = Harness.Active(new() { Smoothing = 0, YawDeadzone = 0, CameraRight = 30, YawSafetyFactor = 1 });
        for (int i = 0; i <= 50; i++) h.Feed(i / 10.0, 0);
        h.Feed(5, 0, 10); // Let the final measured target settle after the fractional steps.
        Check(h.Output.Moves.Sum(m => m.X) == 5); // Neutral curve: 5 / 30 * 30 = 5 counts.
    }),
    ("Mouse output continues between 33 Hz packets at the configured cadence", () =>
    {
        foreach (int hz in new[] { 125, 250, 500, 1000 })
        {
            var h = Harness.Active(new() { Smoothing = 0, PollingRateHz = hz });
            h.Time += .03;
            h.Controller.Update(new(9000, 30, 0, 0), true, h.Time);
            h.Controller.AdvanceOutput(h.Time);
            int before = h.Output.Moves.Count;
            for (int i = 0; i < hz / 50; i++) h.Tick(1.0 / hz);
            Check(h.Output.Moves.Count - before == hz / 50);
            Check(h.Output.Moves.Skip(before).All(m => Math.Abs(m.X) <= Math.Ceiling(6000.0 / hz)));
            Check(h.Controller.Telemetry.VirtualX <= h.Controller.Telemetry.TargetX);
        }
    }),
    ("Interpolation reaches the measured target and never extrapolates", () =>
    {
        var h = Harness.Active(new() { Smoothing = 0, CameraRight = 100, YawSafetyFactor = 1 });
        h.Time += .03; h.Controller.Update(new(9000, 30, 0, 0), true, h.Time);
        h.Controller.AdvanceOutput(h.Time);
        for (int i = 0; i < 25; i++) { h.Tick(.004); Check(h.Controller.Telemetry.VirtualX is >= 0 and <= 100); }
        Near(h.Controller.Telemetry.VirtualX, 100);
        int before = h.Output.Moves.Count;
        for (int i = 0; i < 50; i++) h.Tick(.004);
        Check(h.Output.Moves.Count == before && h.Controller.State == TrackingState.TrackingLost);
    }),
    ("Loss, disable and recenter cancel movement queued between packets", () =>
    {
        foreach (string command in new[] { "loss", "disable", "recenter" })
        {
            var h = Harness.Active(new() { Smoothing = 0 }); h.Feed(30, 20);
            Check(h.Output.Moves.Count > 0 && h.Controller.Telemetry.VirtualX < h.Controller.Telemetry.TargetX);
            int before = h.Output.Moves.Count;
            if (command == "loss") h.Controller.Update(null, false, h.Time);
            else if (command == "disable") h.Controller.Disable();
            else Check(h.Controller.Recenter(h.Time));
            for (int i = 0; i < 20; i++) h.Tick(.004);
            Check(h.Output.Moves.Count == before);
        }
    }),
    ("Output alone checks sample freshness and does not catch up after a stall", () =>
    {
        var h = Harness.Active(new() { Smoothing = 0 }); h.Feed(30, 20);
        int before = h.Output.Moves.Count;
        h.Time += .2; h.Controller.AdvanceOutput(h.Time);
        Check(h.Controller.State == TrackingState.TrackingLost && h.Output.Moves.Count == before);
        h.Feed(30, 20); Check(h.Output.Moves.Count == before);
        h.Feed(30, 20); Check(h.Output.Moves.Skip(before).All(m => Math.Abs(m.X) <= 12 && Math.Abs(m.Y) <= 12));
    }),
    ("A reversed target settles without overshoot or continued motion", () =>
    {
        var h = Harness.Active(new() { Smoothing = 0, CameraRight = 100, CameraLeft = 100, YawSafetyFactor = 1 });
        h.Feed(30, 0); h.Feed(-30, 0);
        for (int i = 0; i < 20; i++) { h.Tick(.004); Check(Math.Abs(h.Controller.Telemetry.VirtualX) <= 100); }
        Near(h.Controller.Telemetry.VirtualX, -100);
        int before = h.Output.Moves.Count; h.Tick(.004); Check(h.Output.Moves.Count == before);
    }),
    ("Output cadence does not change the head filter", () =>
    {
        var a = Harness.Active(); var b = Harness.Active();
        for (int i = 0; i < 20; i++)
        {
            a.Time += .03; b.Time += .03;
            var pose = new HeadPose(9000 + i, i, 0, 0);
            a.Controller.Update(pose, true, a.Time); b.Controller.Update(pose, true, b.Time);
            a.Controller.AdvanceOutput(a.Time);
            for (int j = 0; j < 10; j++) b.Controller.AdvanceOutput(b.Time + j * .002);
            Near(a.Controller.Telemetry.ProcessedX, b.Controller.Telemetry.ProcessedX);
        }
    }),
    ("Polling configuration rejects rates outside supported bounds", () =>
    {
        foreach (int hz in new[] { 0, 59, 1001, int.MaxValue })
        {
            bool rejected = false;
            try { (new AppSettings { PollingRateHz = hz }).Validate(); } catch (ArgumentException) { rejected = true; }
            Check(rejected);
        }
        (new AppSettings { PollingRateHz = 60 }).Validate();
        (new AppSettings { PollingRateHz = 500 }).Validate();
        (new AppSettings { PollingRateHz = 1000 }).Validate();
        Check(new AppSettings().PollingRateHz == 500);
    }),
    ("Filter reduces alternating head noise", () =>
    {
        var filtered = new HeadTrackingProcessor(new() { Smoothing = .4 });
        var raw = new HeadTrackingProcessor(new() { Smoothing = 0 });
        List<double> f = [], r = [];
        for (int i = 0; i < 200; i++) { double yaw = i % 2 == 0 ? 10 : 14; f.Add(filtered.Process(yaw, 0, .01).X); r.Add(raw.Process(yaw, 0, .01).X); }
        double Variance(List<double> a) { var tail = a.Skip(100).ToArray(); double mean = tail.Average(); return tail.Average(v => (v - mean) * (v - mean)); }
        Check(Variance(f) < Variance(r) / 10);
    }),
    ("Adaptive filtering reduces stationary jitter and fast-turn lag at 33 Hz", () =>
    {
        const double dt = 1.0 / 33;
        var basis = new AppSettings { YawDeadzone = 0, Smoothing = .5 };
        var adaptive = new HeadTrackingProcessor(basis);
        var fixedFilter = new HeadTrackingProcessor(basis with { AdaptiveSmoothing = false });
        List<double> adaptiveNoise = [], fixedNoise = [];
        for (int i = 0; i < 240; i++)
        {
            double angle = 10 + (i % 2 == 0 ? .12 : -.12);
            double a = adaptive.Process(angle, 0, dt).X * 30, f = fixedFilter.Process(angle, 0, dt).X * 30;
            if (i >= 180) { double target = basis.LookCurveX.Evaluate(10.0 / 30) * 30; adaptiveNoise.Add(a - target); fixedNoise.Add(f - target); }
        }
        double Rms(List<double> values) => Math.Sqrt(values.Average(v => v * v));
        double noiseRatio = Rms(adaptiveNoise) / Rms(fixedNoise);
        double aLag = 0, fLag = 0;
        for (int i = 1; i <= 10; i++)
        {
            double angle = 10 + 60 * dt * i;
            double target = basis.LookCurveX.Evaluate(angle / 30) * 30;
            aLag += target - adaptive.Process(angle, 0, dt).X * 30;
            fLag += target - fixedFilter.Process(angle, 0, dt).X * 30;
        }
        Console.WriteLine($"  Synthetic 33 Hz: jitter ratio {noiseRatio:0.000}, fast-turn lag ratio {aLag / fLag:0.000} (adaptive/fixed)");
        Check(noiseRatio < .75 && aLag < fLag * .65);
    }),
    ("Adaptive filter behaves consistently at different sampling rates", () =>
    {
        double AtRate(int hz)
        {
            var filter = new AdaptiveTrackingFilter();
            double output = 0;
            for (int i = 1; i <= hz; i++) output = filter.Process(15.0 * i / hz, 1.0 / hz, .5, .08);
            return output;
        }
        Near(AtRate(30), AtRate(120), .4);
        Near(AtRate(60), AtRate(120), .2);
    }),
    ("Adaptive reset clears position and velocity history", () =>
    {
        var reused = new AdaptiveTrackingFilter();
        for (int i = 0; i < 100; i++) reused.Process(i % 2 == 0 ? 25 : -25, .03, .5, .08);
        reused.Reset();
        var fresh = new AdaptiveTrackingFilter();
        foreach (double angle in new double[] { 0, .1, .2, 5, 10 })
            Near(reused.Process(angle, .03, .5, .08), fresh.Process(angle, .03, .5, .08));
    }),
    ("Zero smoothing bypasses adaptive filtering", () =>
    {
        var adaptive = new HeadTrackingProcessor(new() { Smoothing = 0 });
        var fixedFilter = new HeadTrackingProcessor(new() { Smoothing = 0, AdaptiveSmoothing = false });
        foreach (double angle in new double[] { 0, 5, 20, -20, 60, -60, 0 })
        {
            var a = adaptive.Process(angle, angle, .03); var f = fixedFilter.Process(angle, angle, .03);
            Near(a.X, f.X); Near(a.Y, f.Y);
        }
    }),
    ("Existing settings retain tuning and enable adaptive smoothing", () =>
    {
        var legacy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"Smoothing\":0.5,\"CameraLeft\":1000}")!;
        Check(legacy.AdaptiveSmoothing && legacy.Smoothing == .5 && legacy.CameraLeft == 1000 && legacy.PollingRateHz == 500);
        legacy.Validate();
    }),
    ("Raw key edges preserve a tap between tracking ticks", () =>
    {
        var input = new InputCommandService(3000); List<InputCommand> commands = []; input.Command += commands.Add;
        input.Update(false, 0); input.Update(true, .001); input.Update(false, .002);
        Check(commands.SequenceEqual([InputCommand.Recenter]) && !input.IsHeld);
    }),
    ("Held-key feedback follows the toggle threshold", () =>
    {
        var input = new InputCommandService(3000); int toggles = 0; input.Command += _ => toggles++;
        input.Update(true, 0); input.Update(true, 1.5);
        Near(input.HeldMilliseconds(1.5), 1500); Check(input.IsHeld && !input.LongPressTriggered && toggles == 0);
        input.Update(true, 3); Check(input.LongPressTriggered && toggles == 1); Near(input.HeldMilliseconds(5), 3000);
        input.Update(false, 5); Near(input.HeldMilliseconds(5), 0); Check(toggles == 1);
    }),
    ("Key repeats trigger only one long press", () =>
    {
        var input = new InputCommandService(3000); List<InputCommand> commands = []; input.Command += commands.Add;
        for (int i = 0; i <= 1000; i++) input.Update(true, i / 100.0);
        input.Update(false, 10.1); Check(commands.SequenceEqual([InputCommand.Toggle]));
        input.Update(true, 11); input.Update(false, 11.1); Check(commands[^1] == InputCommand.Recenter);
    }),
    ("Threshold release is a toggle, never recenter", () =>
    {
        var input = new InputCommandService(3000); List<InputCommand> commands = []; input.Command += commands.Add;
        input.Update(true, 0); input.Update(false, 3); Check(commands.SequenceEqual([InputCommand.Toggle]));
    }),
    ("Key rebind requires a release before action", () =>
    {
        var input = new InputCommandService(3000); int count = 0; input.Command += _ => count++;
        input.Reset(); input.Update(true, 0); input.Update(true, 5); input.Update(false, 6); Check(count == 0);
        input.Update(true, 7); input.Update(false, 7.1); Check(count == 1);
    }),
    ("Settings persist negative monitor coordinates and key code", () =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "DogsEye-tests-" + Guid.NewGuid());
        try
        {
            var store = new SettingsService(folder);
            var settings = new AppSettings { ActivationKey = 0x75, CameraLeft = 2450, PollingRateHz = 1000, LookCurveX = new() { Head = .4, Turn = .6 }, LookCurveY = new() { Head = .7, Turn = .3 }, Overlay = new() { Left = -1200, Locked = true } };
            store.Save(settings); Check(store.Load(out var warning) == settings && warning is null);
            File.WriteAllText(store.SettingsPath, "invalid json"); Check(store.Load(out warning) == new AppSettings() && warning is not null);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }),
    ("Unsafe and non-finite settings are rejected", () =>
    {
        AppSettings[] invalid = [new() { HeadLeft = 1 }, new() { Smoothing = double.NaN }, new() { AdaptiveResponsiveness = double.NaN }, new() { AdaptiveResponsiveness = -1 }, new() { CameraRight = -1 }, new() { YawSafetyFactor = 2 }, new() { Overlay = new() { Left = double.PositiveInfinity } }];
        foreach (var settings in invalid) { bool rejected = false; try { settings.Validate(); } catch (ArgumentException) { rejected = true; } Check(rejected); }
    }),
    ("Calibration handles angular wrap", () =>
    {
        var h = new Harness(); h.Controller.Toggle();
        for (int i = 0; i < 8; i++) h.Feed(i % 2 == 0 ? 179.9 : -179.9, 0);
        h.Feed(180, 0, 10); Near(h.Controller.Telemetry.TargetX, 0);
    })
};
int failures = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception e) { failures++; Console.WriteLine($"FAIL {test.Name}: {e.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} acceptance tests passed.");
return failures == 0 ? 0 : 1;

static void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
static void Near(double actual, double expected, double tolerance = .0001) { if (Math.Abs(actual - expected) > tolerance) throw new Exception($"Expected {expected}, got {actual}"); }

sealed class RecordingOutput : IMouseOutput
{
    public List<(int X, int Y)> Moves { get; } = [];
    public bool Accept { get; set; } = true;
    public bool Move(int x, int y) { if (Accept) Moves.Add((x, y)); return Accept; }
}
sealed class Harness
{
    public double Time;
    private long timestamp = 10;
    public RecordingOutput Output { get; } = new();
    public TrackingController Controller { get; }
    public Harness(AppSettings? settings = null) { Controller = new(settings ?? new(), Output); }
    public void Feed(double yaw, double pitch, int times = 1)
    {
        for (int i = 0; i < times; i++)
            for (int tick = 0; tick < 5; tick++)
            {
                Time += .002;
                Controller.Update(tick == 0 ? new HeadPose(++timestamp, yaw, pitch, 0) : null, true, Time);
                Controller.AdvanceOutput(Time);
            }
    }
    public void Tick(double seconds)
    {
        Time += seconds;
        Controller.Update(null, true, Time);
        Controller.AdvanceOutput(Time);
    }
    public static Harness Active(AppSettings? settings = null) { var h = new Harness(settings); h.Controller.Toggle(); h.Feed(0, 0, 10); return h; }
}


