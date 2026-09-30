using System.Text.Json;

namespace DogsEye.Core;

public sealed record AppSettings
{
    public int ActivationKey { get; init; } = 0xC0;
    public int HoldDurationMs { get; init; } = 3000;
    public int PollingRateHz { get; init; } = 500;
    public double YawDeadzone { get; init; } = 2.5;
    public double PitchDeadzone { get; init; } = 2;
    public double HeadLeft { get; init; } = 30;
    public double HeadRight { get; init; } = 30;
    public double HeadUp { get; init; } = 20;
    public double HeadDown { get; init; } = 20;
    public LookResponseCurve LookCurveX { get; init; } = new();
    public LookResponseCurve LookCurveY { get; init; } = new();
    public double Smoothing { get; init; } = 0.15;
    public bool AdaptiveSmoothing { get; init; } = true;
    public double AdaptiveResponsiveness { get; init; } = 0.1;
    public int CameraLeft { get; init; } = 3000;
    public int CameraRight { get; init; } = 3000;
    public int CameraUp { get; init; } = 1700;
    public int CameraDown { get; init; } = 1500;
    public double YawSafetyFactor { get; init; } = 0.92;
    public double PitchSafetyFactor { get; init; } = 0.90;
    public bool AudioEnabled { get; init; } = true;
    public OverlaySettings Overlay { get; init; } = new();

    public void Validate()
    {
        static void Range(double value, double min, double max, string name)
        {
            if (!double.IsFinite(value) || value < min || value > max)
                throw new ArgumentException($"{name} must be between {min} and {max}.");
        }
        Range(ActivationKey, 8, 254, "Activation key");
        Range(HoldDurationMs, 500, 10000, "Hold duration (ms)");
        Range(PollingRateHz, 60, 1000, "Polling / mouse output rate (Hz)");
        Range(YawDeadzone, 0, 30, "Yaw deadzone");
        Range(PitchDeadzone, 0, 30, "Pitch deadzone");
        Range(HeadLeft, YawDeadzone + 0.1, 90, "Head left range");
        Range(HeadRight, YawDeadzone + 0.1, 90, "Head right range");
        Range(HeadUp, PitchDeadzone + 0.1, 90, "Head up range");
        Range(HeadDown, PitchDeadzone + 0.1, 90, "Head down range");
        if (LookCurveX is null || LookCurveY is null) throw new ArgumentException("Look response curves are missing.");
        LookCurveX.Validate("X / yaw");
        LookCurveY.Validate("Y / pitch");
        Range(Smoothing, 0, 1, "Smoothing");
        Range(AdaptiveResponsiveness, 0, 1, "Adaptive responsiveness");
        Range(CameraLeft, 1, 50000, "Camera left counts");
        Range(CameraRight, 1, 50000, "Camera right counts");
        Range(CameraUp, 1, 50000, "Camera up counts");
        Range(CameraDown, 1, 50000, "Camera down counts");
        Range(YawSafetyFactor, 0.1, 1, "Yaw safety factor");
        Range(PitchSafetyFactor, 0.1, 1, "Pitch safety factor");
        if (Overlay is null) throw new ArgumentException("Overlay settings are missing.");
        Range(Overlay.Left, -100000, 100000, "Overlay left");
        Range(Overlay.Top, -100000, 100000, "Overlay top");
        Range(Overlay.FontSize, 12, 72, "Overlay font size");
        Range(Overlay.Opacity, 0.2, 1, "Overlay opacity");
    }
}

public sealed record OverlaySettings
{
    public bool Enabled { get; init; } = true;
    public bool Topmost { get; init; } = true;
    public bool Locked { get; init; }
    // Physical desktop pixels, including negative monitor coordinates.
    public double Left { get; init; } = 20;
    public double Top { get; init; } = 20;
    public double FontSize { get; init; } = 22;
    public double Opacity { get; init; } = 0.9;
    public bool ShowCalibrationState { get; init; } = true;
    public bool ShowTrackingLostState { get; init; } = true;
}

public sealed class SettingsService(string directory)
{
    public string SettingsPath => Path.Combine(directory, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public AppSettings Load(out string? warning)
    {
        warning = null;
        if (!File.Exists(SettingsPath)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                ?? throw new ArgumentException("Settings are empty.");
            settings.Validate();
            return settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            warning = $"Settings could not be loaded; using defaults. {e.Message}";
            return new();
        }
    }
    public void Save(AppSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(directory);
        string temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, SettingsPath, true);
    }
}

