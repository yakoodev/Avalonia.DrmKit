using Avalonia.DrmKit.Input;
using Xunit;

namespace Avalonia.DrmKit.Tests;

public class TouchCoordinateMapperTests
{
    private static AxisCalibration KernelAxis(int min, int max)
    {
        var axis = new AxisCalibration();
        axis.SetKernelRange(min, max);
        return axis;
    }

    [Theory]
    [InlineData(true, false, true)]   // raw X, no swap -> screen X
    [InlineData(false, false, false)] // raw Y, no swap -> screen Y
    [InlineData(true, true, false)]   // raw X, swap -> screen Y
    [InlineData(false, true, true)]   // raw Y, swap -> screen X
    public void TargetsScreenX_honors_swap(bool rawAxisIsX, bool swapXY, bool expected)
    {
        Assert.Equal(expected, TouchCoordinateMapper.TargetsScreenX(rawAxisIsX, swapXY));
    }

    [Fact]
    public void Maps_kernel_range_linearly()
    {
        var axis = KernelAxis(0, 4095);

        Assert.Equal(0, TouchCoordinateMapper.Map(0, axis, 1280, invert: false)!.Value, 3);
        Assert.Equal(1279, TouchCoordinateMapper.Map(4095, axis, 1280, invert: false)!.Value, 3);

        var mid = TouchCoordinateMapper.Map(2048, axis, 1280, invert: false)!.Value;
        Assert.InRange(mid, 639, 641);
    }

    [Fact]
    public void Maps_nonzero_min_range()
    {
        var axis = KernelAxis(200, 3800);

        Assert.Equal(0, TouchCoordinateMapper.Map(200, axis, 800, invert: false)!.Value, 3);
        Assert.Equal(799, TouchCoordinateMapper.Map(3800, axis, 800, invert: false)!.Value, 3);
    }

    [Fact]
    public void Invert_flips_after_normalization()
    {
        var axis = KernelAxis(0, 1000);

        Assert.Equal(799, TouchCoordinateMapper.Map(0, axis, 800, invert: true)!.Value, 3);
        Assert.Equal(0, TouchCoordinateMapper.Map(1000, axis, 800, invert: true)!.Value, 3);

        // Invert of the midpoint stays at the midpoint.
        var straight = TouchCoordinateMapper.Map(500, axis, 800, invert: false)!.Value;
        var inverted = TouchCoordinateMapper.Map(500, axis, 800, invert: true)!.Value;
        Assert.Equal(straight, inverted, 3);
    }

    [Fact]
    public void Clamps_out_of_range_raw_values()
    {
        var axis = KernelAxis(0, 1000);

        Assert.Equal(0, TouchCoordinateMapper.Map(-50, axis, 800, invert: false)!.Value, 3);
        Assert.Equal(799, TouchCoordinateMapper.Map(2000, axis, 800, invert: false)!.Value, 3);
    }

    [Fact]
    public void Uncalibrated_axis_drops_position()
    {
        var axis = new AxisCalibration();
        Assert.Null(TouchCoordinateMapper.Map(512, axis, 800, invert: false));

        // A single observation gives Span == 0: still uncalibrated, still dropped.
        axis.UpdateObservation(512);
        Assert.Null(TouchCoordinateMapper.Map(512, axis, 800, invert: false));

        // A second distinct observation produces a usable range.
        axis.UpdateObservation(600);
        Assert.NotNull(TouchCoordinateMapper.Map(550, axis, 800, invert: false));
    }

    [Fact]
    public void Unavailable_screen_dimension_drops_position()
    {
        var axis = KernelAxis(0, 1000);

        Assert.Null(TouchCoordinateMapper.Map(500, axis, 0, invert: false));
        Assert.Null(TouchCoordinateMapper.Map(500, axis, 1, invert: false));
    }

    [Fact]
    public void Override_wins_over_kernel_range()
    {
        var axis = new AxisCalibration();
        Assert.True(axis.TryApplyOverride(100, 900));

        // Kernel range read after the override must not replace it.
        axis.SetKernelRange(0, 4095);

        Assert.Equal(0, TouchCoordinateMapper.Map(100, axis, 800, invert: false)!.Value, 3);
        Assert.Equal(799, TouchCoordinateMapper.Map(900, axis, 800, invert: false)!.Value, 3);
    }

    [Fact]
    public void Swap_and_invert_combination_maps_rotated_panel()
    {
        // Rotated panel: raw X (0..4095) drives screen Y (0..719), inverted.
        var rawXAxis = KernelAxis(0, 4095);

        var targetsScreenX = TouchCoordinateMapper.TargetsScreenX(rawAxisIsX: true, swapXY: true);
        Assert.False(targetsScreenX);

        var screenY = TouchCoordinateMapper.Map(0, rawXAxis, 720, invert: true)!.Value;
        Assert.Equal(719, screenY, 3);
    }
}
