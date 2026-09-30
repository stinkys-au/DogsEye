namespace DogsEye.Core;

// Two monotone cubic Hermite segments through one control point. Matching slopes
// at the point keep the response smooth. A point on the diagonal is neutral/linear.
public sealed record LookResponseCurve
{
    public double Head { get; init; } = .5;
    public double Turn { get; init; } = .5;

    public void Validate(string axis)
    {
        if (!double.IsFinite(Head) || !double.IsFinite(Turn) || Head < .1 || Head > .9 || Turn < .1 || Turn > .9)
            throw new ArgumentException($"{axis} curve point must be between 10% and 90% on both axes.");
    }

    public double Evaluate(double magnitude)
    {
        double x = Math.Clamp(magnitude, 0, 1);
        double leftSlope = Turn / Head, rightSlope = (1 - Turn) / (1 - Head);
        double slope = 2 * leftSlope * rightSlope / (leftSlope + rightSlope);
        double start, end, width, t, startSlope, endSlope;
        if (x <= Head)
        {
            start = 0; end = Turn; width = Head; t = x / width;
            startSlope = leftSlope; endSlope = slope;
        }
        else
        {
            start = Turn; end = 1; width = 1 - Head; t = (x - Head) / width;
            startSlope = slope; endSlope = rightSlope;
        }
        double t2 = t * t, t3 = t2 * t;
        return Math.Clamp((2 * t3 - 3 * t2 + 1) * start + (t3 - 2 * t2 + t) * width * startSlope
            + (-2 * t3 + 3 * t2) * end + (t3 - t2) * width * endSlope, 0, 1);
    }
}
