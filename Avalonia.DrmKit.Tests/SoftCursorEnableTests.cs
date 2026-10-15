using Avalonia.DrmKit;
using Xunit;

namespace Avalonia.DrmKit.Tests;

/// <summary>Начальное состояние программного курсора: опция и переменная DRMKIT_SOFT_CURSOR.</summary>
public class SoftCursorEnableTests
{
    [Theory]
    [InlineData(true, null, true)]
    [InlineData(false, null, false)]
    [InlineData(true, "0", false)]
    [InlineData(true, "false", false)]
    [InlineData(true, "OFF", false)]
    [InlineData(false, "1", true)]
    [InlineData(false, "true", true)]
    [InlineData(false, "garbage", false)] // непонятное значение не перебивает опцию
    [InlineData(true, " ", true)]
    public void Env_overrides_option(bool option, string? env, bool expected)
        => Assert.Equal(expected, DrmSoftCursor.ResolveInitial(option, env));
}
