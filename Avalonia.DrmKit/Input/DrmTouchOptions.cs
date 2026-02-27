namespace Avalonia.DrmKit.Input;

/// <summary>
/// Touch gesture tuning options for DRM evdev input backend.
/// </summary>
public sealed class DrmTouchOptions
{
    /// <summary>
    /// Maximum movement distance (in pixels) still considered a tap.
    /// </summary>
    public double TapMaxDistancePx { get; set; } = 10;

    /// <summary>
    /// Maximum touch duration (in milliseconds) still considered a tap.
    /// </summary>
    public int TapMaxDurationMs { get; set; } = 220;

    /// <summary>
    /// Minimum movement (in pixels) required to emit touch move updates.
    /// Used to suppress sensor jitter while finger is down.
    /// </summary>
    public double MoveJitterThresholdPx { get; set; } = 1.5;

    /// <summary>
    /// Enables tap/swipe separation based on distance/time thresholds.
    /// </summary>
    public bool EnableTapSwipeSeparation { get; set; } = true;
}
