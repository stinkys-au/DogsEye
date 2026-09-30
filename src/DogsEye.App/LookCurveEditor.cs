using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DogsEye.Core;

namespace DogsEye.App;

public sealed class LookCurveEditor : FrameworkElement
{
    private LookResponseCurve curve = new();
    public event EventHandler? CurveChanged;
    public LookResponseCurve Curve
    {
        get => curve;
        set
        {
            value.Validate("Look");
            if (curve == value) return;
            curve = value;
            InvalidateVisual();
            CurveChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private Rect Plot => new(42, 28, Math.Max(1, ActualWidth - 60), Math.Max(1, ActualHeight - 82));
    private Point Handle => new(Plot.Left + Curve.Head * Plot.Width, Plot.Bottom - Curve.Turn * Plot.Height);

    public LookCurveEditor()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        ToolTip = "Drag the point. Up = more turning; down = less. Arrow keys move 1%, Shift + arrows 5%. Home resets.";
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var plot = Plot;
        var muted = new SolidColorBrush(Color.FromRgb(165, 183, 207));
        var accent = new SolidColorBrush(Color.FromRgb(102, 222, 194));
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(43, 64, 89)), 1);
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(14, 25, 41)), new Pen(IsKeyboardFocused ? accent : muted, 1), plot, 4, 4);
        Point At(double x, double y) => new(plot.Left + x * plot.Width, plot.Bottom - y * plot.Height);
        for (int i = 1; i < 4; i++)
        {
            dc.DrawLine(grid, At(i / 4.0, 0), At(i / 4.0, 1));
            dc.DrawLine(grid, At(0, i / 4.0), At(1, i / 4.0));
        }
        dc.DrawLine(new Pen(muted, 1) { DashStyle = DashStyles.Dash }, At(0, 0), At(1, 1));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(At(0, 0), false, false);
            for (int i = 1; i <= 200; i++) context.LineTo(At(i / 200.0, Curve.Evaluate(i / 200.0)), true, false);
        }
        dc.DrawGeometry(null, new Pen(accent, 2.5), geometry);
        dc.DrawEllipse(accent, new Pen(Brushes.White, 2), Handle, 8, 8);
        void Label(string text, double x, double y)
        {
            dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 12, muted, VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x, y));
        }
        Label("Camera turn", plot.Left, 3);
        Label("100%", 0, plot.Top - 7);
        Label("0", 24, plot.Bottom - 9);
        Label("0", plot.Left, plot.Bottom + 5);
        Label("100%", plot.Right - 32, plot.Bottom + 5);
        Label("Head movement after deadzone", plot.Left + 24, plot.Bottom + 5);
        Label($"Point: {Curve.Head:P0} head → {Curve.Turn:P0} turn", plot.Left, plot.Bottom + 29);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if ((e.GetPosition(this) - Handle).Length <= 20)
        {
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured) return;
        if (e.LeftButton != MouseButtonState.Pressed) { ReleaseMouseCapture(); return; }
        var point = e.GetPosition(this);
        SetPoint((point.X - Plot.Left) / Plot.Width, (Plot.Bottom - point.Y) / Plot.Height);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) { ReleaseMouseCapture(); e.Handled = true; }
    }

    private void SetPoint(double head, double turn) => Curve = new()
    {
        Head = Math.Round(Math.Clamp(head, .1, .9), 2),
        Turn = Math.Round(Math.Clamp(turn, .1, .9), 2)
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? .05 : .01;
        switch (e.Key)
        {
            case Key.Left: SetPoint(Curve.Head - step, Curve.Turn); break;
            case Key.Right: SetPoint(Curve.Head + step, Curve.Turn); break;
            case Key.Up: SetPoint(Curve.Head, Curve.Turn + step); break;
            case Key.Down: SetPoint(Curve.Head, Curve.Turn - step); break;
            case Key.Home: Curve = new(); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }
}
