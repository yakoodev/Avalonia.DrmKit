
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Avalonia.DrmKit.OnScreenKeyboard
{
    public static class KeyboardPopup
    {
        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<Control, Control, bool>("IsEnabled");

        public static void SetIsEnabled(Control element, bool value) => element.SetValue(IsEnabledProperty, value);
        public static bool GetIsEnabled(Control element) => element.GetValue(IsEnabledProperty);

        static KeyboardPopup()
        {
            IsEnabledProperty.Changed.AddClassHandler<Control>((ctrl, e) =>
            {
                if (ctrl is TextBox tb)
                {
                    if ((bool)e.NewValue!)
                        Attach(tb);
                }
            });
        }

        private static void Attach(TextBox tb)
        {
            var popup = new Popup
            {
                PlacementTarget = tb,
                PlacementMode = PlacementMode.Bottom,
                IsLightDismissEnabled = false,
                Topmost = true
            };

            var keyboard = new OnScreenKeyboard();
            keyboard.Attach(tb);
            popup.Child = keyboard;

            // Enter/Cancel на экранной клавиатуре закрывают popup
            keyboard.CloseRequested += (_, __) => popup.IsOpen = false;

            // Открываем по фокусу
            tb.AddHandler(InputElement.GotFocusEvent, (_, __) => popup.IsOpen = true);

            // Закрываем popup по Escape на физической клавиатуре
            tb.AddHandler(
                InputElement.KeyDownEvent,
                (sender, e) =>
                {
                    if (e.Key == Key.Escape && popup.IsOpen)
                    {
                        popup.IsOpen = false;
                        e.Handled = true;
                    }
                },
                RoutingStrategies.Tunnel);
        }

    }
}
