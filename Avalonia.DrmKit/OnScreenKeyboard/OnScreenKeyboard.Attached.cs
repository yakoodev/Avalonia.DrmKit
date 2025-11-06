
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

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
                //StaysOpen = true,
                IsLightDismissEnabled = false,
                Topmost = true
            };
            var keyboard = new OnScreenKeyboard();
            keyboard.Attach(tb);
            popup.Child = keyboard;
            tb.AddHandler(InputElement.GotFocusEvent, (_, __) => popup.IsOpen = true);
        }
    }
}
