using System;
using System.Collections.Generic;
using System.Linq;

namespace Avalonia.DrmKit.OnScreenKeyboard
{
    /// <summary>One key of a new-look layout.</summary>
    /// <param name="Token">What the key types or does (see <see cref="OnScreenKeyboardKeys"/> and <see cref="OnScreenKeyboardLayouts"/>); empty — a spacer.</param>
    /// <param name="Weight">Width relative to the other keys of the row.</param>
    /// <param name="Class">Style classes, space separated: <c>digit</c>, <c>mod</c>, <c>go</c>, <c>quiet</c>.</param>
    /// <param name="Enabled">False — the key is shown dimmed and cannot be pressed.</param>
    public readonly record struct OnScreenKeyboardKey(string Token, double Weight = 1, string Class = "", bool Enabled = true)
    {
        /// <summary>An empty cell that only keeps the row aligned.</summary>
        public bool IsBlank => Token.Length == 0;
    }

    /// <summary>
    /// Rows of the new keyboard looks (<see cref="OnScreenKeyboardStyle"/>). Kept free of UI types so the
    /// layouts can be unit-tested; <c>OnScreenKeyboard.Modern.cs</c> only turns them into buttons.
    ///
    /// Letters (chosen 24.11.2026): three rows of letters, ⇧ and ⌫ at the ends of the third row, the bottom
    /// row ends with «Done» only. «Undo changes» is not in the rows: it sits in the strip above the keys next
    /// to «Hide», away from the letters and «Done».
    ///
    /// Digits, <see cref="OnScreenKeyboardDigitVariant.TwoRows"/> (chosen 24.11.2026): two rows of five digits,
    /// then ⌫ over the decimal separator, then «Undo changes» over «Done». It is the lowest pad, so it covers
    /// the least of the screen.
    /// </summary>
    public static class OnScreenKeyboardLayouts
    {
        /// <summary>One-shot ⇧ (double tap — Caps Lock).</summary>
        public const string Shift = "shift";
        /// <summary>Switches the letters to the «?123» layer.</summary>
        public const string Symbols = "sym";
        /// <summary>Switches the «?123» layer back to letters.</summary>
        public const string Letters = "abc";
        /// <summary>Switches between the Russian and the English letters.</summary>
        public const string Language = "lang";
        /// <summary>Space bar.</summary>
        public const string Space = "space";

        /// <summary>Widest the two-row pad gets, px: wider keys than ~110 px are harder to hit in a row, not easier.</summary>
        public const double TwoRowsMaxWidth = 880;
        /// <summary>
        /// Two-row pad: width of «Undo changes» / «Done» in digit keys. At 1.7 the key was 185 px on a 1024 px
        /// screen and «Отменить изменения» wrapped into two lines; at 2 it is 209 px and the caption fits.
        /// </summary>
        public const double ActionWeight = 2;
        /// <summary>Widest the 4×4 pad gets, px.</summary>
        public const double PadMaxWidth = 520;

        /// <summary>Letter rows of the <see cref="OnScreenKeyboardTextVariant.Phone"/> look.</summary>
        /// <param name="layout"><c>ru</c> or <c>en</c>.</param>
        /// <param name="symbols">The «?123» layer: digits and punctuation instead of letters.</param>
        public static OnScreenKeyboardKey[][] PhoneLetters(string layout, bool symbols)
        {
            var ru = layout == "ru";
            string[] r1, r2, r3;
            if (symbols)
            {
                r1 = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
                r2 = new[] { "@", "-", "_", "/", "(", ")", "\"", "'", ":", ";" };
                r3 = new[] { "+", "*", "#", "%", "!", "?", "&", "№" };
            }
            else if (ru)
            {
                r1 = Chars("йцукенгшщзхъ");
                r2 = Chars("фывапролджэё");
                r3 = Chars("ячсмитьбю");
            }
            else
            {
                r1 = Chars("qwertyuiop");
                r2 = Chars("asdfghjkl");
                r3 = Chars("zxcvbnm");
            }

            var row1 = r1.Select(k => new OnScreenKeyboardKey(k)).ToArray();

            // English middle row is one key shorter: half-key insets keep it under the top row, like a real keyboard.
            var inset = !symbols && !ru;
            var row2 = new List<OnScreenKeyboardKey>();
            if (inset) row2.Add(new OnScreenKeyboardKey(OnScreenKeyboardKeys.Blank, 0.5));
            row2.AddRange(r2.Select(k => new OnScreenKeyboardKey(k)));
            if (inset) row2.Add(new OnScreenKeyboardKey(OnScreenKeyboardKeys.Blank, 0.5));

            var row3 = new List<OnScreenKeyboardKey>();
            if (!symbols) row3.Add(new OnScreenKeyboardKey(Shift, 1.5, "mod"));
            row3.AddRange(r3.Select(k => new OnScreenKeyboardKey(k)));
            row3.Add(new OnScreenKeyboardKey(OnScreenKeyboardKeys.Backspace, 1.5, "mod"));

            var bottom = new[]
            {
                new OnScreenKeyboardKey(symbols ? Letters : Symbols, 1.5, "mod"),
                new OnScreenKeyboardKey(Language, 1.3, "mod"),
                new OnScreenKeyboardKey(Space, 6.6),
                new OnScreenKeyboardKey(".", 1, "mod"),
                new OnScreenKeyboardKey(OnScreenKeyboardKeys.Enter, 2.2, "go"),
            };

            return new[] { row1, row2.ToArray(), row3.ToArray(), bottom };
        }

        /// <summary>Digit rows of a new look.</summary>
        /// <param name="variant"><see cref="OnScreenKeyboardDigitVariant.TwoRows"/> or <see cref="OnScreenKeyboardDigitVariant.Pad"/>.</param>
        /// <param name="withSeparator">The field takes fractions: the separator key types the culture's separator.</param>
        /// <param name="dimSeparator">
        /// Whole-number fields: true — the separator key stays in place dimmed and cannot be pressed (the keys
        /// do not jump from field to field); false — its cell is empty.
        /// </param>
        public static OnScreenKeyboardKey[][] Digits(OnScreenKeyboardDigitVariant variant, bool withSeparator, bool dimSeparator)
        {
            var separator = withSeparator
                ? new OnScreenKeyboardKey(OnScreenKeyboardKeys.DecimalSeparator, 1, "digit")
                : dimSeparator
                    ? new OnScreenKeyboardKey(OnScreenKeyboardKeys.DecimalSeparator, 1, "digit", Enabled: false)
                    : new OnScreenKeyboardKey(OnScreenKeyboardKeys.Blank);
            var back = new OnScreenKeyboardKey(OnScreenKeyboardKeys.Backspace, 1, "mod");

            switch (variant)
            {
                case OnScreenKeyboardDigitVariant.TwoRows:
                    return new[]
                    {
                        new[] { D("1"), D("2"), D("3"), D("4"), D("5"), back, new OnScreenKeyboardKey(OnScreenKeyboardKeys.Cancel, ActionWeight, "quiet") },
                        new[] { D("6"), D("7"), D("8"), D("9"), D("0"), separator, new OnScreenKeyboardKey(OnScreenKeyboardKeys.Enter, ActionWeight, "go") },
                    };

                case OnScreenKeyboardDigitVariant.Pad:
                    return new[]
                    {
                        new[] { D("1"), D("2"), D("3"), back },
                        new[] { D("4"), D("5"), D("6"), separator },
                        new[] { D("7"), D("8"), D("9"), D("0") },
                        new[] { new OnScreenKeyboardKey(OnScreenKeyboardKeys.Cancel, 2, "quiet"), new OnScreenKeyboardKey(OnScreenKeyboardKeys.Enter, 2, "go") },
                    };

                default:
                    throw new ArgumentOutOfRangeException(nameof(variant), variant, "The classic pad is built from OnScreenKeyboardKeys.DigitRows.");
            }
        }

        /// <summary>Widest a digit look gets, px: the pad is centred once the screen is wider.</summary>
        public static double DigitsMaxWidth(OnScreenKeyboardDigitVariant variant)
            => variant == OnScreenKeyboardDigitVariant.TwoRows ? TwoRowsMaxWidth : PadMaxWidth;

        private static OnScreenKeyboardKey D(string digit) => new(digit, 1, "digit");

        private static string[] Chars(string letters) => letters.Select(c => c.ToString()).ToArray();
    }
}
