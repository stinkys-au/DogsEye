using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using DogsEye.Core;

namespace DogsEye.App;

internal sealed class StatusOverlayWindow : Window
{
    private readonly TextBlock label;
    private OverlaySettings settings;
    private nint handle;
    private bool positioning;
    public event Action<double, double>? PositionSaved;
    public StatusOverlayWindow(OverlaySettings settings)
    {
        this.settings = settings;
        Title = "DogsEye status";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
        label = new TextBlock { Text = "[OFF]", Foreground = Brushes.White, FontWeight = FontWeights.Bold,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 4, ShadowDepth = 1, Opacity = 1 } };
        Content = new Border { Padding = new Thickness(7, 3, 7, 3), CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)), Child = label };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowMessages);
            Apply(settings);
        };
        Loaded += (_, _) => RestorePosition();
        MouseLeftButtonDown += (_, e) =>
        {
            if (this.settings.Locked || e.ButtonState != MouseButtonState.Pressed) return;
            DragMove();
            SavePosition();
        };
    }
    private nint WindowMessages(nint hwnd, int message, nint wparam, nint lparam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return 3; } // WM_MOUSEACTIVATE / MA_NOACTIVATE
        if (message == 0x007E) Dispatcher.BeginInvoke(RestorePosition); // Display topology changed.
        return 0;
    }
    public void Apply(OverlaySettings value)
    {
        settings = value;
        Topmost = value.Topmost;
        Opacity = value.Opacity;
        label.FontSize = value.FontSize;
        if (handle != 0)
        {
            long style = NativeMethods.GetWindowLongPtr(handle, -20).ToInt64();
            style |= 0x08000000L | 0x00000080L; // NOACTIVATE, TOOLWINDOW. WPF owns the layered window.
            style = value.Locked ? style | 0x20 : style & ~0x20L;
            NativeMethods.SetWindowLongPtr(handle, -20, (nint)style);
        }
        if (value.Enabled) Show(); else Hide();
    }
    public void UpdateState(TrackingState state)
    {
        label.Text = state switch
        {
            TrackingState.Active => "[ON]",
            TrackingState.Calibrating when settings.ShowCalibrationState => "[CAL]",
            TrackingState.TrackingLost when settings.ShowTrackingLostState => "[LOST]",
            _ => "[OFF]"
        };
        label.Foreground = state switch
        {
            TrackingState.Active => new SolidColorBrush(Color.FromRgb(119, 255, 202)),
            TrackingState.TrackingLost => new SolidColorBrush(Color.FromRgb(255, 201, 106)),
            _ => Brushes.White
        };
    }
    internal void RestorePosition()
    {
        if (handle == 0 || positioning) return;
        positioning = true;
        try
        {
            NativeMethods.GetWindowRect(handle, out var current);
            int width = Math.Max(70, current.Right - current.Left), height = Math.Max(30, current.Bottom - current.Top);
            int x = (int)settings.Left, y = (int)settings.Top;
            var monitors = NativeMethods.Monitors();
            bool visible = monitors.Any(m => x < m.Work.Right && x + width > m.Work.Left && y < m.Work.Bottom && y + height > m.Work.Top);
            if (!visible)
            {
                var primary = monitors.FirstOrDefault(m => (m.Flags & 1) != 0);
                x = primary.Work.Left + 20;
                y = primary.Work.Top + 20;
            }
            NativeMethods.SetWindowPos(handle, 0, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
            SavePosition();
        }
        finally { positioning = false; }
    }
    public void ResetPosition()
    {
        var primary = NativeMethods.Monitors().FirstOrDefault(m => (m.Flags & 1) != 0);
        settings = settings with { Left = primary.Work.Left + 20, Top = primary.Work.Top + 20 };
        PositionSaved?.Invoke(settings.Left, settings.Top);
        RestorePosition();
    }
    private void SavePosition()
    {
        if (handle == 0 || !NativeMethods.GetWindowRect(handle, out var rect)) return;
        settings = settings with { Left = rect.Left, Top = rect.Top };
        PositionSaved?.Invoke(rect.Left, rect.Top);
    }
    internal bool HasClickThroughStyle => handle != 0 && (NativeMethods.GetWindowLongPtr(handle, -20).ToInt64() & 0x20) != 0;
    internal bool HasNoActivateStyle => handle != 0 && (NativeMethods.GetWindowLongPtr(handle, -20).ToInt64() & 0x08000000) != 0;
    internal string StateText => label.Text;
}
