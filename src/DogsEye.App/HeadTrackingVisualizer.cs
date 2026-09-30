using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DogsEye.Core;

namespace DogsEye.App;

public sealed class HeadTrackingVisualizer : FrameworkElement
{
    // Fixed domain covers every wrapped SDK angle and every allowed reach limit.
    private const double DisplayExtent = 180;
    private TrackingTelemetry telemetry = new();
    private static double Wrap(double degrees) => ((degrees + 180) % 360 + 360) % 360 - 180;
    public AppSettings Settings { get; set; } = new();
    public TrackingTelemetry Telemetry
    {
        get => telemetry;
        set
        {
            telemetry = value;
            InvalidateVisual();
        }
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth < 80 || ActualHeight < 100) return;
        // Keep the original full-width chart dimensions and a fixed angular domain,
        // rather than shrinking the entire chart or scrolling its contents.
        double w = ActualWidth - 56, h = ActualHeight - 70;
        var area = new Rect(28, 24, w, h);
        var muted = new SolidColorBrush(Color.FromRgb(104, 128, 157));
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(37, 56, 78)), 1);
        var accent = new SolidColorBrush(Color.FromRgb(102, 222, 194));
        var red = new SolidColorBrush(Color.FromRgb(255, 112, 112));
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(12, 23, 38)), new Pen(muted, 1), area, 6, 6);
        double yaw = Telemetry.Raw is { } pose ? Wrap(pose.Yaw - (Telemetry.NeutralYaw ?? 0)) : 0;
        double pitch = Telemetry.Raw is { } pitchPose ? Wrap(pitchPose.Pitch - (Telemetry.NeutralPitch ?? 0)) : 0;
        double cx = area.Left + w / 2, cy = area.Top + h / 2;
        // Insets reserve enough room for complete dots and dotted boundary lines.
        double halfWidth = (w - 16) / 2, halfHeight = (h - 16) / 2;
        double X(double angle) => cx + angle * halfWidth / DisplayExtent;
        double Y(double angle) => cy - angle * halfHeight / DisplayExtent;
        void Label(string text, double x, double y, Brush? brush = null)
        {
            var label = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, brush ?? muted, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = Math.Max(1, w), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(label, new(Math.Clamp(x, 4, Math.Max(4, ActualWidth - 4 - label.WidthIncludingTrailingWhitespace)),
                Math.Clamp(y, 4, Math.Max(4, ActualHeight - 4 - label.Height))));
        }
        for (int i = 1; i < 8; i++)
        {
            dc.DrawLine(gridPen, new(area.Left + i * w / 8, area.Top), new(area.Left + i * w / 8, area.Bottom));
            dc.DrawLine(gridPen, new(area.Left, area.Top + i * h / 8), new(area.Right, area.Top + i * h / 8));
        }
        dc.DrawLine(new(muted, 1), new(cx, area.Top), new(cx, area.Bottom));
        dc.DrawLine(new(muted, 1), new(area.Left, cy), new(area.Right, cy));
        Label("0°", cx + 5, cy + 3);
        Label($"Yaw −{DisplayExtent:0.#}°", area.Left, area.Bottom + 7);
        Label($"Yaw +{DisplayExtent:0.#}°", area.Right - 75, area.Bottom + 7);
        Label($"Pitch: +{DisplayExtent:0.#}° up / −{DisplayExtent:0.#}° down", area.Left, 4);

        // Both dots use measured degrees relative to the same recentered zero.
        // Deadzone and reach thresholds therefore stay fixed around the plot centre.
        dc.PushClip(new RectangleGeometry(area));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(45, 102, 222, 194)), new Pen(accent, 1),
            new Rect(X(-Settings.YawDeadzone), Y(Settings.PitchDeadzone),
                X(Settings.YawDeadzone) - X(-Settings.YawDeadzone),
                Y(-Settings.PitchDeadzone) - Y(Settings.PitchDeadzone)));
        var limitPen = new Pen(red, 1.5) { DashStyle = DashStyles.Dot, DashCap = PenLineCap.Round };
        double left = X(-Settings.HeadLeft), right = X(Settings.HeadRight);
        double up = Y(Settings.HeadUp), down = Y(-Settings.HeadDown);
        dc.DrawLine(limitPen, new(left, area.Top), new(left, area.Bottom));
        dc.DrawLine(limitPen, new(right, area.Top), new(right, area.Bottom));
        dc.DrawLine(limitPen, new(area.Left, up), new(area.Right, up));
        dc.DrawLine(limitPen, new(area.Left, down), new(area.Right, down));
        dc.Pop();
        Label($"L {Settings.HeadLeft:0.#}°", left + 4, area.Top + 4, red);
        Label($"R {Settings.HeadRight:0.#}°", right - 48, area.Top + 18, red);
        Label($"Up {Settings.HeadUp:0.#}°", area.Left + 4, up + 3, red);
        Label($"Down {Settings.HeadDown:0.#}°", area.Left + 4, down - 17, red);
        if (Telemetry.Raw is not { } raw)
        {
            Label("Waiting for Tobii SDK head pose", area.Left, area.Bottom + 25);
            return;
        }
        // Blue remains unfiltered SDK movement, with only the new centre subtracted.


        var rawBrush = new SolidColorBrush(Telemetry.PoseValid ? Color.FromRgb(101, 181, 255) : Color.FromRgb(97, 108, 123));
        // Map the processed fraction back onto the head-degree scale. This lets a
        // neutral curve follow blue outside the deadzone once smoothing has settled.
        double ResponseAngle(double response, double deadzone, double negative, double positive) => response == 0 ? 0
            : Math.Sign(response) * (deadzone + Math.Abs(response) * ((response < 0 ? negative : positive) - deadzone));
        double mouseYaw = ResponseAngle(Telemetry.ProcessedX, Settings.YawDeadzone, Settings.HeadLeft, Settings.HeadRight);
        double mousePitch = ResponseAngle(Telemetry.ProcessedY, Settings.PitchDeadzone, Settings.HeadDown, Settings.HeadUp);
        // A shared inset keeps both complete circles visible, and keeps neutral
        // responses coincident even at the edge of the observed motion range.
        Point VisiblePoint(double yawAngle, double pitchAngle) => new(
            Math.Clamp(X(yawAngle), area.Left + 7, area.Right - 7),
            Math.Clamp(Y(pitchAngle), area.Top + 7, area.Bottom - 7));
        var mousePoint = VisiblePoint(mouseYaw, mousePitch);
        var mouseBrush = Telemetry.PoseValid ? Brushes.Gold : Brushes.DarkGoldenrod;
        dc.DrawEllipse(mouseBrush, null, mousePoint, 7, 7);
        dc.DrawEllipse(rawBrush, null, VisiblePoint(yaw, pitch), 4, 4);
        Label($"{(Telemetry.PoseValid ? "SDK" : "Last SDK sample · stale")}  yaw {raw.Yaw:+0.00;-0.00;0.00}°  pitch {raw.Pitch:+0.00;-0.00;0.00}°", area.Left, area.Bottom + 25, rawBrush);
    }
}



