using Avalonia.DrmKit.OnScreenKeyboard;
using Avalonia.Input.TextInput;
using System.Globalization;
using System.Linq;
using Xunit;

namespace Avalonia.DrmKit.Tests;

/// <summary>
/// 12.0.9: digit pad for numeric fields and three distinct action keys — «Done», «Hide», «Undo changes».
/// Before, the keyboard always showed letters and had «Enter» / «Cancel», where «Cancel» silently rolled
/// the typed text back.
/// </summary>
public sealed class OnScreenKeyboardKeysTests
{
    [Theory]
    [InlineData(TextInputContentType.Digits, OnScreenKeyboardMode.Digits)]
    [InlineData(TextInputContentType.Pin, OnScreenKeyboardMode.Digits)]
    [InlineData(TextInputContentType.Number, OnScreenKeyboardMode.Decimal)]
    [InlineData(TextInputContentType.Normal, OnScreenKeyboardMode.Text)]
    [InlineData(TextInputContentType.Email, OnScreenKeyboardMode.Text)]
    public void ModeFollowsTheFieldContentType(TextInputContentType contentType, OnScreenKeyboardMode expected)
        => Assert.Equal(expected, OnScreenKeyboardKeys.ModeFor(contentType));

    [Fact]
    public void DigitPad_IsFourByFour_WithActionsInTheRightColumn()
    {
        var rows = OnScreenKeyboardKeys.DigitRows(withSeparator: false);

        Assert.Equal(4, rows.Length);
        Assert.All(rows, r => Assert.Equal(4, r.Length));
        Assert.Equal(new[] { "7", "8", "9", "4", "5", "6", "1", "2", "3", "0" },
            rows.SelectMany(r => r.Take(3)).Where(k => k.Length > 0));
        Assert.Equal(new[] { OnScreenKeyboardKeys.Backspace, OnScreenKeyboardKeys.Hide, OnScreenKeyboardKeys.Cancel, OnScreenKeyboardKeys.Enter },
            rows.Select(r => r[3]));
        Assert.DoesNotContain(rows.SelectMany(r => r), k => k is "," or ".");
    }

    [Fact]
    public void DecimalPad_HasTheCultureSeparator()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            Assert.Equal(",", OnScreenKeyboardKeys.DigitRows(withSeparator: true)[3][0]);
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal(".", OnScreenKeyboardKeys.DigitRows(withSeparator: true)[3][0]);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData("ru-RU", "Готово", "Скрыть", "Отменить изменения")]
    [InlineData("en-US", "Done", "Hide", "Undo changes")]
    public void ActionCaptions_FollowTheUiLanguage(string culture, string done, string hide, string undo)
    {
        var saved = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            Assert.Equal(done, OnScreenKeyboardKeys.Caption(OnScreenKeyboardKeys.Enter));
            Assert.Equal(hide, OnScreenKeyboardKeys.Caption(OnScreenKeyboardKeys.Hide));
            Assert.Equal(undo, OnScreenKeyboardKeys.Caption(OnScreenKeyboardKeys.Cancel));
            Assert.Equal("7", OnScreenKeyboardKeys.Caption("7"));
            Assert.Equal("<-", OnScreenKeyboardKeys.Caption(OnScreenKeyboardKeys.Backspace));
        }
        finally
        {
            CultureInfo.CurrentUICulture = saved;
        }
    }

    [Fact]
    public void AppCanOverrideTheWording()
    {
        try
        {
            OnScreenKeyboardLabels.Done = "OK";
            Assert.Equal("OK", OnScreenKeyboardKeys.Caption(OnScreenKeyboardKeys.Enter));
        }
        finally
        {
            OnScreenKeyboardLabels.Done = null!;
        }
    }

    [Fact]
    public void TextLayouts_HaveHideUndoAndDone_InsteadOfEnterCancel()
    {
        foreach (var name in new[] { "en", "ru" })
        {
            Input.KeyboardLayoutManager.SetCurrent(name);
            var last = Input.KeyboardLayoutManager.GetCurrent().Rows[^1].Select(k => k.ToLowerInvariant()).ToArray();
            Assert.Contains(OnScreenKeyboardKeys.Hide, last);
            Assert.Contains(OnScreenKeyboardKeys.Cancel, last);
            Assert.Contains(OnScreenKeyboardKeys.Enter, last);
        }
    }
}
