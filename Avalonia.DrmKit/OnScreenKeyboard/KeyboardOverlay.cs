
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Avalonia.DrmKit.OnScreenKeyboard
{
    public enum KeyboardOcclusionMode
    {
        InsetOnly,
        ScrollOnly,
        Hybrid
    }

    public enum KeyboardInsetTarget
    {
        HostPanel,
        TopLevelContent
    }

    public enum KeyboardHeightMode
    {
        AutoFromMeasured,
        Fixed
    }

    public static class KeyboardOverlay
    {
        private static readonly AttachedProperty<bool> IsAttachedProperty =
            AvaloniaProperty.RegisterAttached<Control, Control, bool>("IsAttached_Internal");

        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<Control, Control, bool>("IsEnabled");

        public static readonly AttachedProperty<bool> AvoidOcclusionProperty =
            AvaloniaProperty.RegisterAttached<Control, Control, bool>("AvoidOcclusion", true);

        public static readonly AttachedProperty<KeyboardOcclusionMode> OcclusionModeProperty =
            AvaloniaProperty.RegisterAttached<Control, Control, KeyboardOcclusionMode>(
                "OcclusionMode",
                KeyboardOcclusionMode.Hybrid);

        public static readonly AttachedProperty<KeyboardInsetTarget> InsetTargetProperty =
            AvaloniaProperty.RegisterAttached<Control, Control, KeyboardInsetTarget>(
                "InsetTarget",
                KeyboardInsetTarget.HostPanel);

        public static readonly AttachedProperty<KeyboardHeightMode> KeyboardHeightModeProperty =
            AvaloniaProperty.RegisterAttached<Control, Control, KeyboardHeightMode>(
                "KeyboardHeightMode",
                KeyboardHeightMode.AutoFromMeasured);

        public static readonly AttachedProperty<double> KeyboardFixedHeightProperty =
            AvaloniaProperty.RegisterAttached<Control, Control, double>("KeyboardFixedHeight", 0);

        public static readonly AttachedProperty<double> ExtraSafeGapProperty =
            AvaloniaProperty.RegisterAttached<Control, Control, double>("ExtraSafeGap", 8);

        public static readonly AttachedProperty<bool> OpenOnProgrammaticFocusProperty =
            AvaloniaProperty.RegisterAttached<Control, Control, bool>("OpenOnProgrammaticFocus", false);

        public static readonly AttachedProperty<bool> OpenOnKeyboardFocusProperty =
            AvaloniaProperty.RegisterAttached<Control, Control, bool>("OpenOnKeyboardFocus", false);

        public static readonly AttachedProperty<double> InputGateTimeoutMsProperty =
            AvaloniaProperty.RegisterAttached<Control, Control, double>("InputGateTimeoutMs", 300);

        public static readonly AttachedProperty<bool> EnsureVisibleWrapInScrollViewerProperty =
            AvaloniaProperty.RegisterAttached<Control, Control, bool>("EnsureVisibleWrapInScrollViewer", true);

        public static void SetIsEnabled(Control element, bool value) => element.SetValue(IsEnabledProperty, value);
        public static bool GetIsEnabled(Control element) => element.GetValue(IsEnabledProperty);
        public static void SetAvoidOcclusion(Control element, bool value) => element.SetValue(AvoidOcclusionProperty, value);
        public static bool GetAvoidOcclusion(Control element) => element.GetValue(AvoidOcclusionProperty);
        public static void SetOcclusionMode(Control element, KeyboardOcclusionMode value) => element.SetValue(OcclusionModeProperty, value);
        public static KeyboardOcclusionMode GetOcclusionMode(Control element) => element.GetValue(OcclusionModeProperty);
        public static void SetInsetTarget(Control element, KeyboardInsetTarget value) => element.SetValue(InsetTargetProperty, value);
        public static KeyboardInsetTarget GetInsetTarget(Control element) => element.GetValue(InsetTargetProperty);
        public static void SetKeyboardHeightMode(Control element, KeyboardHeightMode value) => element.SetValue(KeyboardHeightModeProperty, value);
        public static KeyboardHeightMode GetKeyboardHeightMode(Control element) => element.GetValue(KeyboardHeightModeProperty);
        public static void SetKeyboardFixedHeight(Control element, double value) => element.SetValue(KeyboardFixedHeightProperty, value);
        public static double GetKeyboardFixedHeight(Control element) => element.GetValue(KeyboardFixedHeightProperty);
        public static void SetExtraSafeGap(Control element, double value) => element.SetValue(ExtraSafeGapProperty, value);
        public static double GetExtraSafeGap(Control element) => element.GetValue(ExtraSafeGapProperty);
        public static void SetOpenOnProgrammaticFocus(Control element, bool value) => element.SetValue(OpenOnProgrammaticFocusProperty, value);
        public static bool GetOpenOnProgrammaticFocus(Control element) => element.GetValue(OpenOnProgrammaticFocusProperty);
        public static void SetOpenOnKeyboardFocus(Control element, bool value) => element.SetValue(OpenOnKeyboardFocusProperty, value);
        public static bool GetOpenOnKeyboardFocus(Control element) => element.GetValue(OpenOnKeyboardFocusProperty);
        public static void SetInputGateTimeoutMs(Control element, double value) => element.SetValue(InputGateTimeoutMsProperty, value);
        public static double GetInputGateTimeoutMs(Control element) => element.GetValue(InputGateTimeoutMsProperty);
        public static void SetEnsureVisibleWrapInScrollViewer(Control element, bool value) => element.SetValue(EnsureVisibleWrapInScrollViewerProperty, value);
        public static bool GetEnsureVisibleWrapInScrollViewer(Control element) => element.GetValue(EnsureVisibleWrapInScrollViewerProperty);

        private const string OverlayKey = "__DrmKit_KeyboardOverlay_Panel";
        private const double DefaultKeyboardHeight = 280;
        private static readonly ConditionalWeakTable<Border, KeyboardOverlaySessionState> s_sessions = new();
        private static readonly ConditionalWeakTable<TopLevel, InputGateState> s_inputGates = new();

        static KeyboardOverlay()
        {
            IsEnabledProperty.Changed.AddClassHandler<Control>((ctrl, e) =>
            {
                if (ctrl is TextBox tb)
                {
                    if ((bool)e.NewValue! && !tb.GetValue(IsAttachedProperty))
                        Attach(tb);
                }
            });
        }

        private static void Attach(TextBox tb)
        {
            tb.SetValue(IsAttachedProperty, true);

            tb.AddHandler(
                InputElement.PointerPressedEvent,
                (_, __) =>
                {
                    var tl = TopLevel.GetTopLevel(tb);
                    if (tl is not null)
                        MarkUserInput(tl, tb);
                },
                RoutingStrategies.Tunnel);

            tb.AddHandler(
                InputElement.KeyDownEvent,
                (_, __) =>
                {
                    if (!GetOpenOnKeyboardFocus(tb))
                        return;

                    var tl = TopLevel.GetTopLevel(tb);
                    if (tl is not null)
                        MarkUserInput(tl, tb);
                },
                RoutingStrategies.Tunnel);

            tb.GotFocus += (_, __) =>
            {
                Console.WriteLine("[DrmKit] KeyboardOverlay: GotFocus on TextBox");
                ShowFor(tb);
            };

            // Касание по полю, которое УЖЕ в фокусе, а клавиатура скрыта: GotFocus второй раз не придёт,
            // и клавиатура не открывалась. Так бывает, когда окно само поставило фокус в поле (без
            // клавиатуры — программный фокус её не открывает) или клавиатуру скрыли, а фокус остался.
            tb.AddHandler(
                InputElement.PointerReleasedEvent,
                (_, __) =>
                {
                    if (!tb.IsFocused)
                        return;
                    var tl = TopLevel.GetTopLevel(tb);
                    var hostPanel = tl is null ? null : FindOrCreateOverlayHost(tl, tb);
                    if (tl is null || hostPanel is null)
                        return;
                    if (EnsurePanel(hostPanel, tl).IsVisible)
                        return;
                    Console.WriteLine("[DrmKit] KeyboardOverlay: tap on focused TextBox");
                    MarkUserInput(tl, tb);
                    ShowFor(tb);
                },
                RoutingStrategies.Tunnel,
                handledEventsToo: true);

            tb.DetachedFromVisualTree += (_, __) =>
            {
                var tl = TopLevel.GetTopLevel(tb);
                var hostPanel = tl is null ? null : FindOrCreateOverlayHost(tl, tb);
                if (hostPanel is null) return;
                var kbPanel = hostPanel.Resources[OverlayKey] as Border;
                if (kbPanel is null) return;
                MaybeHideIfNoFocusedTextBox(tl!, kbPanel);
            };
        }

        /// <summary>Show the keyboard for a text box (focus gained, or a tap on an already focused one).</summary>
        private static void ShowFor(TextBox tb)
        {
            var tl = TopLevel.GetTopLevel(tb);
            if (tl is null) return;
            var hostPanel = FindOrCreateOverlayHost(tl, tb);
            if (hostPanel is null)
            {
                Console.WriteLine("[DrmKit] KeyboardOverlay: no host Panel");
                return;
            }

            var kbPanel = EnsurePanel(hostPanel, tl);
            var allowProgrammatic = GetOpenOnProgrammaticFocus(tb);
            var allowKeyboardFocus = GetOpenOnKeyboardFocus(tb);
            var userInitiated = IsUserInputRecent(tl);

            if (!allowProgrammatic && !allowKeyboardFocus && !userInitiated)
            {
                var session = GetSession(kbPanel);
                session.CurrentTarget = tb;
                return;
            }

            var keyboard = kbPanel.Tag as OnScreenKeyboard;
            keyboard?.Attach(tb);
            kbPanel.IsVisible = true;
            var sessionOnShow = GetSession(kbPanel);
            sessionOnShow.CurrentTarget = tb;
            ConsumeUserInput(tl);
            RefreshOcclusion(tb, tl, hostPanel, kbPanel);
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (kbPanel.IsVisible)
                        RefreshOcclusion(tb, tl, hostPanel, kbPanel);
                },
                DispatcherPriority.Background);
            Console.WriteLine("[DrmKit] KeyboardOverlay: show");
        }

        private static Panel? FindOrCreateOverlayHost(TopLevel tl, TextBox? sourceTextBox)
        {
            _ = sourceTextBox;

            var existingHost = tl.GetVisualDescendants()
                                 .OfType<Panel>()
                                 .FirstOrDefault(p => p.Resources.ContainsKey(OverlayKey));
            if (existingHost is not null)
                return existingHost;

            if (tl is ContentControl cc)
            {
                if (cc.Content is Panel panelContent)
                    return panelContent;

                if (cc.Content is Decorator decorator && decorator.Child is Panel panelChild)
                    return panelChild;
            }

            return tl.GetVisualDescendants().OfType<Panel>().FirstOrDefault();
        }

        private static Border EnsurePanel(Panel hostPanel, TopLevel tl)
        {
            if (hostPanel.Resources.TryGetValue(OverlayKey, out var existing) && existing is Border b)
                return b;

            var keyboard = new OnScreenKeyboard();
            var panel = new Border
            {
                // Прозрачный, но не пустой фон: поля вокруг клавиатуры тоже ловят касание, а не
                // пропускают его на экран под ней (иначе нажатие мимо клавиши закрывало клавиатуру).
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsVisible = false,
                Child = keyboard,
                Focusable = false,           // фокус не забираем
                IsHitTestVisible = true,     // ← клики по детям (кнопкам) проходят
            };
            hostPanel.Children.Add(panel);
            if (hostPanel is Grid)
            {
                var grid = (Grid)hostPanel;
                Grid.SetRowSpan(panel, Math.Max(1, grid.RowDefinitions.Count));
                Grid.SetColumnSpan(panel, Math.Max(1, grid.ColumnDefinitions.Count));
            }
            panel.SetValue(Visual.ZIndexProperty, int.MaxValue);
            hostPanel.Resources[OverlayKey] = panel;
            panel.Tag = keyboard;
            keyboard.CloseRequested += (_, __) => panel.IsVisible = false;
            s_sessions.Add(panel, new KeyboardOverlaySessionState());

            panel.PropertyChanged += (_, e) =>
            {
                if (e.Property == Visual.IsVisibleProperty && panel.IsVisible == false)
                    ResetOcclusion(panel);
            };

            panel.SizeChanged += (_, __) =>
            {
                if (!panel.IsVisible) return;
                var session = GetSession(panel);
                if (session.CurrentTarget is null) return;
                RefreshOcclusion(session.CurrentTarget, tl, hostPanel, panel);
            };

            tl.PropertyChanged += (_, e) =>
            {
                if (e.Property == TopLevel.BoundsProperty && panel.IsVisible)
                {
                    var session = GetSession(panel);
                    if (session.CurrentTarget is not null)
                        RefreshOcclusion(session.CurrentTarget, tl, hostPanel, panel);
                }
            };

            tl.AddHandler(InputElement.LostFocusEvent, (_, __) => MaybeHideIfNoFocusedTextBox(tl, panel), RoutingStrategies.Tunnel);
            // Поле, к которому открыта клавиатура, скрыли (IsVisible = false у него или у контейнера):
            // фокус при этом снимается БЕЗ LostFocus, и клавиатура оставалась висеть на пол-экрана
            // поверх формы, где вводить уже нечего (пример: «Отменить» правку профиля). Скрытие
            // контейнера всегда вызывает проход раскладки, поэтому проверяем после него.
            tl.LayoutUpdated += (_, __) =>
            {
                if (!panel.IsVisible)
                    return;
                var target = GetSession(panel).CurrentTarget;
                // Поле ушло из дерева — экран сменили (переход по «хлебным крошкам» и т. п.): у
                // отцепленного элемента IsEffectivelyVisible остаётся true, и клавиатура висела на
                // пол-экрана над новым экраном, где поля уже нет.
                if (target is not null && (!target.IsEffectivelyVisible || TopLevel.GetTopLevel(target) is null))
                {
                    panel.IsVisible = false;
                    Console.WriteLine("[DrmKit] KeyboardOverlay: auto-hide (target TextBox hidden or detached)");
                }
            };
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

        private static KeyboardOverlaySessionState GetSession(Border panel)
            => s_sessions.TryGetValue(panel, out var session) ? session : new KeyboardOverlaySessionState();

        private static void MarkUserInput(TopLevel tl, Control? source)
        {
            var gate = GetOrCreateInputGate(tl);
            var timeoutMs = source is null ? 300 : Math.Clamp(GetInputGateTimeoutMs(source), 50, 3000);
            gate.PendingUserInputUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(timeoutMs);
        }

        private static bool IsUserInputRecent(TopLevel tl)
        {
            var gate = GetOrCreateInputGate(tl);
            return gate.PendingUserInputUntilUtc >= DateTimeOffset.UtcNow;
        }

        private static void ConsumeUserInput(TopLevel tl)
        {
            var gate = GetOrCreateInputGate(tl);
            gate.PendingUserInputUntilUtc = DateTimeOffset.MinValue;
        }

        private static InputGateState GetOrCreateInputGate(TopLevel tl)
            => s_inputGates.TryGetValue(tl, out var gate) ? gate : s_inputGates.GetValue(tl, _ => new InputGateState());

        private static void RefreshOcclusion(TextBox tb, TopLevel tl, Panel hostPanel, Border kbPanel)
        {
            if (!GetAvoidOcclusion(tb))
                return;

            var mode = GetOcclusionMode(tb);
            var gap = Math.Max(0, GetExtraSafeGap(tb));
            var keyboardHeight = ResolveKeyboardHeight(tb, kbPanel);
            var session = GetSession(kbPanel);
            session.HostPanel = hostPanel;
            session.CurrentKeyboardHeight = keyboardHeight;

            if (mode is KeyboardOcclusionMode.InsetOnly or KeyboardOcclusionMode.Hybrid)
                ApplyInset(tb, hostPanel, kbPanel, keyboardHeight, gap);
            else
                RestoreInset(kbPanel);

            if (mode is KeyboardOcclusionMode.ScrollOnly or KeyboardOcclusionMode.Hybrid)
                EnsureEditorVisible(tb, tl, hostPanel, kbPanel, keyboardHeight, gap);
        }

        private static double ResolveKeyboardHeight(Control source, Border panel)
        {
            if (GetKeyboardHeightMode(source) == KeyboardHeightMode.Fixed)
            {
                var fixedHeight = GetKeyboardFixedHeight(source);
                if (fixedHeight > 0)
                    return fixedHeight;
            }

            if (panel.Bounds.Height > 0)
                return panel.Bounds.Height;

            if (panel.DesiredSize.Height > 0)
                return panel.DesiredSize.Height;

            return DefaultKeyboardHeight;
        }

        private static void ApplyInset(TextBox tb, Panel hostPanel, Border kbPanel, double keyboardHeight, double gap)
        {
            var session = GetSession(kbPanel);
            if (GetInsetTarget(tb) == KeyboardInsetTarget.TopLevelContent)
            {
                // Kept for API compatibility. In current implementation inset is driven by host spacer.
            }

            // Safe fallback: do not mutate Window.Content runtime.
            // Inset is applied only when host provided an explicit spacer.
            _ = hostPanel;
            _ = keyboardHeight;
            _ = gap;
            session.Spacer = null;
            session.InsetApplied = false;
        }

        private static void RestoreInset(Border kbPanel)
        {
            var session = GetSession(kbPanel);
            if (!session.InsetApplied)
                return;

            if (session.Spacer is not null)
                session.Spacer.Height = 0;

            session.Spacer = null;
            session.InsetApplied = false;
        }

        private static void ResetOcclusion(Border kbPanel)
        {
            RestoreInset(kbPanel);
            var session = GetSession(kbPanel);
            RestoreCompensation(session);
            ClearCompensationSnapshot(session);
            session.CurrentTarget = null;
            session.CurrentKeyboardHeight = 0;

            if (kbPanel.Tag is OnScreenKeyboard keyboard)
                keyboard.OnOverlayHidden();
        }

        private static void EnsureEditorVisible(TextBox tb, TopLevel tl, Panel hostPanel, Border kbPanel, double keyboardHeight, double gap)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!kbPanel.IsVisible)
                    return;

                var session = GetSession(kbPanel);
                RestoreCompensation(session);

                var bottomPoint = tb.TranslatePoint(new Point(0, tb.Bounds.Height), tl);
                if (bottomPoint is null)
                    return;

                var keyboardTop = Math.Max(0, tl.Bounds.Height - keyboardHeight);
                var threshold = keyboardTop - gap;
                if (bottomPoint.Value.Y <= threshold)
                    return;

                var overflow = bottomPoint.Value.Y - threshold;
                var scrollViewer = FindNearestScrollViewer(tb);
                if (scrollViewer is not null)
                {
                    CaptureScrollSnapshot(session, scrollViewer);

                    var maxOffsetY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
                    var availableDown = Math.Max(0, maxOffsetY - scrollViewer.Offset.Y);
                    var scrollDelta = Math.Min(overflow, availableDown);
                    if (scrollDelta > 0)
                    {
                        var newOffsetY = scrollViewer.Offset.Y + scrollDelta;
                        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, newOffsetY);
                    }
                }

                var remainingBottom = tb.TranslatePoint(new Point(0, tb.Bounds.Height), tl);
                if (remainingBottom is null)
                    return;

                var remaining = remainingBottom.Value.Y - threshold;
                if (remaining <= 0)
                    return;

                var panRoot = FindPanRoot(tb, hostPanel, kbPanel);
                if (panRoot is not null)
                {
                    CapturePanSnapshot(session, panRoot);
                    ApplyPan(session, -remaining);
                }
            }, DispatcherPriority.Render);
        }

        private static ScrollViewer? FindNearestScrollViewer(Visual visual)
            => visual.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();

        private static Control? FindPanRoot(TextBox tb, Panel hostPanel, Border kbPanel)
        {
            foreach (var child in hostPanel.Children.OfType<Control>())
            {
                if (ReferenceEquals(child, kbPanel))
                    continue;

                if (ReferenceEquals(tb, child) || IsDescendantOf(tb, child))
                    return child;
            }

            return null;
        }

        private static bool IsDescendantOf(Visual visual, Visual ancestor)
        {
            var current = visual;
            while (current is not null)
            {
                if (ReferenceEquals(current, ancestor))
                    return true;

                current = current.GetVisualParent();
            }

            return false;
        }

        private static void CaptureScrollSnapshot(KeyboardOverlaySessionState session, ScrollViewer scrollViewer)
        {
            if (session.HasScrollSnapshot)
                return;

            session.ScrollViewer = scrollViewer;
            session.ScrollOriginalOffset = scrollViewer.Offset;
            session.HasScrollSnapshot = true;
        }

        private static void CapturePanSnapshot(KeyboardOverlaySessionState session, Control panRoot)
        {
            if (!session.HasTransformSnapshot)
            {
                session.PanRoot = panRoot;
                session.PanRootOriginalTransform = panRoot.RenderTransform;
                session.HasTransformSnapshot = true;
            }
        }

        private static void ApplyPan(KeyboardOverlaySessionState session, double translateY)
        {
            if (session.PanRoot is null)
                return;

            session.PanAppliedY = translateY;
            session.PanRoot.RenderTransform = ComposeWithTranslation(session.PanRootOriginalTransform, translateY);
        }

        private static ITransform ComposeWithTranslation(ITransform? baseTransform, double translateY)
        {
            if (Math.Abs(translateY) < 0.5)
                return baseTransform ?? new TranslateTransform();

            if (baseTransform is null)
                return new TranslateTransform(0, translateY);

            var matrix = baseTransform.Value;
            var translated = matrix * Matrix.CreateTranslation(0, translateY);
            return new MatrixTransform(translated);
        }

        private static void RestoreCompensation(KeyboardOverlaySessionState session)
        {
            if (session.HasScrollSnapshot && session.ScrollViewer is not null)
                session.ScrollViewer.Offset = session.ScrollOriginalOffset;

            if (session.HasTransformSnapshot && session.PanRoot is not null)
                session.PanRoot.RenderTransform = session.PanRootOriginalTransform;

            session.PanAppliedY = 0;
        }

        private static void ClearCompensationSnapshot(KeyboardOverlaySessionState session)
        {
            session.ScrollViewer = null;
            session.ScrollOriginalOffset = default;
            session.HasScrollSnapshot = false;

            session.PanRoot = null;
            session.PanRootOriginalTransform = null;
            session.PanAppliedY = 0;
            session.HasTransformSnapshot = false;
        }

        private sealed class KeyboardOverlaySessionState
        {
            public TextBox? CurrentTarget { get; set; }
            public bool InsetApplied { get; set; }
            public Panel? HostPanel { get; set; }
            public Border? Spacer { get; set; }
            public double CurrentKeyboardHeight { get; set; }
            public ScrollViewer? ScrollViewer { get; set; }
            public Vector ScrollOriginalOffset { get; set; }
            public bool HasScrollSnapshot { get; set; }
            public Control? PanRoot { get; set; }
            public ITransform? PanRootOriginalTransform { get; set; }
            public double PanAppliedY { get; set; }
            public bool HasTransformSnapshot { get; set; }
        }

        private sealed class InputGateState
        {
            public DateTimeOffset PendingUserInputUntilUtc { get; set; }
        }
    }
}
