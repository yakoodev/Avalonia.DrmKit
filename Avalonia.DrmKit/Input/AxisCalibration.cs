namespace Avalonia.DrmKit.Input;

/// <summary>
/// Raw-to-range calibration state of a single absolute evdev axis.
/// Range precedence: explicit option override → kernel EVIOCGABS → runtime observation.
/// </summary>
internal sealed class AxisCalibration
{
    public int Min;
    public int Max;
    public bool HasKernelRange;
    public bool HasObservation;
    public bool HasOverride;
    public bool WarnedUncalibrated;

    public int Span => Max - Min;

    /// <summary>
    /// True when the axis has a usable range and mapped coordinates are meaningful.
    /// While false, position updates must be dropped instead of emitting garbage.
    /// </summary>
    public bool IsCalibrated => Span > 0 && (HasOverride || HasKernelRange || HasObservation);

    /// <summary>
    /// Applies an explicit user-provided range. Wins over kernel range and observation;
    /// subsequent observations no longer modify the range.
    /// </summary>
    public bool TryApplyOverride(int? min, int? max)
    {
        if (min is null || max is null || max.Value <= min.Value)
            return false;

        Min = min.Value;
        Max = max.Value;
        HasOverride = true;
        HasObservation = true;
        return true;
    }

    /// <summary>
    /// Applies the kernel-reported EVIOCGABS range unless an override is already active.
    /// </summary>
    public void SetKernelRange(int min, int max)
    {
        if (HasOverride || max <= min)
            return;

        Min = min;
        Max = max;
        HasKernelRange = true;
        HasObservation = true;
    }

    /// <summary>
    /// Widens the observed range from a raw sample. No-op when an explicit override is set.
    /// </summary>
    public void UpdateObservation(int raw)
    {
        if (HasOverride)
            return;

        if (!HasObservation)
        {
            Min = raw;
            Max = raw;
            HasObservation = true;
            return;
        }

        if (raw < Min) Min = raw;
        if (raw > Max) Max = raw;
    }
}
