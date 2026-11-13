using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.DrmKit.Input;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Globalization;
using System.Linq;

namespace Avalonia.DrmKit.OnScreenKeyboard
{
    /// <summary>
    /// The new keyboard variants (<see cref="OnScreenKeyboardStyle"/>): the app's look, a strip with what is
    /// being typed and its limits, the UI language first, one-shot ⇧, a «?123» layer instead of a fixed row of
    /// symbols, and one clearly marked «Done». The classic layout stays as it was for apps that keep it.
    /// </summary>
    public partial class OnScreenKeyboard
    {
        private const string ShiftKey = "shift";
        private const string SymbolsKey = "sym";
        private const string LettersKey = "abc";
        private const string LanguageKey = "lang";
        private const string MinusOneKey = "minus1";
        private const string PlusOneKey = "plus1";
        private const double Gap = 7;

        private bool _shift;
        private bool _symbols;
        private bool _userPickedLayout;
        private DateTime _lastShiftTap;

        /// <summary>The field asks for the new look (letters or digits — each has its own setting).</summary>
        private bool IsModern => _mode == OnScreenKeyboardMode.Text
            ? OnScreenKeyboardStyle.TextVariant != OnScreenKeyboardTextVariant.Classic
            : OnScreenKeyboardStyle.DigitVariant != OnScreenKeyboardDigitVariant.Classic;

        /// <summary>Called on attach: the UI language first (until the user switches), letters, auto-⇧ for names.</summary>
        private void PrepareModern(TextBox textBox)
        {
            if (!IsModern)
                return;
            if (!_userPickedLayout)
                KeyboardLayoutManager.SetCurrent(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en");
            _symbols = false;
            UpdateAutoShift(textBox);
        }

        /// <summary>Names: ⇧ is on at the start of every word, off inside a word.</summary>
        private void UpdateAutoShift(TextBox textBox)
        {
            if (_mode != OnScreenKeyboardMode.Text || !OnScreenKeyboardField.GetCapitalizeWords(textBox))
                return;
            var text = textBox.Text ?? string.Empty;
            var caret = Math.Min(textBox.CaretIndex, text.Length);
            _shift = caret == 0 || char.IsWhiteSpace(text[caret - 1]);
        }

        /// <summary>Keys get shorter on low screens: 54 px at 600, 44 px at 480, at most 58.</summary>
        private double KeyHeight()
        {
            var height = TopLevel.GetTopLevel(this)?.Bounds.Height ?? 0;
            return height > 0 ? Math.Clamp(Math.Round(height * 0.09), 44, 58) : 54;
        }

        private void BuildModern(ContentControl host)
        {
            var h = KeyHeight();
            var root = new DockPanel { Background = Brushes.Transparent, LastChildFill = true };
            var strip = BuildStrip();
            DockPanel.SetDock(strip, Dock.Top);
            root.Children.Add(strip);
            root.Children.Add(_mode == OnScreenKeyboardMode.Text ? BuildLetters(h) : BuildDigits(h));
            host.Content = root;
        }

        private Control BuildStrip()
        {
            var title = _target is { } tb
                ? OnScreenKeyboardField.GetTitle(tb) ?? tb.PlaceholderText ?? AutomationProperties.GetName(tb)
                : null;
            title = title?.Trim().TrimEnd(':').TrimEnd();   // подпись поля «Сек:» — в полоске без двоеточия
            var hint = _target is { } t ? OnScreenKeyboardField.GetHint(t) : null;
            if (_mode != OnScreenKeyboardMode.Text && _sessionActive && _sessionOriginalText.Length > 0)
                hint = string.IsNullOrEmpty(hint) ? OnScreenKeyboardText.Was(_sessionOriginalText) : $"{hint} · {OnScreenKeyboardText.Was(_sessionOriginalText)}";

            var caption = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            caption.Classes.Add("osk-strip");
            if (!string.IsNullOrEmpty(title))
                caption.Inlines!.Add(new Run(title) { FontWeight = FontWeight.SemiBold });
            if (!string.IsNullOrEmpty(hint))
            {
                var run = new Run((string.IsNullOrEmpty(title) ? "" : "   ") + hint);
                run.Classes.Add("osk-hint");
                caption.Inlines!.Add(run);
            }

            var hide = MakeKey(OnScreenKeyboardKeys.Hide, "pill");
            hide.MinHeight = 40;
            hide.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(hide, 1);

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(4, 0, 0, 8) };
            grid.Children.Add(caption);
            grid.Children.Add(hide);
            return grid;
        }

        // ---------- letters ----------

        private Control BuildLetters(double h)
        {
            var ru = KeyboardLayoutManager.Current == "ru";
            var phone = OnScreenKeyboardStyle.TextVariant == OnScreenKeyboardTextVariant.Phone;
            string[] r1, r2, r3;
            if (_symbols)
            {
                r1 = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
                r2 = new[] { "@", "-", "_", "/", "(", ")", "\"", "'", ":", ";" };
                r3 = phone ? new[] { "+", "*", "#", "%", "!", "?", "&", "№" } : new[] { "+", "*", "#", "%", "!", "?", "&", "№", ",", "." };
            }
            else if (ru)
            {
                r1 = "йцукенгшщзхъ".Select(c => c.ToString()).ToArray();
                r2 = "фывапролджэё".Select(c => c.ToString()).ToArray();
                r3 = "ячсмитьбю".Select(c => c.ToString()).ToArray();
            }
            else
            {
                r1 = "qwertyuiop".Select(c => c.ToString()).ToArray();
                r2 = "asdfghjkl".Select(c => c.ToString()).ToArray();
                r3 = "zxcvbnm".Select(c => c.ToString()).ToArray();
            }

            var row1 = Row(h, r1.Select(k => (k, 1.0, "")).ToArray());
            // English middle row is one key shorter: half-key insets keep it under the top row, like a real keyboard.
            var row2 = Row(h, (!_symbols && !ru ? new[] { ("", 0.5, "") } : Array.Empty<(string, double, string)>())
                .Concat(r2.Select(k => (k, 1.0, "")))
                .Concat(!_symbols && !ru ? new[] { ("", 0.5, "") } : Array.Empty<(string, double, string)>()).ToArray());
            var row3Keys = r3.Select(k => (k, 1.0, "")).ToList();
            if (!_symbols)
                row3Keys.Insert(0, (ShiftKey, 1.5, "mod"));
            if (phone)
                row3Keys.Add((OnScreenKeyboardKeys.Backspace, 1.5, "mod"));
            else if (!_symbols)
            {
                row3Keys.Add((",", 0.75, "mod"));
                row3Keys.Add((".", 0.75, "mod"));
            }
            var row3 = Row(h, row3Keys.ToArray());

            var layerKey = _symbols ? LettersKey : SymbolsKey;
            var bottom = phone
                ? Row(h, (layerKey, 1.5, "mod"), (LanguageKey, 1.3, "mod"), ("space", 5, ""), (".", 1, "mod"),
                         (OnScreenKeyboardKeys.Cancel, 1.6, "quiet"), (OnScreenKeyboardKeys.Enter, 2.2, "go"))
                : Row(h, (layerKey, 1.5, "mod"), (LanguageKey, 1.5, "mod"), ("space", 8, ""));

            var letters = Stack(row1, row2, row3, bottom);
            if (phone)
                return letters;

            // Action column: ⌫ level with the top row, «Undo changes» next, «Done» two rows high.
            var column = new Grid { RowDefinitions = new RowDefinitions($"{h},{h},*"), RowSpacing = Gap };
            var back = MakeKey(OnScreenKeyboardKeys.Backspace, "mod");
            var undo = MakeKey(OnScreenKeyboardKeys.Cancel, "quiet");
            var done = MakeKey(OnScreenKeyboardKeys.Enter, "go");
            Grid.SetRow(undo, 1);
            Grid.SetRow(done, 2);
            column.Children.Add(back);
            column.Children.Add(undo);
            column.Children.Add(done);

            var split = new Grid { ColumnDefinitions = new ColumnDefinitions("5.2*,*"), ColumnSpacing = Gap + 3 };
            Grid.SetColumn(column, 1);
            split.Children.Add(letters);
            split.Children.Add(column);
            return split;
        }

        // ---------- digits ----------

        private Control BuildDigits(double h)
        {
            var sep = _mode == OnScreenKeyboardMode.Decimal ? OnScreenKeyboardKeys.DecimalSeparator : "";
            if (OnScreenKeyboardStyle.DigitVariant == OnScreenKeyboardDigitVariant.Row)
            {
                var digits = Row(h, new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" }.Select(k => (k, 1.0, "digit"))
                    .Append((OnScreenKeyboardKeys.Backspace, 1.3, "mod")).ToArray());
                var actions = Row(h, (MinusOneKey, 1.6, "mod"), (PlusOneKey, 1.6, "mod"), (sep, 1, "mod"), ("", 3.6, ""),
                                     (OnScreenKeyboardKeys.Cancel, 1.6, "quiet"), (OnScreenKeyboardKeys.Enter, 2.5, "go"));
                return Stack(digits, actions);
            }

            // Сетка 4×4 по центру: ⌫ и разделитель в правом столбце, «Отменить изменения» и «Готово»
            // отдельной строкой под цифрами (вариант Е, решение 13.11). У целых полей место разделителя пустое.
            var pad = Stack(
                Row(h, ("1", 1, "digit"), ("2", 1, "digit"), ("3", 1, "digit"), (OnScreenKeyboardKeys.Backspace, 1, "mod")),
                Row(h, ("4", 1, "digit"), ("5", 1, "digit"), ("6", 1, "digit"), (sep, 1, "mod")),
                Row(h, ("7", 1, "digit"), ("8", 1, "digit"), ("9", 1, "digit"), ("0", 1, "digit")),
                Row(h, (OnScreenKeyboardKeys.Cancel, 2, "quiet"), (OnScreenKeyboardKeys.Enter, 2, "go")));
            pad.MaxWidth = 520;
            pad.HorizontalAlignment = HorizontalAlignment.Stretch;
            return pad;
        }

        // ---------- building blocks ----------

        private static StackPanel Stack(params Control[] rows)
        {
            var panel = new StackPanel { Spacing = Gap };
            foreach (var row in rows)
                panel.Children.Add(row);
            return panel;
        }

        private Grid Row(double h, params (string token, double weight, string cls)[] keys)
        {
            var grid = new Grid { Height = h, ColumnSpacing = Gap };
            for (var i = 0; i < keys.Length; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(keys[i].weight, GridUnitType.Star));
                Control cell = keys[i].token.Length == 0
                    ? new Border { IsHitTestVisible = false }
                    : MakeKey(keys[i].token, keys[i].cls);
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
            }
            return grid;
        }

        private Button MakeKey(string token, string cls)
        {
            var upper = _caps || _shift;
            var (content, name) = token switch
            {
                OnScreenKeyboardKeys.Backspace => ((object)Icon(IconBackspace, filled: false), "Backspace"),
                ShiftKey => (Icon(IconShift, filled: upper), "Shift"),
                OnScreenKeyboardKeys.Hide => (WithIcon(OnScreenKeyboardLabels.Hide, IconChevronDown, iconFirst: true), OnScreenKeyboardLabels.Hide),
                OnScreenKeyboardKeys.Enter => (WithIcon(OnScreenKeyboardLabels.Done, IconCheck, iconFirst: false), OnScreenKeyboardLabels.Done),
                OnScreenKeyboardKeys.Cancel => (Wrapped(cls == "quiet" && OnScreenKeyboardStyle.TextVariant == OnScreenKeyboardTextVariant.Phone && _mode == OnScreenKeyboardMode.Text
                                                    ? OnScreenKeyboardText.Undo : OnScreenKeyboardLabels.CancelChanges), OnScreenKeyboardLabels.CancelChanges),
                SymbolsKey => (OnScreenKeyboardText.Symbols, OnScreenKeyboardText.Symbols),
                LettersKey => (OnScreenKeyboardText.Letters(KeyboardLayoutManager.Current), OnScreenKeyboardText.Letters(KeyboardLayoutManager.Current)),
                LanguageKey => (KeyboardLayoutManager.Current == "ru" ? "EN" : "RU", KeyboardLayoutManager.Current == "ru" ? "EN" : "RU"),
                MinusOneKey => ("−1", "−1"),
                PlusOneKey => ("+1", "+1"),
                "space" => (OnScreenKeyboardText.Space, OnScreenKeyboardText.Space),
                _ => (upper && token.Length == 1 && char.IsLetter(token[0]) ? token.ToUpperInvariant() : token,
                      upper && token.Length == 1 && char.IsLetter(token[0]) ? token.ToUpperInvariant() : token),
            };

            var btn = new Button
            {
                Content = content,
                IsTabStop = false,
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            btn.Classes.Add("osk-key");
            if (cls.Length > 0)
                btn.Classes.Add(cls);
            if (token == ShiftKey && (_caps || _shift))
                btn.Classes.Add("on");
            AutomationProperties.SetName(btn, name);
            btn.Click += (_, __) => HandleKey(token);
            return btn;
        }

        private static Control Wrapped(string text) => new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        private static Control WithIcon(string text, Geometry icon, bool iconFirst)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
            var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
            var glyph = Icon(icon, filled: false, size: 18);
            if (iconFirst) { panel.Children.Add(glyph); panel.Children.Add(label); }
            else { panel.Children.Add(label); panel.Children.Add(glyph); }
            return panel;
        }

        /// <summary>Glyphs are drawn, not typed: the kiosk font has no ⌫ / ⇧ / ✓.</summary>
        private static Control Icon(Geometry data, bool filled, double size = 24)
        {
            var path = new Avalonia.Controls.Shapes.Path
            {
                Data = data,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                StrokeThickness = 2,
                StrokeJoin = PenLineJoin.Round,
                StrokeLineCap = PenLineCap.Round,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            path.Classes.Add(filled ? "osk-icon-filled" : "osk-icon");
            return path;
        }

        private static readonly Geometry IconBackspace = Geometry.Parse("M9,5 L21,5 L21,19 L9,19 L3,12 Z M12,9 L17,15 M17,9 L12,15");
        private static readonly Geometry IconShift = Geometry.Parse("M12,3 L21,12.5 L16,12.5 L16,20 L8,20 L8,12.5 L3,12.5 Z");
        private static readonly Geometry IconChevronDown = Geometry.Parse("M5,9 L12,16 L19,9");
        private static readonly Geometry IconCheck = Geometry.Parse("M4,12.5 L9.5,18 L20,6");

        // ---------- keys of the new variants ----------

        /// <summary>Handles the keys only the new variants have. Returns false for everything else.</summary>
        private bool HandleModernKey(string key)
        {
            switch (key)
            {
                case ShiftKey:
                    // One tap — the next letter is capital; a second tap within 0.4 s — Caps Lock; tap again — off.
                    var now = DateTime.UtcNow;
                    if (_caps)
                        KeyboardLayoutManager.SetCaps(false);
                    else if (_shift && (now - _lastShiftTap).TotalMilliseconds < 400)
                    {
                        _shift = false;
                        KeyboardLayoutManager.SetCaps(true);
                    }
                    else
                        _shift = !_shift;
                    _lastShiftTap = now;
                    Rebuild();
                    return true;

                case SymbolsKey:
                case LettersKey:
                    _symbols = key == SymbolsKey;
                    Rebuild();
                    return true;

                case LanguageKey:
                    _userPickedLayout = true;
                    KeyboardLayoutManager.SetCurrent(KeyboardLayoutManager.Current == "ru" ? "en" : "ru");
                    return true;

                case MinusOneKey:
                case PlusOneKey:
                    Step(key == PlusOneKey ? 1 : -1);
                    return true;
            }
            return false;
        }

        /// <summary>«−1» / «+1» on the digit row: nudge the number; the app's own checks still apply to the text.</summary>
        private void Step(int delta)
        {
            if (_target is not { } tb)
                return;
            var sep = OnScreenKeyboardKeys.DecimalSeparator;
            var text = (tb.Text ?? "").Trim();
            if (!double.TryParse(text.Replace(sep, "."), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                value = 0;
            value += delta;
            if (value < 0 && _mode != OnScreenKeyboardMode.Decimal)
                value = 0;
            tb.Text = value.ToString(CultureInfo.InvariantCulture).Replace(".", sep);
            tb.SelectionStart = tb.SelectionEnd = tb.Text.Length;
            tb.Focus();
        }

        /// <summary>After a letter: one-shot ⇧ goes off; for names it comes back at the next word.</summary>
        private void AfterModernInput()
        {
            if (!IsModern || _target is null)
                return;
            var wasShift = _shift;
            _shift = false;
            UpdateAutoShift(_target);
            if (wasShift != _shift)
                Rebuild();
        }
    }
}
