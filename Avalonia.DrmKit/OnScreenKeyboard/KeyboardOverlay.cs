
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Linq;

namespace Avalonia.DrmKit.OnScreenKeyboard
{
    public static class KeyboardOverlay
    {
        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<Control, Control, bool>("IsEnabled");

        public static void SetIsEnabled(Control element, bool value) => element.SetValue(IsEnabledProperty, value);
        public static bool GetIsEnabled(Control element) => element.GetValue(IsEnabledProperty);

        private const string OverlayKey = "__DrmKit_KeyboardOverlay_Panel";

        static KeyboardOverlay()
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
            tb.GotFocus += (_, __) =>
            {
                var tl = TopLevel.GetTopLevel(tb);
                if (tl is null) return;
                Console.WriteLine("[DrmKit] KeyboardOverlay: GotFocus on TextBox");
                var hostPanel = tl.GetVisualDescendants().OfType<Panel>().FirstOrDefault();
                if (hostPanel is null)
                {
                    Console.WriteLine("[DrmKit] KeyboardOverlay: no host Panel");
                    return;
                }

                var kbPanel = EnsurePanel(hostPanel, tl);
                var keyboard = kbPanel.Tag as OnScreenKeyboard;
                keyboard?.Attach(tb);
                kbPanel.IsVisible = true;
                Console.WriteLine("[DrmKit] KeyboardOverlay: show");
            };

            tb.DetachedFromVisualTree += (_, __) =>
            {
                var tl = TopLevel.GetTopLevel(tb);
                var hostPanel = tl?.GetVisualDescendants().OfType<Panel>().FirstOrDefault();
                if (hostPanel is null) return;
                var kbPanel = hostPanel.Resources[OverlayKey] as Border;
                if (kbPanel is null) return;
                MaybeHideIfNoFocusedTextBox(tl!, kbPanel);
            };
        }

        private static Border EnsurePanel(Panel hostPanel, TopLevel tl)
        {
            if (hostPanel.Resources.TryGetValue(OverlayKey, out var existing) && existing is Border b)
                return b;

            var keyboard = new OnScreenKeyboard();
            var panel = new Border
            {
                
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsVisible = false,
                Child = keyboard,
                Focusable = false,           // фокус не забираем
                IsHitTestVisible = true,     // ← клики по детям (кнопкам) проходят
            };
            //Panel.SetZIndex(panel, 9000);

            hostPanel.Children.Add(panel);
            hostPanel.Resources[OverlayKey] = panel;
            panel.Tag = keyboard;
            keyboard.CloseRequested += (_, __) => panel.IsVisible = false;

            tl.AddHandler(InputElement.LostFocusEvent, (_, __) => MaybeHideIfNoFocusedTextBox(tl, panel), RoutingStrategies.Tunnel);
            tl.AddHandler(
                        InputElement.KeyDownEvent,
                        (sender, e) =>
                        {
                            if (e.Key == Key.Escape && panel.IsVisible)
                            {
                                panel.IsVisible = false;
                                e.Handled = true;
                            }
                        },
                        RoutingStrategies.Tunnel);

            Console.WriteLine("[DrmKit] KeyboardOverlay: panel created");
            return panel;
        }

        private static void MaybeHideIfNoFocusedTextBox(TopLevel tl, Border kbPanel)
        {
            var focused = tl.FocusManager?.GetFocusedElement();

            // Если фокус на TextBox — оставляем
            if (focused is TextBox) return;

            // Если фокус внутри панели (кнопка экранной клавиатуры) — тоже оставляем
            //if (focused is Visual v && v.IsVisualDescendantOf(kbPanel))
            //    return;

            // Иначе прячем
            kbPanel.IsVisible = false;
            Console.WriteLine("[DrmKit] KeyboardOverlay: auto-hide (focus outside TextBox and keyboard)");
        }
    }
}
