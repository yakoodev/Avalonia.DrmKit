using Avalonia.Input.TextInput;
using System;
using System.Globalization;

namespace Avalonia.DrmKit.OnScreenKeyboard
{
    /// <summary>What the on-screen keyboard shows for the focused text box.</summary>
    public enum OnScreenKeyboardMode
    {
        /// <summary>Full letter layout (en / ru).</summary>
        Text,
        /// <summary>Digits only: 0–9.</summary>
        Digits,
        /// <summary>Digits and a decimal separator.</summary>
        Decimal,
    }

    /// <summary>
    /// Text the action keys show. Defaults follow <see cref="CultureInfo.CurrentUICulture"/> (Russian or
    /// English); an app can set its own wording at startup. The tokens in the layout rows stay fixed
    /// (<see cref="OnScreenKeyboardKeys"/>), only the captions change.
    /// </summary>
    public static class OnScreenKeyboardLabels
    {
        private static string? _done, _hide, _cancelChanges;

        private static bool IsRussian => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";

        /// <summary>Confirm the value and close the keyboard.</summary>
        public static string Done { get => _done ?? (IsRussian ? "Готово" : "Done"); set => _done = value; }

        /// <summary>Close the keyboard and keep what was typed.</summary>
        public static string Hide { get => _hide ?? (IsRussian ? "Скрыть" : "Hide"); set => _hide = value; }

        /// <summary>Restore the text the field had when the keyboard opened, then close.</summary>
        public static string CancelChanges { get => _cancelChanges ?? (IsRussian ? "Отменить изменения" : "Undo changes"); set => _cancelChanges = value; }
    }

    /// <summary>
    /// Key tokens, the digit layouts and token → caption. Kept free of UI types so it can be unit-tested.
    ///
    /// Why the action keys changed (12.0.9): the keyboard had «Enter» and «Cancel», and «Cancel» silently
    /// rolled the text back — people used it to get rid of the keyboard and lost what they had typed.
    /// Now there are three distinct actions: <c>enter</c> = «Done» (confirm, close), <c>hide</c> = «Hide»
    /// (close, keep the text), <c>cancel</c> = «Undo changes» (roll back, close).
    /// </summary>
    public static class OnScreenKeyboardKeys
    {
        public const string Enter = "enter";
        public const string Hide = "hide";
        public const string Cancel = "cancel";
        public const string Backspace = "<-";

        /// <summary>An empty cell that only keeps the grid aligned.</summary>
        public const string Blank = "";

        /// <summary>Keyboard mode for a text box: digits for <c>Digits</c>/<c>Pin</c>, decimal for <c>Number</c>.</summary>
        public static OnScreenKeyboardMode ModeFor(TextInputContentType contentType) => contentType switch
        {
            TextInputContentType.Digits => OnScreenKeyboardMode.Digits,
            TextInputContentType.Pin => OnScreenKeyboardMode.Digits,
            TextInputContentType.Number => OnScreenKeyboardMode.Decimal,
            _ => OnScreenKeyboardMode.Text,
        };

        /// <summary>
        /// Digit pad: 7-8-9 / 4-5-6 / 1-2-3 / 0, actions in the right column — backspace, Hide, Undo changes,
        /// Done. Backspace stays the «&lt;-» token: the Inter font draws it as an arrow (no extra glyph needed).
        /// The decimal pad has a separator key left of 0 (the culture's separator is inserted).
        /// </summary>
        public static string[][] DigitRows(bool withSeparator) => new[]
        {
            new[] { "7", "8", "9", Backspace },
            new[] { "4", "5", "6", Hide },
            new[] { "1", "2", "3", Cancel },
            new[] { withSeparator ? DecimalSeparator : Blank, "0", Blank, Enter },
        };

        /// <summary>Decimal separator of the UI culture («,» in Russian, «.» in English).</summary>
        public static string DecimalSeparator => CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

        /// <summary>Caption for a key token; letters and digits are shown as they are.</summary>
        public static string Caption(string token) => token.ToLowerInvariant() switch
        {
            Enter => OnScreenKeyboardLabels.Done,
            Hide => OnScreenKeyboardLabels.Hide,
            Cancel => OnScreenKeyboardLabels.CancelChanges,
            _ => token,
        };

        /// <summary>Action keys get the accent style.</summary>
        public static bool IsAction(string token) => token.ToLowerInvariant() is Enter or Hide or Cancel or Backspace or "del" or "eng" or "rus";
    }
}
