using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.DrmKit.Input;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
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
        private string _originalText = string.Empty;



        public OnScreenKeyboard()
        {
            InitializeComponent();

            _caps = KeyboardLayoutManager.Caps;

            KeyboardLayoutManager.CurrentChanged += (_, __) => Rebuild();
            KeyboardLayoutManager.CapsChanged += (_, on) => { _caps = on; Rebuild(); };

            Rebuild();
        }

        public void Attach(TextBox textBox)
        {
            _target = textBox;
            _originalText = textBox.Text ?? string.Empty;
        }

        private void Rebuild()
        {
            var layout = KeyboardLayoutManager.GetCurrent();
            var rows = layout.Rows;

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
                grid.Children.Clear();
                foreach (var key in rows[r])
                    grid.Children.Add(CreateKeyButton(key));
            }
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            // Любое касание по клавиатуре — держим фокус в TextBox
            _target?.Focus();
        }
        private Button CreateKeyButton(string text)
        {
            var label = _caps ? text.ToUpperInvariant() : text;
            var btn = new Button
            {
                Content = label,
                MinHeight = 42,
                IsTabStop = false,
                Focusable = true,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
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
                    _originalText = _target.Text ?? string.Empty;
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

                case "enter":
                    _target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Enter });
                    var tl = TopLevel.GetTopLevel(this);
                    tl?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsDefault)
                      ?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    _target.Focus();
                    return;

                case "cancel":
                    _target!.Text = _originalText;
                    _target.SelectionStart = _target.SelectionEnd = _target.Text?.Length ?? 0;

                    // Скрываем оверлей
                    HideOverlay();

                    // Чётко снимаем фокус со всего
                    var tl2 = TopLevel.GetTopLevel(this);
                    tl2?.FocusManager?.ClearFocus();

                    // На всякий случай "пинок" контролу, чтобы последующий tap точно дал GotFocus
                    var tb = _target;
                    _target = null;
                    tb.IsEnabled = false;
                    Dispatcher.UIThread.Post(() => tb.IsEnabled = true);
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

            void SendEnter()
            {
                // 1) шлём Key.Enter в фокус
                _target!.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Enter
                });

                // 2) если есть Button с IsDefault="True" — жмём его
                var tl = TopLevel.GetTopLevel(this);
                var defaultBtn = tl?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsDefault);
                defaultBtn?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            void HideOverlay()
            {
                // Найдём нашу оверлей-панель (Border), в которой живёт клавиатура, и спрячем её
                var panel = this.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => ReferenceEquals(b.Tag, this));
                if (panel is null)
                    panel = this.GetVisualAncestors().OfType<Border>().FirstOrDefault(); // запасной вариант
                if (panel is not null) panel.IsVisible = false;
            }
        }
    }
}
