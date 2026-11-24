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
    /// Which keys stand where is in <see cref="OnScreenKeyboardLayouts"/>; this file turns the rows into buttons.
    /// </summary>
    public partial class OnScreenKeyboard
    {
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

            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8, Margin = new Thickness(4, 0, 0, 8) };
            grid.Children.Add(caption);

            // Буквы: «Отменить изменения» стоит здесь, рядом со «Скрыть», а не в нижнем ряду: там она была
            // между точкой и «Готово», и по ней попадали вместо «Готово» (вариант Б3, решение 24.11.2026).
            // У цифр она в сетке клавиш (OnScreenKeyboardLayouts.Digits).
            if (_mode == OnScreenKeyboardMode.Text)
            {
                var undo = MakeKey(new OnScreenKeyboardKey(OnScreenKeyboardKeys.Cancel, 1, "pill quiet"));
                undo.MinHeight = 40;
                Grid.SetColumn(undo, 1);
                grid.Children.Add(undo);
            }

            var hide = MakeKey(new OnScreenKeyboardKey(OnScreenKeyboardKeys.Hide, 1, "pill"));
            hide.MinHeight = 40;
            Grid.SetColumn(hide, 2);
            grid.Children.Add(hide);
            return grid;
        }

        private Control BuildLetters(double h)
            => Stack(OnScreenKeyboardLayouts.PhoneLetters(KeyboardLayoutManager.Current, _symbols)
                .Select(row => (Control)Row(h, row)).ToArray());

        private Control BuildDigits(double h)
        {
            var variant = OnScreenKeyboardStyle.DigitVariant;
            if (variant == OnScreenKeyboardDigitVariant.Calculator)
                return BuildGrid(h, OnScreenKeyboardLayouts.CalculatorDigits(
                    withSeparator: _mode == OnScreenKeyboardMode.Decimal,
                    dimSeparator: OnScreenKeyboardStyle.DimSeparatorInIntegerFields));

            var rows = OnScreenKeyboardLayouts.Digits(variant,
                withSeparator: _mode == OnScreenKeyboardMode.Decimal,
                dimSeparator: OnScreenKeyboardStyle.DimSeparatorInIntegerFields);
            var pad = Stack(rows.Select(row => (Control)Row(h, row)).ToArray());
            // Stretch + MaxWidth: растянутый элемент, упёршийся в MaxWidth, встаёт по центру.
            pad.MaxWidth = OnScreenKeyboardLayouts.DigitsMaxWidth(variant);
            pad.HorizontalAlignment = HorizontalAlignment.Stretch;
            return pad;
        }

        // ---------- building blocks ----------

        /// <summary>Раскладка-сетка: клавиши могут занимать два ряда (высокое «Готово») или два столбца (широкий ноль).</summary>
        private Grid BuildGrid(double h, OnScreenKeyboardGridLayout layout)
        {
            // Stretch + MaxWidth: растянутый элемент, упёршийся в MaxWidth, встаёт по центру.
            var grid = new Grid { RowSpacing = Gap, ColumnSpacing = Gap, MaxWidth = layout.MaxWidth, HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (var weight in layout.Columns)
                grid.ColumnDefinitions.Add(new ColumnDefinition(weight, GridUnitType.Star));
            for (var row = 0; row < layout.Rows; row++)
                grid.RowDefinitions.Add(new RowDefinition(h, GridUnitType.Pixel));
            foreach (var cell in layout.Cells)
            {
                if (cell.Key.IsBlank)
                    continue;
                var key = MakeKey(cell.Key);
                Grid.SetRow(key, cell.Row);
                Grid.SetColumn(key, cell.Column);
                Grid.SetRowSpan(key, cell.RowSpan);
                Grid.SetColumnSpan(key, cell.ColumnSpan);
                grid.Children.Add(key);
            }
            return grid;
        }

        private static StackPanel Stack(params Control[] rows)
        {
            var panel = new StackPanel { Spacing = Gap };
            foreach (var row in rows)
                panel.Children.Add(row);
            return panel;
        }

        private Grid Row(double h, OnScreenKeyboardKey[] keys)
        {
            var grid = new Grid { Height = h, ColumnSpacing = Gap };
            for (var i = 0; i < keys.Length; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition(keys[i].Weight, GridUnitType.Star));
                Control cell = keys[i].IsBlank
                    ? new Border { IsHitTestVisible = false }
                    : MakeKey(keys[i]);
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
            }
            return grid;
        }

        private Button MakeKey(OnScreenKeyboardKey key)
        {
            var token = key.Token;
            var upper = _caps || _shift;
            var letter = upper && token.Length == 1 && char.IsLetter(token[0]) ? token.ToUpperInvariant() : token;
            var layout = KeyboardLayoutManager.Current;
            var (content, name) = token switch
            {
                OnScreenKeyboardKeys.Backspace => ((object)Icon(IconBackspace, filled: false), "Backspace"),
                OnScreenKeyboardLayouts.Shift => (Icon(IconShift, filled: upper), "Shift"),
                OnScreenKeyboardKeys.Hide => (WithIcon(OnScreenKeyboardLabels.Hide, IconChevronDown, iconFirst: true), OnScreenKeyboardLabels.Hide),
                OnScreenKeyboardKeys.Enter => (WithIcon(OnScreenKeyboardLabels.Done, IconCheck, iconFirst: false), OnScreenKeyboardLabels.Done),
                OnScreenKeyboardKeys.Cancel => (Wrapped(OnScreenKeyboardLabels.CancelChanges), OnScreenKeyboardLabels.CancelChanges),
                OnScreenKeyboardLayouts.Symbols => (OnScreenKeyboardText.Symbols, OnScreenKeyboardText.Symbols),
                OnScreenKeyboardLayouts.Letters => (OnScreenKeyboardText.Letters(layout), OnScreenKeyboardText.Letters(layout)),
                OnScreenKeyboardLayouts.Language => (layout == "ru" ? "EN" : "RU", layout == "ru" ? "EN" : "RU"),
                OnScreenKeyboardLayouts.Space => (OnScreenKeyboardText.Space, OnScreenKeyboardText.Space),
                _ => (letter, letter),
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
            foreach (var cls in key.Class.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                btn.Classes.Add(cls);
            if (token == OnScreenKeyboardLayouts.Shift && upper)
                btn.Classes.Add("on");
            if (!key.Enabled)
            {
                // Клавиша на месте, но приглушена и не нажимается: запятая в поле с целыми числами.
                btn.IsEnabled = false;
                btn.Classes.Add("off");
            }
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
                case OnScreenKeyboardLayouts.Shift:
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

                case OnScreenKeyboardLayouts.Symbols:
                case OnScreenKeyboardLayouts.Letters:
                    _symbols = key == OnScreenKeyboardLayouts.Symbols;
                    Rebuild();
                    return true;

                case OnScreenKeyboardLayouts.Language:
                    _userPickedLayout = true;
                    KeyboardLayoutManager.SetCurrent(KeyboardLayoutManager.Current == "ru" ? "en" : "ru");
                    return true;
            }
            return false;
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
