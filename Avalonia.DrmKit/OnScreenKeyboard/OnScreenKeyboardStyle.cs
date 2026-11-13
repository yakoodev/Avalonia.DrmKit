using Avalonia.Controls;
using System;
using System.Globalization;

namespace Avalonia.DrmKit.OnScreenKeyboard
{
    /// <summary>Letter keyboard look. <see cref="Classic"/> is the keyboard up to 12.0.10.</summary>
    public enum OnScreenKeyboardTextVariant
    {
        /// <summary>The keyboard up to 12.0.10: a row of symbols, digits, letters and six action keys.</summary>
        Classic,
        /// <summary>Like a phone: ⇧ and ⌫ at the ends of the third row, «Done» at the end of the bottom row.</summary>
        Phone,
        /// <summary>Letters on the left, ⌫ / «Undo changes» / a large «Done» in a column on the right.</summary>
        ActionColumn,
    }

    /// <summary>Digit keyboard look. <see cref="Classic"/> is the pad up to 12.0.10.</summary>
    public enum OnScreenKeyboardDigitVariant
    {
        /// <summary>The pad up to 12.0.10: 7-8-9 on top, four action keys in the right column.</summary>
        Classic,
        /// <summary>4×4 grid 1-2-3 on top, ⌫ and the separator in the right column, «Undo changes» and «Done» in a row below the digits.</summary>
        Pad,
        /// <summary>Two low rows: digits 1…0 and ⌫, then −1 / +1 and the actions.</summary>
        Row,
    }

    /// <summary>
    /// Which keyboard the app shows. Both default to <c>Classic</c>, so an app that does not set them keeps
    /// the old keyboard; changing them rebuilds an open keyboard at once.
    /// </summary>
    public static class OnScreenKeyboardStyle
    {
        private static OnScreenKeyboardTextVariant _text;
        private static OnScreenKeyboardDigitVariant _digits;

        /// <summary>A variant changed; an open keyboard rebuilds itself.</summary>
        public static event EventHandler? Changed;

        /// <summary>Letter keyboard look.</summary>
        public static OnScreenKeyboardTextVariant TextVariant
        {
            get => _text;
            set { if (_text == value) return; _text = value; Changed?.Invoke(null, EventArgs.Empty); }
        }

        /// <summary>Digit keyboard look.</summary>
        public static OnScreenKeyboardDigitVariant DigitVariant
        {
            get => _digits;
            set { if (_digits == value) return; _digits = value; Changed?.Invoke(null, EventArgs.Empty); }
        }
    }

    /// <summary>
    /// What the keyboard says about the field it types into (shown in the strip above the keys of the new
    /// variants) and how it types there.
    /// </summary>
    public static class OnScreenKeyboardField
    {
        /// <summary>Field name in the strip. Without it the field's placeholder text is shown.</summary>
        public static readonly AttachedProperty<string?> TitleProperty =
            AvaloniaProperty.RegisterAttached<TextBox, string?>("Title", typeof(OnScreenKeyboardField));

        /// <summary>A hint next to the name, e.g. «от 0 до 59».</summary>
        public static readonly AttachedProperty<string?> HintProperty =
            AvaloniaProperty.RegisterAttached<TextBox, string?>("Hint", typeof(OnScreenKeyboardField));

        /// <summary>Names: every word starts with a capital letter (one-shot ⇧ is switched on by itself).</summary>
        public static readonly AttachedProperty<bool> CapitalizeWordsProperty =
            AvaloniaProperty.RegisterAttached<TextBox, bool>("CapitalizeWords", typeof(OnScreenKeyboardField));

        /// <summary>Gets <see cref="TitleProperty"/>.</summary>
        public static string? GetTitle(TextBox box) => box.GetValue(TitleProperty);
        /// <summary>Sets <see cref="TitleProperty"/>.</summary>
        public static void SetTitle(TextBox box, string? value) => box.SetValue(TitleProperty, value);
        /// <summary>Gets <see cref="HintProperty"/>.</summary>
        public static string? GetHint(TextBox box) => box.GetValue(HintProperty);
        /// <summary>Sets <see cref="HintProperty"/>.</summary>
        public static void SetHint(TextBox box, string? value) => box.SetValue(HintProperty, value);
        /// <summary>Gets <see cref="CapitalizeWordsProperty"/>.</summary>
        public static bool GetCapitalizeWords(TextBox box) => box.GetValue(CapitalizeWordsProperty);
        /// <summary>Sets <see cref="CapitalizeWordsProperty"/>.</summary>
        public static void SetCapitalizeWords(TextBox box, bool value) => box.SetValue(CapitalizeWordsProperty, value);
    }

    /// <summary>Captions of the new variants, Russian or English by <see cref="CultureInfo.CurrentUICulture"/>.</summary>
    internal static class OnScreenKeyboardText
    {
        private static bool IsRussian => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";

        public static string Space => IsRussian ? "пробел" : "space";
        public static string Symbols => "?123";
        public static string Letters(string layout) => layout == "ru" ? "АБВ" : "ABC";
        public static string Was(string value) => IsRussian ? $"было {value}" : $"was {value}";
        public static string Undo => IsRussian ? "Отменить" : "Undo";
    }
}
