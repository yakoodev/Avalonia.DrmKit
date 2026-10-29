using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.DrmKit.Input;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace Avalonia.DrmKit.OnScreenKeyboard
{
    public partial class OnScreenKeyboard : UserControl
    {
        private bool _caps;
        private TextBox? _target;
        private TextBox? _sessionTarget;
        private string _sessionOriginalText = string.Empty;
        private bool _sessionActive;
        private OnScreenKeyboardMode _mode = OnScreenKeyboardMode.Text;
        public event EventHandler? CloseRequested;

        /// <summary>Letters, digits or digits with a separator — chosen from the focused text box.</summary>
        public OnScreenKeyboardMode Mode => _mode;

        /// <summary>Widest the digit pad gets: full width on 1280 px gave 300 px keys; 640 px cut «Undo changes».</summary>
        public const double DigitPadMaxWidth = 840;



        public OnScreenKeyboard()
        {
            InitializeComponent();
            Focusable = false;
            IsTabStop = false;

            _caps = KeyboardLayoutManager.Caps;

            KeyboardLayoutManager.CurrentChanged += (_, __) => Rebuild();
            KeyboardLayoutManager.CapsChanged += (_, on) => { _caps = on; Rebuild(); };

            Rebuild();
        }

        public void Attach(TextBox textBox)
        {
            _target = textBox;
            BeginEditSession(textBox);

            // The field asks for digits (TextInputOptions.ContentType = Digits / Number / Pin) — show the
            // digit pad instead of the letter layout.
            var mode = OnScreenKeyboardKeys.ModeFor(TextInputOptions.GetContentType(textBox));
            if (mode != _mode)
            {
                _mode = mode;
                Rebuild();
            }
        }

        private void BeginEditSession(TextBox textBox)
        {
            if (_sessionActive && ReferenceEquals(_sessionTarget, textBox))
                return;

            _sessionTarget = textBox;
            _sessionOriginalText = textBox.Text ?? string.Empty;
            _sessionActive = true;
        }

        private void EndEditSession()
        {
            _sessionActive = false;
            _sessionTarget = null;
            _sessionOriginalText = string.Empty;
        }

        public void OnOverlayHidden()
        {
            EndEditSession();
        }

        private void Rebuild()
        {
            var rows = _mode == OnScreenKeyboardMode.Text
                ? KeyboardLayoutManager.GetCurrent().Rows
                : OnScreenKeyboardKeys.DigitRows(withSeparator: _mode == OnScreenKeyboardMode.Decimal);

            if (this.Find<Grid>("KeysGrid") is { } keysGrid)
            {
                // Stretch + MaxWidth: a stretched element that hits MaxWidth is centred. Center would size the
                // pad by its keys, and wrapped captions (FitCaption) would then shrink it step by step.
                keysGrid.MaxWidth = _mode == OnScreenKeyboardMode.Text ? double.PositiveInfinity : DigitPadMaxWidth;
                keysGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            var containers = new[] {
                this.Find<UniformGrid>("Row0"),
                this.Find<UniformGrid>("Row1"),
                this.Find<UniformGrid>("Row2"),
                this.Find<UniformGrid>("Row3"),
                this.Find<UniformGrid>("Row4"),
                this.Find<UniformGrid>("Row5"),
            };
            for (int r = 0; r < containers.Length; r++)
            {
                var grid = containers[r];
                if (grid is null)
                    continue;

                grid.Children.Clear();
                grid.IsVisible = r < rows.Length;
                if (r >= rows.Length)
                    continue;
                grid.Columns = rows[r].Length;
                foreach (var key in rows[r])
                    grid.Children.Add(key.Length == 0 ? new Border { IsHitTestVisible = false } : CreateKeyButton(key));
            }
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            // Любое касание по клавиатуре — держим фокус в TextBox
            _target?.Focus();
        }
        private static bool IsLetterKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            // Одиночный Unicode-символ-буква (английский, русский и т.п.)
            return key.Length == 1 && char.IsLetter(key[0]);
        }
        /// <summary>
        /// Long captions («Отменить изменения», «Undo changes») were clipped on narrow keys (800–1024 px
        /// screens). Now they wrap to the key width (see <see cref="FitCaption"/>), and whatever still does
        /// not fit is scaled down — never up, so wide keys keep the normal font.
        /// </summary>
        private static object CaptionContent(string label) => label.Length <= 2
            ? label
            : new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                Child = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            };

        /// <summary>A Viewbox measures its child unconstrained, so the wrap width has to come from the key.</summary>
        private static void FitCaption(Button btn)
        {
            if (btn.Content is not Viewbox { Child: TextBlock caption })
                return;
            btn.SizeChanged += (_, e) =>
                caption.MaxWidth = Math.Max(1, e.NewSize.Width - btn.Padding.Left - btn.Padding.Right
                                                - btn.BorderThickness.Left - btn.BorderThickness.Right);
        }

        private Button CreateKeyButton(string text)
        {
            // Caps влияет только на "одиночные буквы" (en/ru и т.п.)
            var label = IsLetterKey(text) && _caps
                ? text.ToUpperInvariant()
                : OnScreenKeyboardKeys.Caption(text);

            var btn = new Button
            {
                Content = CaptionContent(label),
                MinHeight = 42,
                IsTabStop = false,
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            AutomationProperties.SetName(btn, label);
            FitCaption(btn);

            // Базовый класс для всех клавиш ОСК
            btn.Classes.Add("osk-button");

            var lower = text.ToLowerInvariant();

            switch (lower)
            {
                // Переключатели раскладки
                case "eng":
                case "rus":
                    btn.Classes.Add("primary");
                    break;

                // Служебные клавиши: ⌫, «Очистить», «Готово», «Скрыть», «Отменить изменения»
                case "<-":     // Backspace
                case "del":
                case OnScreenKeyboardKeys.Enter:
                case OnScreenKeyboardKeys.Hide:
                case OnScreenKeyboardKeys.Cancel:
                    btn.Classes.Add("primary");
                    break;

                case "space":
                    btn.Classes.Add("wide");
                    break;

                case "caps":
                    btn.Classes.Add("wide");
                    if (_caps)
                        btn.Classes.Add("active"); // подсветка активного CAPS
                    break;
            }

            btn.Click += (_, __) => HandleKey(text);
            return btn;
        }




        private void HandleKey(string key)
        {
            key = key.ToLowerInvariant();

            if (_target is null)
            {
                var tl = TopLevel.GetTopLevel(this);
                var focused = tl?.FocusManager?.GetFocusedElement() as TextBox;
                if (focused is not null)
                {
                    _target = focused;
                    BeginEditSession(focused);
                }
            }
            if (_target is null) return;

            switch (key)
            {
                case "caps":
                    KeyboardLayoutManager.SetCaps(!_caps);   // ← менеджер синхронит с физ. клавой и перерисует OSK
                    _target.Focus();                          // удерживаем фокус
                    return;

                case "space":
                    InsertText(" ");
                    return;

                case "<-": // Backspace
                    Backspace();
                    return;

                case "del": // удалить весь текст
                    _target.Text = string.Empty;
                    _target.SelectionStart = _target.SelectionEnd = 0;
                    _target.Focus();
                    return;

                case OnScreenKeyboardKeys.Enter:
                    // «Готово»: полю — Enter (так NumericUpDown и формы принимают значение), затем закрываем
                    // ОСК и снимаем фокус. Текст не откатываем.
                    SendEnterToTarget();
                    CloseAndClearFocus(restoreOriginalText: false);
                    return;

                case OnScreenKeyboardKeys.Hide:
                    // «Скрыть»: просто убрать клавиатуру — набранное остаётся (как Esc).
                    CloseAndClearFocus(restoreOriginalText: false);
                    return;

                case OnScreenKeyboardKeys.Cancel:
                    // «Отменить изменения»: вернуть текст, который был при открытии, и закрыть.
                    CloseAndClearFocus(restoreOriginalText: true);
                    return;


                case "eng":
                    KeyboardLayoutManager.SetCurrent("en");
                    _target.Focus();
                    return;

                case "rus":
                    KeyboardLayoutManager.SetCurrent("ru");
                    _target.Focus();
                    return;

                default:
                    var text = _caps ? key.ToUpperInvariant() : key;
                    InsertText(text);
                    _target.Focus();
                    return;
            }

            void InsertText(string txt)
            {
                var content = _target!.Text ?? string.Empty;
                var start = _target.SelectionStart;
                var end = _target.SelectionEnd;
                if (end < start) (start, end) = (end, start);
                if (end > start) content = content.Remove(start, end - start);

                var t = content.Insert(start, txt);
                _target.Text = t;
                _target.SelectionStart = _target.SelectionEnd = start + txt.Length;
            }
            void CloseAndClearFocus(bool restoreOriginalText)
            {
                if (_target is { } tb)
                {
                    if (restoreOriginalText)
                    {
                        var rollback = _sessionActive && ReferenceEquals(_sessionTarget, tb)
                            ? _sessionOriginalText
                            : (tb.Text ?? string.Empty);

                        tb.Text = rollback;
                        tb.SelectionStart = tb.SelectionEnd = tb.Text?.Length ?? 0;
                    }

                    var tl = TopLevel.GetTopLevel(tb);
                    tl?.FocusManager?.Focus(null!, NavigationMethod.Unspecified, KeyModifiers.None);
                }

                EndEditSession();
                HideOverlay();
            }
            void SendEnterToTarget()
            {
                if (_target is not { } tb)
                    return;
                var args = new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Enter,
                    Source = tb,
                };
                tb.RaiseEvent(args);
            }
            void Backspace()
            {
                var content = _target!.Text ?? string.Empty;
                var start = _target.SelectionStart;
                var end = _target.SelectionEnd;
                if (end < start) (start, end) = (end, start);

                if (end > start)
                {
                    _target.Text = content.Remove(start, end - start);
                    _target.SelectionEnd = _target.SelectionStart = start;
                }
                else if (start > 0)
                {
                    _target.Text = content.Remove(start - 1, 1);
                    _target.SelectionEnd = _target.SelectionStart = start - 1;
                }
            }

            void HideOverlay()
            {
                // Сначала уведомляем хост (Overlay/Popup), что клавиатуру нужно закрыть
                CloseRequested?.Invoke(this, EventArgs.Empty);

                // Обратная совместимость: если хост — Border-оверлей (KeyboardOverlay),
                // спрячем панель напрямую.
                var panel = this.GetVisualAncestors()
                                .OfType<Border>()
                                .FirstOrDefault(b => ReferenceEquals(b.Tag, this))
                         ?? this.GetVisualAncestors().OfType<Border>().FirstOrDefault();

                if (panel is not null)
                    panel.IsVisible = false;
            }

        }
    }
}
