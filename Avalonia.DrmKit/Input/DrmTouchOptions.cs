namespace Avalonia.DrmKit.Input;

using System;

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

    /// <summary>
    /// Swaps the raw X and Y touch axes before mapping to the screen.
    /// Use for panels mounted rotated 90/270 degrees relative to the display.
    /// A raw X event then drives the screen Y coordinate and vice versa.
    /// Environment override: <c>DRMKIT_TOUCH_SWAP_XY=1</c>.
    /// </summary>
    public bool SwapXY { get; set; }

    /// <summary>
    /// Inverts the final screen X coordinate (applied after normalization and axis swap).
    /// Environment override: <c>DRMKIT_TOUCH_INVERT_X=1</c>.
    /// </summary>
    public bool InvertX { get; set; }

    /// <summary>
    /// Inverts the final screen Y coordinate (applied after normalization and axis swap).
    /// Environment override: <c>DRMKIT_TOUCH_INVERT_Y=1</c>.
    /// </summary>
    public bool InvertY { get; set; }

    /// <summary>
    /// Explicit minimum raw value of the device X axis (before <see cref="SwapXY"/>).
    /// Use together with <see cref="AbsXMax"/> for panels where the EVIOCGABS ioctl
    /// fails or reports a wrong range. Takes precedence over kernel-reported ranges
    /// and runtime observation. Environment override: <c>DRMKIT_TOUCH_ABS_X_MIN</c>.
    /// </summary>
    public int? AbsXMin { get; set; }

    /// <summary>
    /// Explicit maximum raw value of the device X axis (before <see cref="SwapXY"/>).
    /// Environment override: <c>DRMKIT_TOUCH_ABS_X_MAX</c>.
    /// </summary>
    public int? AbsXMax { get; set; }

    /// <summary>
    /// Explicit minimum raw value of the device Y axis (before <see cref="SwapXY"/>).
    /// Environment override: <c>DRMKIT_TOUCH_ABS_Y_MIN</c>.
    /// </summary>
    public int? AbsYMin { get; set; }

    /// <summary>
    /// Explicit maximum raw value of the device Y axis (before <see cref="SwapXY"/>).
    /// Environment override: <c>DRMKIT_TOUCH_ABS_Y_MAX</c>.
    /// </summary>
    public int? AbsYMax { get; set; }

    /// <summary>
    /// Shows a visual indicator (semi-transparent circle) at every active touch
    /// contact: it appears on TouchBegin, follows TouchUpdate and fades out
    /// quickly after TouchEnd. Enabled by default.
    /// Environment override: <c>DRMKIT_TOUCH_FEEDBACK</c> (0/1).
    /// </summary>
    public bool ShowTouchFeedback { get; set; } = true;

    /// <summary>
    /// Hides the soft cursor (arrow) while any touch contact is active. The
    /// cursor reappears on the next real mouse/relative input. Enabled by default.
    /// Environment override: <c>DRMKIT_TOUCH_HIDE_CURSOR</c> (0/1).
    /// </summary>
    public bool HideCursorWhileTouching { get; set; } = true;

    /// <summary>
    /// Diameter (in pixels) of the touch feedback indicator.
    /// Environment override: <c>DRMKIT_TOUCH_FEEDBACK_SIZE</c>.
    /// </summary>
    public double TouchFeedbackSizePx { get; set; } = 44;

    /// <summary>
    /// Fill opacity (0..1) of the touch feedback indicator. The ring border is
    /// drawn slightly brighter automatically.
    /// Environment override: <c>DRMKIT_TOUCH_FEEDBACK_OPACITY</c>.
    /// </summary>
    public double TouchFeedbackOpacity { get; set; } = 0.38;

    /// <summary>
    /// Applies <c>DRMKIT_TOUCH_*</c> environment variable overrides on top of
    /// <paramref name="options"/> (a new instance is created when null) and returns it.
    /// Environment variables always win over values set in code, which allows
    /// per-device tuning without rebuilding the application.
    /// </summary>
    public static DrmTouchOptions WithEnvironmentOverrides(DrmTouchOptions? options = null)
        => ApplyOverrides(options ?? new DrmTouchOptions(), Environment.GetEnvironmentVariable);

    internal static DrmTouchOptions ApplyOverrides(DrmTouchOptions options, Func<string, string?> getEnv)
    {
        if (TryParseBool(getEnv("DRMKIT_TOUCH_SWAP_XY"), out var swap))
            options.SwapXY = swap;
        if (TryParseBool(getEnv("DRMKIT_TOUCH_INVERT_X"), out var invertX))
            options.InvertX = invertX;
        if (TryParseBool(getEnv("DRMKIT_TOUCH_INVERT_Y"), out var invertY))
            options.InvertY = invertY;

        if (TryParseInt(getEnv("DRMKIT_TOUCH_ABS_X_MIN"), out var xMin))
            options.AbsXMin = xMin;
        if (TryParseInt(getEnv("DRMKIT_TOUCH_ABS_X_MAX"), out var xMax))
            options.AbsXMax = xMax;
        if (TryParseInt(getEnv("DRMKIT_TOUCH_ABS_Y_MIN"), out var yMin))
            options.AbsYMin = yMin;
        if (TryParseInt(getEnv("DRMKIT_TOUCH_ABS_Y_MAX"), out var yMax))
            options.AbsYMax = yMax;

        if (TryParseBool(getEnv("DRMKIT_TOUCH_FEEDBACK"), out var feedback))
            options.ShowTouchFeedback = feedback;
        if (TryParseBool(getEnv("DRMKIT_TOUCH_HIDE_CURSOR"), out var hideCursor))
            options.HideCursorWhileTouching = hideCursor;
        if (TryParseDouble(getEnv("DRMKIT_TOUCH_FEEDBACK_SIZE"), out var feedbackSize) && feedbackSize > 0)
            options.TouchFeedbackSizePx = feedbackSize;
        if (TryParseDouble(getEnv("DRMKIT_TOUCH_FEEDBACK_OPACITY"), out var feedbackOpacity)
            && feedbackOpacity >= 0 && feedbackOpacity <= 1)
            options.TouchFeedbackOpacity = feedbackOpacity;

        return options;
    }

    private static bool TryParseBool(string? raw, out bool value)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case "1" or "true" or "yes" or "on":
                value = true;
                return true;
            case "0" or "false" or "no" or "off":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static bool TryParseInt(string? raw, out int value)
        => int.TryParse(raw?.Trim(), out value);

    private static bool TryParseDouble(string? raw, out double value)
        => double.TryParse(
            raw?.Trim(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
}
