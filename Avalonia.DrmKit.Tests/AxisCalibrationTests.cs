using Avalonia.DrmKit.Input;
using Xunit;

namespace Avalonia.DrmKit.Tests;

public class AxisCalibrationTests
{
    [Fact]
    public void Fresh_axis_is_not_calibrated()
    {
        var axis = new AxisCalibration();
        Assert.False(axis.IsCalibrated);
    }

    [Fact]
    public void Observations_track_min_max()
    {
        var axis = new AxisCalibration();
        axis.UpdateObservation(500);
        axis.UpdateObservation(100);
        axis.UpdateObservation(900);

        Assert.Equal(100, axis.Min);
        Assert.Equal(900, axis.Max);
        Assert.True(axis.IsCalibrated);
    }

    [Fact]
    public void Override_blocks_later_observations_and_kernel_range()
    {
        var axis = new AxisCalibration();
        Assert.True(axis.TryApplyOverride(0, 4095));

        axis.UpdateObservation(-500);
        axis.UpdateObservation(10_000);
        axis.SetKernelRange(0, 100);

        Assert.Equal(0, axis.Min);
        Assert.Equal(4095, axis.Max);
        Assert.True(axis.HasOverride);
        Assert.False(axis.HasKernelRange);
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(100, null)]
    [InlineData(null, null)]
    [InlineData(100, 100)]
    [InlineData(200, 100)]
    public void Invalid_override_pairs_are_rejected(int? min, int? max)
    {
        var axis = new AxisCalibration();
        Assert.False(axis.TryApplyOverride(min, max));
        Assert.False(axis.HasOverride);
        Assert.False(axis.IsCalibrated);
    }

    [Fact]
    public void Kernel_range_allows_widening_by_observation()
    {
        var axis = new AxisCalibration();
        axis.SetKernelRange(0, 4000);

        axis.UpdateObservation(4100); // panel reports slightly beyond advertised max
        Assert.Equal(4100, axis.Max);
        Assert.Equal(0, axis.Min);
        Assert.True(axis.IsCalibrated);
    }

    [Fact]
    public void Invalid_kernel_range_is_ignored()
    {
        var axis = new AxisCalibration();
        axis.SetKernelRange(100, 100);
        Assert.False(axis.HasKernelRange);
        Assert.False(axis.IsCalibrated);
    }
}
