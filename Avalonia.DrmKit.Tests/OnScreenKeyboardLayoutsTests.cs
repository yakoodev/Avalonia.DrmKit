using Avalonia.DrmKit.OnScreenKeyboard;
using System.Globalization;
using System.Linq;
using Xunit;

namespace Avalonia.DrmKit.Tests;

/// <summary>
/// 12.1.0: rows of the new keyboard looks. Letters — the phone look with «Undo changes» moved to the strip;
/// digits — two rows of five with a decimal separator (and the 4×4 pad kept as an option).
/// </summary>
public sealed class OnScreenKeyboardLayoutsTests
{
    private static string[] Tokens(OnScreenKeyboardKey[][] rows) => rows.SelectMany(r => r).Select(k => k.Token).ToArray();

    private static T WithCulture<T>(string culture, System.Func<T> body)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---------- digits, two rows ----------

    [Fact]
    public void TwoRows_FiveDigitsPerRow_ThenBackspaceOverSeparator_ThenUndoOverDone()
    {
        var rows = WithCulture("ru-RU", () => OnScreenKeyboardLayouts.Digits(OnScreenKeyboardDigitVariant.TwoRows, withSeparator: true, dimSeparator: true));

        Assert.Equal(2, rows.Length);
        Assert.All(rows, r => Assert.Equal(7, r.Length));
        Assert.Equal(new[] { "1", "2", "3", "4", "5" }, rows[0].Take(5).Select(k => k.Token));
        Assert.Equal(new[] { "6", "7", "8", "9", "0" }, rows[1].Take(5).Select(k => k.Token));
        Assert.Equal(OnScreenKeyboardKeys.Backspace, rows[0][5].Token);
        Assert.Equal(",", rows[1][5].Token);
        Assert.Equal(OnScreenKeyboardKeys.Cancel, rows[0][6].Token);
        Assert.Equal(OnScreenKeyboardKeys.Enter, rows[1][6].Token);
        Assert.Equal("go", rows[1][6].Class);
        // «Undo changes» has to fit on one line on a 1024 px screen: the action column is two digit keys wide.
        Assert.Equal(2, rows[0][6].Weight);
        // The two rows line up: the same widths column by column.
        Assert.Equal(rows[0].Select(k => k.Weight), rows[1].Select(k => k.Weight));
    }

    [Theory]
    [InlineData(OnScreenKeyboardDigitVariant.TwoRows)]
    [InlineData(OnScreenKeyboardDigitVariant.Pad)]
    public void DigitLooks_HaveEveryDigitOnce_AndNoHideKey(OnScreenKeyboardDigitVariant variant)
    {
        var tokens = Tokens(OnScreenKeyboardLayouts.Digits(variant, withSeparator: true, dimSeparator: true));

        foreach (var digit in "0123456789")
            Assert.Single(tokens, t => t == digit.ToString());
        Assert.Single(tokens, t => t == OnScreenKeyboardKeys.Backspace);
        Assert.Single(tokens, t => t == OnScreenKeyboardKeys.Cancel);
        Assert.Single(tokens, t => t == OnScreenKeyboardKeys.Enter);
        // «Hide» lives in the strip above the keys.
        Assert.DoesNotContain(OnScreenKeyboardKeys.Hide, tokens);
    }

    [Theory]
    [InlineData(OnScreenKeyboardDigitVariant.TwoRows, "ru-RU", ",")]
    [InlineData(OnScreenKeyboardDigitVariant.TwoRows, "en-US", ".")]
    [InlineData(OnScreenKeyboardDigitVariant.Pad, "ru-RU", ",")]
    [InlineData(OnScreenKeyboardDigitVariant.Pad, "en-US", ".")]
    public void FractionField_SeparatorKeyTypesTheCultureSeparator(OnScreenKeyboardDigitVariant variant, string culture, string expected)
    {
        var keys = WithCulture(culture, () => OnScreenKeyboardLayouts.Digits(variant, withSeparator: true, dimSeparator: true)).SelectMany(r => r).ToArray();

        var separator = Assert.Single(keys, k => k.Token == expected);
        Assert.True(separator.Enabled);
    }

    [Theory]
    [InlineData(OnScreenKeyboardDigitVariant.TwoRows)]
    [InlineData(OnScreenKeyboardDigitVariant.Pad)]
    public void WholeNumberField_SeparatorIsDimmedInPlace_OrItsCellIsEmpty(OnScreenKeyboardDigitVariant variant)
    {
        var fraction = WithCulture("ru-RU", () => OnScreenKeyboardLayouts.Digits(variant, withSeparator: true, dimSeparator: true));
        var dimmed = WithCulture("ru-RU", () => OnScreenKeyboardLayouts.Digits(variant, withSeparator: false, dimSeparator: true));
        var empty = WithCulture("ru-RU", () => OnScreenKeyboardLayouts.Digits(variant, withSeparator: false, dimSeparator: false));

        // Dimmed: the same key in the same cell, only it cannot be pressed. Nothing else changes.
        var off = Assert.Single(dimmed.SelectMany(r => r), k => !k.Enabled);
        Assert.Equal(",", off.Token);
        Assert.Equal(Tokens(fraction), Tokens(dimmed));
        Assert.Equal(fraction.SelectMany(r => r).Select(k => k.Weight), dimmed.SelectMany(r => r).Select(k => k.Weight));

        // Empty: a spacer in that cell, the other keys stay where they were.
        Assert.DoesNotContain(",", Tokens(empty));
        Assert.All(empty.SelectMany(r => r), k => Assert.True(k.Enabled));
        Assert.Equal(fraction.Select(r => r.Length), empty.Select(r => r.Length));
        Assert.Single(empty.SelectMany(r => r), k => k.IsBlank);
    }

    [Fact]
    public void Pad_IsFourByFour_WithActionsInARowBelow()
    {
        var rows = WithCulture("ru-RU", () => OnScreenKeyboardLayouts.Digits(OnScreenKeyboardDigitVariant.Pad, withSeparator: true, dimSeparator: true));

        Assert.Equal(4, rows.Length);
        Assert.Equal(new[] { "1", "2", "3", OnScreenKeyboardKeys.Backspace }, rows[0].Select(k => k.Token));
        Assert.Equal(new[] { "4", "5", "6", "," }, rows[1].Select(k => k.Token));
        Assert.Equal(new[] { "7", "8", "9", "0" }, rows[2].Select(k => k.Token));
        Assert.Equal(new[] { OnScreenKeyboardKeys.Cancel, OnScreenKeyboardKeys.Enter }, rows[3].Select(k => k.Token));
    }

    [Fact]
    public void ClassicPad_IsNotANewLook()
        => Assert.Throws<System.ArgumentOutOfRangeException>(() => OnScreenKeyboardLayouts.Digits(OnScreenKeyboardDigitVariant.Classic, true, true));

    [Fact]
    public void TwoRows_IsWiderThanThePad()
    {
        Assert.Equal(OnScreenKeyboardLayouts.TwoRowsMaxWidth, OnScreenKeyboardLayouts.DigitsMaxWidth(OnScreenKeyboardDigitVariant.TwoRows));
        Assert.Equal(OnScreenKeyboardLayouts.PadMaxWidth, OnScreenKeyboardLayouts.DigitsMaxWidth(OnScreenKeyboardDigitVariant.Pad));
        Assert.True(OnScreenKeyboardLayouts.TwoRowsMaxWidth > OnScreenKeyboardLayouts.PadMaxWidth);
    }

    // ---------- letters ----------

    [Theory]
    [InlineData("ru", "абвгдеёжзийклмнопрстуфхцчшщъыьэюя")]
    [InlineData("en", "abcdefghijklmnopqrstuvwxyz")]
    public void Letters_HaveTheWholeAlphabet_EachLetterOnce(string layout, string alphabet)
    {
        var tokens = Tokens(OnScreenKeyboardLayouts.PhoneLetters(layout, symbols: false));

        foreach (var letter in alphabet)
            Assert.Single(tokens, t => t == letter.ToString());
        Assert.Equal(alphabet.Length, tokens.Count(t => t.Length == 1 && char.IsLetter(t[0])));
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void Letters_ShiftAndBackspaceAtTheEndsOfTheThirdRow_DoneLastInTheBottomRow(string layout)
    {
        var rows = OnScreenKeyboardLayouts.PhoneLetters(layout, symbols: false);

        Assert.Equal(4, rows.Length);
        Assert.Equal(OnScreenKeyboardLayouts.Shift, rows[2][0].Token);
        Assert.Equal(OnScreenKeyboardKeys.Backspace, rows[2][^1].Token);
        Assert.Equal(new[] { OnScreenKeyboardLayouts.Symbols, OnScreenKeyboardLayouts.Language, OnScreenKeyboardLayouts.Space, ".", OnScreenKeyboardKeys.Enter },
            rows[3].Select(k => k.Token));
        Assert.Equal("go", rows[3][^1].Class);
        // The space bar is the widest key of the keyboard.
        Assert.Equal(rows.SelectMany(r => r).Max(k => k.Weight), rows[3][2].Weight);
    }

    [Theory]
    [InlineData("ru", false)]
    [InlineData("en", false)]
    [InlineData("ru", true)]
    [InlineData("en", true)]
    public void Letters_UndoAndHideAreNotAmongTheKeys(string layout, bool symbols)
    {
        // Both sit in the strip above the keys: «Undo changes» next to «Done» was hit by mistake.
        var tokens = Tokens(OnScreenKeyboardLayouts.PhoneLetters(layout, symbols));

        Assert.DoesNotContain(OnScreenKeyboardKeys.Cancel, tokens);
        Assert.DoesNotContain(OnScreenKeyboardKeys.Hide, tokens);
        Assert.Single(tokens, t => t == OnScreenKeyboardKeys.Enter);
    }

    [Fact]
    public void EnglishMiddleRow_IsInsetByHalfAKey_RussianIsNot()
    {
        var en = OnScreenKeyboardLayouts.PhoneLetters("en", symbols: false)[1];
        var ru = OnScreenKeyboardLayouts.PhoneLetters("ru", symbols: false)[1];

        Assert.True(en[0].IsBlank);
        Assert.True(en[^1].IsBlank);
        Assert.Equal(0.5, en[0].Weight);
        Assert.DoesNotContain(ru, k => k.IsBlank);
        // Both rows are as wide as the top row of their layout.
        Assert.Equal(10, en.Sum(k => k.Weight));
        Assert.Equal(12, ru.Sum(k => k.Weight));
    }

    [Theory]
    [InlineData("ru")]
    [InlineData("en")]
    public void SymbolsLayer_HasDigitsAndPunctuation_NoShift_AndAWayBackToLetters(string layout)
    {
        var rows = OnScreenKeyboardLayouts.PhoneLetters(layout, symbols: true);
        var tokens = Tokens(rows);

        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" }, rows[0].Select(k => k.Token));
        Assert.Contains("@", tokens);
        Assert.Contains("-", tokens);
        Assert.DoesNotContain(OnScreenKeyboardLayouts.Shift, tokens);
        Assert.DoesNotContain(OnScreenKeyboardLayouts.Symbols, tokens);
        Assert.Equal(OnScreenKeyboardLayouts.Letters, rows[3][0].Token);
        Assert.Equal(OnScreenKeyboardKeys.Backspace, rows[2][^1].Token);
        Assert.DoesNotContain(tokens, t => t.Length == 1 && char.IsLetter(t[0]));
    }

    [Fact]
    public void Style_DefaultsKeepTheClassicKeyboard_AndTheSeparatorDimmed()
    {
        // An app that sets nothing keeps the keyboard of 12.0.10.
        Assert.Equal(OnScreenKeyboardTextVariant.Classic, default(OnScreenKeyboardTextVariant));
        Assert.Equal(OnScreenKeyboardDigitVariant.Classic, default(OnScreenKeyboardDigitVariant));
        Assert.True(OnScreenKeyboardStyle.DimSeparatorInIntegerFields);
    }

    [Fact]
    public void Style_ChangeRaisesChanged_SameValueDoesNot()
    {
        var saved = (OnScreenKeyboardStyle.DigitVariant, OnScreenKeyboardStyle.DimSeparatorInIntegerFields);
        var raised = 0;
        void OnChanged(object? sender, System.EventArgs e) => raised++;
        OnScreenKeyboardStyle.Changed += OnChanged;
        try
        {
            OnScreenKeyboardStyle.DigitVariant = OnScreenKeyboardDigitVariant.TwoRows;
            OnScreenKeyboardStyle.DigitVariant = OnScreenKeyboardDigitVariant.TwoRows;
            OnScreenKeyboardStyle.DimSeparatorInIntegerFields = false;
            OnScreenKeyboardStyle.DimSeparatorInIntegerFields = false;
            Assert.Equal(saved.DigitVariant == OnScreenKeyboardDigitVariant.TwoRows ? 1 : 2, raised);
        }
        finally
        {
            OnScreenKeyboardStyle.Changed -= OnChanged;
            OnScreenKeyboardStyle.DigitVariant = saved.DigitVariant;
            OnScreenKeyboardStyle.DimSeparatorInIntegerFields = saved.DimSeparatorInIntegerFields;
        }
    }
}
