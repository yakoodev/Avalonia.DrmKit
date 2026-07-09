using System;

namespace Avalonia.DrmKit.Input;

/// <summary>
/// Pure raw-axis → screen-coordinate mapping used by <see cref="DrmUnifiedInputBackend"/>.
/// </summary>
internal static class TouchCoordinateMapper
{
    /// <summary>
    /// Resolves which screen axis a raw device axis drives, honoring an XY swap.
    /// With <paramref name="swapXY"/> a raw X event feeds the screen Y pipeline and vice versa.
    /// </summary>
    public static bool TargetsScreenX(bool rawAxisIsX, bool swapXY)
        => rawAxisIsX ^ swapXY;

    /// <summary>
    /// Maps a raw axis value onto a screen dimension.
    /// Returns null when the position must be dropped: the axis has no usable
    /// calibration range yet, or the screen dimension is not available.
    /// Inversion is applied after normalization.
    /// </summary>
    public static double? Map(int raw, AxisCalibration axis, int screenSize, bool invert)
    {
        if (screenSize <= 1)
            return null;

        if (!axis.IsCalibrated)
            return null;

        var normalized = (raw - axis.Min) / (double)axis.Span;
        if (invert)
            normalized = 1.0 - normalized;

        return Math.Clamp(normalized * (screenSize - 1), 0, screenSize - 1);
    }
}
