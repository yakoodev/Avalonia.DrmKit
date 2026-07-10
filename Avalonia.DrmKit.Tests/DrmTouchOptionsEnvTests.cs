using Avalonia.DrmKit.Input;
using Xunit;

namespace Avalonia.DrmKit.Tests;

public class DrmTouchOptionsEnvTests
{
    private static Func<string, string?> Env(Dictionary<string, string?> values)
        => key => values.TryGetValue(key, out var v) ? v : null;

    [Fact]
    public void No_env_vars_leaves_options_untouched()
    {
        var options = new DrmTouchOptions
        {
            SwapXY = true,
            InvertX = true,
            AbsXMin = 5,
            AbsXMax = 500
        };

        DrmTouchOptions.ApplyOverrides(options, Env(new Dictionary<string, string?>()));

        Assert.True(options.SwapXY);
        Assert.True(options.InvertX);
        Assert.False(options.InvertY);
        Assert.Equal(5, options.AbsXMin);
        Assert.Equal(500, options.AbsXMax);
        Assert.Null(options.AbsYMin);
    }

    [Fact]
    public void Env_vars_override_code_configuration()
    {
        var options = new DrmTouchOptions { SwapXY = false, InvertY = true, AbsXMin = 5 };

        DrmTouchOptions.ApplyOverrides(options, Env(new Dictionary<string, string?>
        {
            ["DRMKIT_TOUCH_SWAP_XY"] = "1",
            ["DRMKIT_TOUCH_INVERT_Y"] = "0",
            ["DRMKIT_TOUCH_ABS_X_MIN"] = "0",
            ["DRMKIT_TOUCH_ABS_X_MAX"] = "4095",
            ["DRMKIT_TOUCH_ABS_Y_MIN"] = "-100",
            ["DRMKIT_TOUCH_ABS_Y_MAX"] = "3900"
        }));

        Assert.True(options.SwapXY);
        Assert.False(options.InvertY);
        Assert.Equal(0, options.AbsXMin);
        Assert.Equal(4095, options.AbsXMax);
        Assert.Equal(-100, options.AbsYMin);
        Assert.Equal(3900, options.AbsYMax);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    [InlineData("0", false)]
    public void Boolean_forms_are_accepted(string raw, bool expected)
    {
        var options = new DrmTouchOptions { SwapXY = !expected };

        DrmTouchOptions.ApplyOverrides(options, Env(new Dictionary<string, string?>
        {
            ["DRMKIT_TOUCH_SWAP_XY"] = raw
        }));

        Assert.Equal(expected, options.SwapXY);
    }

    [Fact]
    public void Garbage_values_are_ignored()
    {
        var options = new DrmTouchOptions { SwapXY = true, AbsXMin = 42 };

        DrmTouchOptions.ApplyOverrides(options, Env(new Dictionary<string, string?>
        {
            ["DRMKIT_TOUCH_SWAP_XY"] = "maybe",
            ["DRMKIT_TOUCH_ABS_X_MIN"] = "not-a-number"
        }));

        Assert.True(options.SwapXY);
        Assert.Equal(42, options.AbsXMin);
    }

    [Fact]
    public void Touch_feedback_defaults_are_enabled()
    {
        var options = new DrmTouchOptions();

        Assert.True(options.ShowTouchFeedback);
        Assert.True(options.HideCursorWhileTouching);
        Assert.Equal(44, options.TouchFeedbackSizePx);
        Assert.Equal(0.38, options.TouchFeedbackOpacity);
    }

    [Fact]
    public void Touch_feedback_env_vars_override_code_configuration()
    {
        var options = new DrmTouchOptions();

        DrmTouchOptions.ApplyOverrides(options, Env(new Dictionary<string, string?>
        {
            ["DRMKIT_TOUCH_FEEDBACK"] = "0",
            ["DRMKIT_TOUCH_HIDE_CURSOR"] = "off",
            ["DRMKIT_TOUCH_FEEDBACK_SIZE"] = "60.5",
            ["DRMKIT_TOUCH_FEEDBACK_OPACITY"] = "0.25"
        }));

        Assert.False(options.ShowTouchFeedback);
        Assert.False(options.HideCursorWhileTouching);
        Assert.Equal(60.5, options.TouchFeedbackSizePx);
        Assert.Equal(0.25, options.TouchFeedbackOpacity);
    }

    [Fact]
    public void Invalid_touch_feedback_values_are_ignored()
    {
        var options = new DrmTouchOptions();

        DrmTouchOptions.ApplyOverrides(options, Env(new Dictionary<string, string?>
        {
            ["DRMKIT_TOUCH_FEEDBACK"] = "maybe",
            ["DRMKIT_TOUCH_FEEDBACK_SIZE"] = "-10",
            ["DRMKIT_TOUCH_FEEDBACK_OPACITY"] = "1.5"
        }));

        Assert.True(options.ShowTouchFeedback);
        Assert.Equal(44, options.TouchFeedbackSizePx);
        Assert.Equal(0.38, options.TouchFeedbackOpacity);
    }
}
