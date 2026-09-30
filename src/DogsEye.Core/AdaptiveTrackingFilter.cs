namespace DogsEye.Core;

// One Euro speed-adaptive low-pass filter (Casiez, Roussel & Vogel, CHI 2012).
// Operates on head angles in degrees, never mouse velocity or future poses.
public sealed class AdaptiveTrackingFilter
{
    private double previousRaw, filtered, filteredVelocity;

    public void Reset() => previousRaw = filtered = filteredVelocity = 0;

    public double Process(double value, double elapsed, double strength, double responsiveness)
    {
        double dt = Math.Clamp(elapsed, .001, TrackingController.StaleSeconds);
        if (strength == 0)
        {
            previousRaw = filtered = value;
            filteredVelocity = 0;
            return value;
        }
        static double Alpha(double frequency, double seconds) => 1 / (1 + 1 / (2 * Math.PI * frequency * seconds));
        double velocity = (value - previousRaw) / dt;
        previousRaw = value;
        // Smooth the speed estimate so raw sensor noise does not repeatedly open the filter.
        filteredVelocity += Alpha(1, dt) * (velocity - filteredVelocity);
        // Strength controls steadiness at rest; beta opens the filter during motion.
        double minimumCutoff = 1 / (2 * Math.PI * strength * .5);
        double cutoff = minimumCutoff + responsiveness * Math.Abs(filteredVelocity);
        filtered += Alpha(cutoff, dt) * (value - filtered);
        return filtered;
    }
}
