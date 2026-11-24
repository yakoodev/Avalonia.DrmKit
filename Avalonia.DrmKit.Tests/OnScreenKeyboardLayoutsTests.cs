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

    // ---------- digits, calculator ----------

    private static OnScreenKeyboardCell Cell(OnScreenKeyboardGridLayout grid, string token) => Assert.Single(grid.Cells, c => c.Key.Token == token);

    [Fact]
    public void Calculator_SevenEightNineOnTop_WideZero_SeparatorNextToIt_ActionsInTheRightColumn()
    {
        var grid = WithCulture("ru-RU", () => OnScreenKeyboardLayouts.CalculatorDigits(withSeparator: true, dimSeparator: true));

        Assert.Equal(4, grid.Rows);
        Assert.Equal(new[] { 1, 1, 1, OnScreenKeyboardLayouts.CalculatorActionWeight }, grid.Columns);
        var expected = new[] { new[] { "7", "8", "9" }, new[] { "4", "5", "6" }, new[] { "1", "2", "3" } };
        for (var row = 0; row < 3; row++)
            for (var column = 0; column < 3; column++)
                Assert.Equal((row, column, 1, 1), (Cell(grid, expected[row][column]).Row, Cell(grid, expected[row][column]).Column,
                                                    Cell(grid, expected[row][column]).RowSpan, Cell(grid, expected[row][column]).ColumnSpan));

        // The bottom row: 0 two keys wide, the separator right of it.
        Assert.Equal((3, 0, 1, 2), (Cell(grid, "0").Row, Cell(grid, "0").Column, Cell(grid, "0").RowSpan, Cell(grid, "0").ColumnSpan));
        Assert.Equal((3, 2), (Cell(grid, ",").Row, Cell(grid, ",").Column));

        // The right column: backspace, «Undo changes», «Done» two rows high.
        Assert.Equal((0, 3), (Cell(grid, OnScreenKeyboardKeys.Backspace).Row, Cell(grid, OnScreenKeyboardKeys.Backspace).Column));
        Assert.Equal((1, 3), (Cell(grid, OnScreenKeyboardKeys.Cancel).Row, Cell(grid, OnScreenKeyboardKeys.Cancel).Column));
        var done = Cell(grid, OnScreenKeyboardKeys.Enter);
        Assert.Equal((2, 3, 2, 1), (done.Row, done.Column, done.RowSpan, done.ColumnSpan));
        Assert.Equal("go", done.Key.Class);
        Assert.DoesNotContain(grid.Cells, c => c.Key.Token == OnScreenKeyboardKeys.Hide);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void Calculator_CellsFillTheGridExactlyOnce(bool withSeparator, bool dimSeparator)
    {
        var grid = OnScreenKeyboardLayouts.CalculatorDigits(withSeparator, dimSeparator);
        var taken = new int[grid.Rows, grid.Columns.Length];

        foreach (var cell in grid.Cells)
            for (var row = cell.Row; row < cell.Row + cell.RowSpan; row++)
                for (var column = cell.Column; column < cell.Column + cell.ColumnSpan; column++)
                    taken[row, column]++;   // throws if a key sticks out of the grid

        foreach (var count in taken)
            Assert.Equal(1, count);
        foreach (var digit in "0123456789")
            Assert.Single(grid.Cells, c => c.Key.Token == digit.ToString());
    }

    [Theory]
    [InlineData("ru-RU", ",")]
    [InlineData("en-US", ".")]
    public void Calculator_SeparatorTypesTheCultureSeparator_IsDimmedOrEmptyInWholeNumberFields(string culture, string expected)
    {
        var fraction = WithCulture(culture, () => OnScreenKeyboardLayouts.CalculatorDigits(withSeparator: true, dimSeparator: true));
        var dimmed = WithCulture(culture, () => OnScreenKeyboardLayouts.CalculatorDigits(withSeparator: false, dimSeparator: true));
        var empty = WithCulture(culture, () => OnScreenKeyboardLayouts.CalculatorDigits(withSeparator: false, dimSeparator: false));

        Assert.True(Cell(fraction, expected).Key.Enabled);
        var off = Assert.Single(dimmed.Cells, c => !c.Key.Enabled);
        Assert.Equal(expected, off.Key.Token);
        Assert.Equal((Cell(fraction, expected).Row, Cell(fraction, expected).Column), (off.Row, off.Column));
        var blank = Assert.Single(empty.Cells, c => c.Key.IsBlank);
        Assert.Equal((off.Row, off.Column), (blank.Row, blank.Column));
        Assert.DoesNotContain(empty.Cells, c => c.Key.Token == expected);
    }

    [Fact]
    public void Calculator_IsAGrid_NotRows()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => OnScreenKeyboardLayouts.Digits(OnScreenKeyboardDigitVariant.Calculator, true, true));
        Assert.Equal(OnScreenKeyboardLayouts.CalculatorMaxWidth, OnScreenKeyboardLayouts.DigitsMaxWidth(OnScreenKeyboardDigitVariant.Calculator));
        Assert.Equal(OnScreenKeyboardLayouts.CalculatorMaxWidth, OnScreenKeyboardLayouts.CalculatorDigits(true, true).MaxWidth);
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
