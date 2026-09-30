using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DogsEye.Core;

namespace DogsEye.App;

public partial class MainWindow : Window
{
    private readonly SettingsService persistence;
    private AppSettings settings;
    private readonly TrackingRuntime runtime;
    private readonly StatusOverlayWindow overlay;
    private GlobalKeyboardService? keyboard;
    private string? keyboardError;
    private readonly DispatcherTimer timer;
    private readonly DispatcherTimer settingsTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool settingsPending;
    private readonly Dictionary<string, TextBox> fields = [];
    private readonly bool smoke;
    private bool capturing, closing;
    private DateTime smokeStarted;
    private int smokeSamples;
    private long? smokeTimestamp;
    private readonly HashSet<TrackingState> smokeStates = [];
    private bool smokeCalibrated, smokeRecentered;
    private bool? smokeStartupOff;

    public MainWindow(bool smoke = false)
    {
        InitializeComponent();
        this.smoke = smoke;
        string directory = smoke ? Path.Combine(Environment.CurrentDirectory, "artifacts", "smoke-settings")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DogsEye");
        Directory.CreateDirectory(directory);
        persistence = new(directory);
        settings = persistence.Load(out string? warning);
        if (smoke) settings = settings with { AudioEnabled = false };
        AudioCheck.IsChecked = settings.AudioEnabled;
        OverlayCheck.IsChecked = settings.Overlay.Enabled;
        TopmostCheck.IsChecked = settings.Overlay.Topmost;
        LockedCheck.IsChecked = settings.Overlay.Locked;
        AdaptiveCheck.IsChecked = settings.AdaptiveSmoothing;
        LookCurveXEditor.Curve = settings.LookCurveX;
        LookCurveYEditor.Curve = settings.LookCurveY;
        string logPath = Path.Combine(directory, "dogseye.log");
        if (File.Exists(logPath) && new FileInfo(logPath).Length > 1_000_000) File.Move(logPath, logPath + ".previous", true);
        runtime = new(settings, logPath, smoke);
        if (smoke) runtime.SuspendKeyboard(true);
        SourceInitialized += (_, _) =>
        {
            try
            {
                keyboard = new GlobalKeyboardService(new WindowInteropHelper(this).Handle, settings.ActivationKey);
                keyboard.KeyChanged += runtime.SetActivationKey;
                keyboard.Error += error => keyboardError = error;
            }
            catch (System.ComponentModel.Win32Exception error) { keyboardError = error.Message; }
        };
        overlay = new(settings.Overlay);
        overlay.PositionSaved += (left, top) =>
        {
            settings = settings with { Overlay = settings.Overlay with { Left = left, Top = top } };
            SaveSettings();
        };
        BuildFields();
        settingsTimer.Tick += (_, _) => SavePendingSettings();
        foreach (var field in fields.Values)
        {
            field.TextChanged += (_, _) => QueueSettingsSave();
            field.LostKeyboardFocus += (_, _) => { if (settingsPending) SavePendingSettings(); };
        }
        AdaptiveCheck.Click += (_, _) => QueueSettingsSave();
        LookCurveXEditor.CurveChanged += (_, _) => QueueSettingsSave();
        LookCurveYEditor.CurveChanged += (_, _) => QueueSettingsSave();
        UpdateKeyLabel();
        Visualizer.Settings = settings;
        if (warning is not null) MessageLabel.Text = warning;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => { overlay.Apply(settings.Overlay); timer.Start(); smokeStarted = DateTime.UtcNow; };
        PreviewKeyDown += CaptureKey;
        Deactivated += (_, _) => { if (capturing) CancelCapture(); };
        Closing += (_, _) => { if (settingsPending) SavePendingSettings(); settingsTimer.Stop(); };
        Closed += (_, _) => { closing = true; timer.Stop(); keyboard?.Dispose(); runtime.Dispose(); overlay.Close(); SaveSettings(); };
    }
    public void StopTracking() => runtime.Send(InputCommand.Disable);
    private void Refresh()
    {
        var snapshot = runtime.Latest;
        var t = snapshot.Tracking;
        string state = t.State switch { TrackingState.Disabled => "OFF", TrackingState.Calibrating => "CALIBRATING", TrackingState.Active => "ACTIVE", _ => "TRACKING LOST" };
        StateLabel.Text = state;
        StateLabel.Foreground = t.State == TrackingState.TrackingLost ? Brushes.Orange : (Brush)FindResource("AccentBrush");
        DeviceLabel.Text = snapshot.Device;
        RateLabel.Text = $"Tobii {snapshot.Rate:0} Hz · head pose {(t.PoseValid ? "valid" : "unavailable")}\nPolling {snapshot.PollingRate:0} / {snapshot.RequestedRate} Hz · {(snapshot.LiveOutput ? "Mouse" : "Preview")} {snapshot.MouseEventRate:0} events/s";
        RateLabel.ToolTip = $"{snapshot.TimerMode}. Output is sent only while moving toward a measured target.";
        ToggleButton.Content = t.State == TrackingState.Disabled ? "Enable tracking" : "Disable tracking";
        RecenterButton.IsEnabled = t.PoseValid;
        ModeLabel.Text = snapshot.LiveOutput ? "LIVE · RELATIVE MOUSE OUTPUT" : "PREVIEW · NO MOUSE OUTPUT";
        KeyProgress.Value = snapshot.KeyHeldMs / settings.HoldDurationMs;
        KeyStatus.Text = keyboardError ?? (capturing ? "Choose a key; activation is paused." : snapshot.KeyHeld
            ? snapshot.LongPressTriggered ? "Toggle sent · release the key." : $"Key detected · hold {snapshot.KeyHeldMs / 1000:0.0} / {settings.HoldDurationMs / 1000.0:0.0} seconds"
            : keyboard?.Registered == true ? "Global keyboard ready · waiting for the bound key." : "Global keyboard unavailable.");
        GridCaption.Text = t.PoseValid ? (t.State == TrackingState.Active ? "Blue: head motion · yellow: mouse response target" : "Blue: head motion · yellow: response preview · no mouse output") : "Tracking unavailable · showing last valid head/response positions";
        Visualizer.Telemetry = t;
        Visualizer.InvalidateVisual();
        overlay.UpdateState(t.State);
        var pose = t.Raw;
        Diagnostics.Text = string.Create(CultureInfo.InvariantCulture,
            $"Raw yaw / pitch   {pose?.Yaw ?? 0,8:+0.00;-0.00;0.00}°  {pose?.Pitch ?? 0,8:+0.00;-0.00;0.00}°\nRaw roll          {pose?.Roll ?? 0,8:+0.00;-0.00;0.00}°\nRelative yaw/pitch{t.RelativeYaw,9:+0.00;-0.00;0.00}°  {t.RelativePitch,8:+0.00;-0.00;0.00}°\nProcessed X / Y   {t.ProcessedX,8:0.000}   {t.ProcessedY,8:0.000}\nTarget X / Y      {t.TargetX,8:0.0}   {t.TargetY,8:0.0}\nVirtual X / Y     {t.VirtualX,8}   {t.VirtualY,8}\n{(snapshot.LiveOutput ? "Mouse" : "Preview")} delta     {t.DeltaX,8}   {t.DeltaY,8}");
        if (snapshot.Error is not null) MessageLabel.Text = snapshot.Error;
        if (smoke) SmokeTick(snapshot);
    }
    private void ToggleClick(object sender, RoutedEventArgs e) => runtime.Send(InputCommand.Toggle);
    private void RecenterClick(object sender, RoutedEventArgs e) => runtime.Send(InputCommand.Recenter);
    private void CaptureClick(object sender, RoutedEventArgs e)
    {
        if (capturing) { CancelCapture(); return; }
        capturing = true;
        CaptureButton.Content = "Cancel";
        KeyLabel.Text = "Press a key… (Esc cancels)";
        runtime.SuspendKeyboard(true);
        Focus();
    }
    private void CancelCapture()
    {
        capturing = false;
        CaptureButton.Content = "Change";
        runtime.SuspendKeyboard(false);
        UpdateKeyLabel();
    }
    private void CaptureKey(object sender, KeyEventArgs e)
    {
        if (!capturing || e.IsRepeat) return;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        e.Handled = true;
        if (key == Key.Escape) { CancelCapture(); return; }
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk < 8 || vk > 254) return;
        settings = settings with { ActivationKey = vk };
        keyboard?.Rebind(vk);
        runtime.Configure(settings, LiveOutputCheck.IsChecked == true);
        CancelCapture();
        SaveSettings();
    }
    private void UpdateKeyLabel()
    {
        KeyLabel.Text = settings.ActivationKey == 0xC0 ? "` (Oem3)" : $"{KeyInterop.KeyFromVirtualKey(settings.ActivationKey)} (0x{settings.ActivationKey:X2})";
        KeyHint.Text = $"Hold {settings.HoldDurationMs / 1000.0:0.0} seconds to toggle. Tap to recenter, including while off.";
    }
    private void OutputClick(object sender, RoutedEventArgs e)
    {
        if (smoke) LiveOutputCheck.IsChecked = false;
        runtime.Configure(settings, LiveOutputCheck.IsChecked == true);
        MessageLabel.Text = LiveOutputCheck.IsChecked == true ? "Mouse output selected. Tracking stopped; enable tracking when ready." : "Preview selected. Tracking stopped; no Windows mouse output.";
    }
    private void PreferencesClick(object sender, RoutedEventArgs e)
    {
        settings = settings with { AudioEnabled = AudioCheck.IsChecked == true, Overlay = settings.Overlay with
        {
            Enabled = OverlayCheck.IsChecked == true, Topmost = TopmostCheck.IsChecked == true, Locked = LockedCheck.IsChecked == true
        } };
        overlay.Apply(settings.Overlay);
        // Only the audio preference affects the runtime. Keep overlay movement independent.
        if (ReferenceEquals(sender, AudioCheck)) runtime.UpdateSettings(settings);
        SaveSettings();
    }
    private void ResetOverlayClick(object sender, RoutedEventArgs e) => overlay.ResetPosition();
    private void BuildFields()
    {
        void AddFields(System.Windows.Controls.Primitives.UniformGrid grid, params (string Key, string Label)[] definitions)
        {
            foreach (var (key, label) in definitions)
            {
                var stack = new StackPanel { Margin = new Thickness(0, 8, 16, 12) };
                stack.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5), FontSize = 12, Foreground = (Brush)FindResource("MutedBrush") });
                var input = new TextBox { Text = Convert.ToString(typeof(AppSettings).GetProperty(key)!.GetValue(settings), CultureInfo.CurrentCulture) };
                System.Windows.Automation.AutomationProperties.SetName(input, label);
                fields[key] = input;
                stack.Children.Add(input);
                grid.Children.Add(stack);
            }
        }
        void Section(string title, string description, params (string Key, string Label)[] definitions)
        {
            SettingsSections.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 6), Foreground = (Brush)FindResource("AccentBrush") });
            SettingsSections.Children.Add(new TextBlock { Text = description, Style = (Style)FindResource("Muted") });
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            SettingsSections.Children.Add(grid);
            AddFields(grid, definitions);
        }
        AddFields(CommonFields,
            (nameof(AppSettings.Smoothing), "Smoothing · 0 to 1 (0 = off)"),
            (nameof(AppSettings.YawDeadzone), "Left / right deadzone · degrees"),
            (nameof(AppSettings.PitchDeadzone), "Up / down deadzone · degrees"));
        Section("HEAD MOVEMENT TO REACH FULL TURN", "Physical degrees from your calibrated centre, including the deadzone. Lower = more sensitive; higher = less sensitive. These are not in-game camera angles. For example, Down 15° reaches full downward turn at 15° below centre.",
            (nameof(AppSettings.HeadLeft), "Head left for full turn · degrees"), (nameof(AppSettings.HeadRight), "Head right for full turn · degrees"),
            (nameof(AppSettings.HeadUp), "Head up for full turn · degrees"), (nameof(AppSettings.HeadDown), "Head down for full turn · degrees"));
        Section("CAMERA LIMITS · ADVANCED", "Mouse counts before safety factors. Tune these to the game's camera travel limits.",
            (nameof(AppSettings.CameraLeft), "Camera left · counts"), (nameof(AppSettings.CameraRight), "Camera right · counts"),
            (nameof(AppSettings.CameraUp), "Camera up · counts"), (nameof(AppSettings.CameraDown), "Camera down · counts"),
            (nameof(AppSettings.YawSafetyFactor), "Yaw usable fraction · 0.1 to 1"), (nameof(AppSettings.PitchSafetyFactor), "Pitch usable fraction · 0.1 to 1"));
        Section("TIMING AND FILTER · ADVANCED", "Activation timing, output cadence, and how quickly adaptive smoothing relaxes during turns.",
            (nameof(AppSettings.HoldDurationMs), "Toggle hold duration · milliseconds"),
            (nameof(AppSettings.PollingRateHz), "Polling / mouse output · Hz (60–1000; default 500)"),
            (nameof(AppSettings.AdaptiveResponsiveness), "Adaptive responsiveness · 0 to 1"));
    }
    private void ResetXCurveClick(object sender, RoutedEventArgs e) => LookCurveXEditor.Curve = new();
    private void ResetYCurveClick(object sender, RoutedEventArgs e) => LookCurveYEditor.Curve = new();
    private void QueueSettingsSave()
    {
        settingsPending = true;
        settingsTimer.Stop();
        settingsTimer.Start();
        SettingsSaveStatus.Text = "Editing · changes save automatically…";
    }
    private void SavePendingSettings()
    {
        settingsTimer.Stop();
        settingsPending = false;
        try
        {
            AppSettings updated = settings with { AdaptiveSmoothing = AdaptiveCheck.IsChecked == true, LookCurveX = LookCurveXEditor.Curve, LookCurveY = LookCurveYEditor.Curve };
            foreach (var (key, field) in fields)
            {
                var property = typeof(AppSettings).GetProperty(key)!;
                object parsed = property.PropertyType == typeof(int) ? (object)int.Parse(field.Text, CultureInfo.CurrentCulture) : double.Parse(field.Text, CultureInfo.CurrentCulture);
                property.SetValue(updated, parsed);
            }
            updated.Validate();
            if (updated == settings) { SettingsSaveStatus.Text = "All changes saved."; return; }
            persistence.Save(updated);
            settings = updated;
            runtime.UpdateSettings(settings);
            Visualizer.Settings = settings;
            UpdateKeyLabel();
            SettingsSaveStatus.Text = "Saved · settings are active. Tracking keeps its current centre.";
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException)
        { SettingsSaveStatus.Text = $"Changes not saved: {ex.Message} Last valid settings remain active."; }
    }
    private void SaveSettings()
    {
        try { persistence.Save(settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { if (!closing) MessageLabel.Text = $"Could not save settings: {ex.Message}"; }
    }
    private void SmokeTick(RuntimeSnapshot snapshot)
    {
        smokeStartupOff ??= snapshot.Tracking.State == TrackingState.Disabled && overlay.StateText == "[OFF]";
        smokeStates.Add(snapshot.Tracking.State);
        if (snapshot.Tracking.Raw is { } p && snapshot.Tracking.PoseValid && p.Timestamp != smokeTimestamp) { smokeSamples++; smokeTimestamp = p.Timestamp; }
        double elapsed = (DateTime.UtcNow - smokeStarted).TotalSeconds;
        if (elapsed > 3 && !smokeCalibrated) { smokeCalibrated = true; runtime.Send(InputCommand.Toggle); }
        if (elapsed > 6 && !smokeRecentered && snapshot.Tracking.State == TrackingState.Active) { smokeRecentered = true; runtime.Send(InputCommand.Recenter); }
        if (elapsed < 10) return;
        timer.Stop();
        string output = Path.Combine(Environment.CurrentDirectory, "artifacts");
        Directory.CreateDirectory(output);
        var content = (FrameworkElement)Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, "smoke-window.png"))) encoder.Save(file);
        TrackingSettingsExpander.IsExpanded = true;
        UpdateLayout();
        ((ScrollViewer)Content).ScrollToBottom();
        UpdateLayout();
        var settingsBitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        settingsBitmap.Render(content);
        var settingsEncoder = new PngBitmapEncoder();
        settingsEncoder.Frames.Add(BitmapFrame.Create(settingsBitmap));
        using (var file = File.Create(Path.Combine(output, "smoke-settings.png"))) settingsEncoder.Save(file);
        bool noActivate = overlay.HasNoActivateStyle;
        overlay.Apply(settings.Overlay with { Locked = true });
        bool clickThrough = overlay.HasClickThroughStyle;
        overlay.Apply(settings.Overlay with { Locked = false });
        bool unlocked = !overlay.HasClickThroughStyle;
        var stateLabels = new Dictionary<string, string>();
        foreach (var state in Enum.GetValues<TrackingState>()) { overlay.UpdateState(state); stateLabels[state.ToString()] = overlay.StateText; }
        overlay.Apply(settings.Overlay with { Enabled = false });
        bool hidden = !overlay.IsVisible;
        overlay.Apply(settings.Overlay);
        overlay.UpdateState(snapshot.Tracking.State);
        bool recoveredPosition = false;
        var recoveryOverlay = new StatusOverlayWindow(new() { Left = -99999, Top = -99999, Enabled = true });
        recoveryOverlay.PositionSaved += (x, y) => recoveredPosition = NativeMethods.Monitors().Any(m => x >= m.Work.Left && x < m.Work.Right && y >= m.Work.Top && y < m.Work.Bottom);
        recoveryOverlay.Show();
        recoveryOverlay.UpdateLayout();
        recoveryOverlay.RestorePosition();
        recoveryOverlay.Close();
        File.WriteAllText(Path.Combine(output, "smoke-report.json"), JsonSerializer.Serialize(new
        {
            snapshot.Device, snapshot.Rate, snapshot.LiveOutput, snapshot.Error, snapshot.StreamStatus, ValidSamples = smokeSamples,
            snapshot.PollingRate, snapshot.MouseEventRate, snapshot.RequestedRate, snapshot.TimerMode,
            settings.AdaptiveSmoothing, settings.Smoothing, settings.AdaptiveResponsiveness,
            States = smokeStates.Select(s => s.ToString()).ToArray(), RecenterRequested = smokeRecentered,
            OverlayNoActivate = noActivate, OverlayClickThrough = clickThrough, OverlayUnlock = unlocked,
            StartupOff = smokeStartupOff, GlobalKeyboardRegistered = keyboard?.Registered, OverlayHidden = hidden, OverlayPositionRecovery = recoveredPosition, StateLabels = stateLabels,
            OverlayText = overlay.StateText, MouseInputStructSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>()
        }, new JsonSerializerOptions { WriteIndented = true }));
        Close();
    }
}





