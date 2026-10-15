
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using System;
using System.Linq;

namespace Avalonia.DrmKit
{
    public static class AppBuilderExtensions
    {
        public static AppBuilder UseDrmSoftCursor(this AppBuilder builder, SoftCursorOptions? options = null)
        {
            options ??= new SoftCursorOptions();

            builder.AfterSetup(_ =>
            {
                Console.WriteLine("[DrmKit] UseDrmSoftCursor enabled");

                // Начальное состояние: опция, переопределяемая DRMKIT_SOFT_CURSOR (0/false — выключен).
                // Слой курсора всё равно ставится — включить его потом можно через DrmSoftCursor.IsEnabled.
                var initiallyEnabled = DrmSoftCursor.ResolveInitial(
                    options.Enabled, Environment.GetEnvironmentVariable("DRMKIT_SOFT_CURSOR"));
                Input.SoftCursorHub.SetEnabled(initiallyEnabled);
                if (!initiallyEnabled)
                    Console.WriteLine("[DrmKit] Soft cursor is disabled (SoftCursorOptions.Enabled / DRMKIT_SOFT_CURSOR)");

                if (!OperatingSystem.IsLinux())
                    return;

                var sys = SmartPlatform.DetectSystemType(Array.Empty<string>());
                if (!sys.IsDrm())
                    return;

                var app = Application.Current;
                const string FlagKey = "__DrmKit_SoftCursorAttached";

                void AttachToTopLevel(TopLevel tl)
                {
                    if (tl is null) return;
                    if (tl.Resources.ContainsKey(FlagKey)) return;
                    tl.Resources[FlagKey] = true;
                    Console.WriteLine($"[DrmKit] Attaching soft cursor to TopLevel: {tl}");
                    SoftCursorInstaller.TryAttach(tl, options!);
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (app?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        foreach (var w in desktop.Windows)
                            AttachToTopLevel(w);

                        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                        timer.Tick += (_, __) =>
                        {
                            foreach (var w in desktop.Windows)
                                AttachToTopLevel(w);
                        };
                        timer.Start();
                    }
                    else if (app?.ApplicationLifetime is ISingleViewApplicationLifetime single)
                    {
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                        timer.Tick += (_, __) =>
                        {
                            var view = single.MainView;
                            if (view is null) return;

                            var tl = TopLevel.GetTopLevel(view);
                            if (tl is null) return;

                            AttachToTopLevel(tl);
                            timer.Stop();
                        };
                        timer.Start();
                    }
                });
            });

            return builder;
        }

        [Obsolete("Use DrmUnifiedInputBackend (IInputBackend) through SmartPlatformOptions.InputBackend. This API is legacy and may emit duplicate input events.")]
        public static AppBuilder UseLinuxKeyboardListener(this AppBuilder builder, Func<Input.KeyboardListener>? factory = null)
        {
            builder.AfterSetup(_ =>
            {
                Console.WriteLine("[DrmKit] UseLinuxKeyboardListener enabled");

                if (!OperatingSystem.IsLinux()) return;
                if (RuntimeInputState.UnifiedBackendEnabled)
                {
                    Console.WriteLine("[DrmKit] UseLinuxKeyboardListener skipped: unified input backend is active.");
                    return;
                }

                var sys = SmartPlatform.DetectSystemType(Array.Empty<string>());
                var isDrm = sys.IsDrm();

                var listener = factory?.Invoke() ?? new Input.KeyboardListener();
                AppDomain.CurrentDomain.ProcessExit += (_, __) => listener.StopListening();
                listener.StartListening();

                var app = Application.Current;
                if (app?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    listener.KeyEvent += (_, e) =>
                    {
                        var active = desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.Windows.FirstOrDefault();
                        var focused = active?.FocusManager?.GetFocusedElement();
                        Dispatcher.UIThread.Post(() => (focused as IInputElement)?.RaiseEvent(e));
                    };

                    if (isDrm)
                    {
                        listener.TextInput += (_, text) =>
                        {
                            var active = desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.Windows.FirstOrDefault();
                            var focused = active?.FocusManager?.GetFocusedElement();
                            if (focused is IInputElement ie)
                            {
                                Dispatcher.UIThread.Post(() =>
                                    ie.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text }));
                            }
                        };
                    }
                }
                else if (app?.ApplicationLifetime is ISingleViewApplicationLifetime single)
                {
                    listener.KeyEvent += (_, e) =>
                    {
                        var tl = TopLevel.GetTopLevel(single.MainView);
                        var focused = tl?.FocusManager?.GetFocusedElement();
                        Dispatcher.UIThread.Post(() => (focused as IInputElement)?.RaiseEvent(e));
                    };

                    if (isDrm)
                    {
                        listener.TextInput += (_, text) =>
                        {
                            var tl = TopLevel.GetTopLevel(single.MainView);
                            var focused = tl?.FocusManager?.GetFocusedElement();
                            if (focused is IInputElement ie)
                            {
                                Dispatcher.UIThread.Post(() =>
                                    ie.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text }));
                            }
                        };
                    }
                }
            });
            return builder;
        }

        [Obsolete("Use DrmUnifiedInputBackend (IInputBackend) through SmartPlatformOptions.InputBackend. This API is legacy and may emit duplicate input events.")]
        public static AppBuilder UseEvdevMouseFallback(this AppBuilder builder)
        {
            builder.AfterSetup(_ =>
            {
                if (!OperatingSystem.IsLinux()) return;
                var sys = SmartPlatform.DetectSystemType(Array.Empty<string>());
                if (!sys.IsDrm()) return;
                if (RuntimeInputState.UnifiedBackendEnabled)
                {
                    Console.WriteLine("[DrmKit] UseEvdevMouseFallback skipped: unified input backend is active.");
                    return;
                }

                Console.WriteLine("[DrmKit] UseEvdevMouseFallback enabled");
                var mouse = new Input.EvdevMouseListener();
                AppDomain.CurrentDomain.ProcessExit += (_, __) => mouse.Stop();
                mouse.Start();
            });

            return builder;
        }
    }

    public class SoftCursorOptions
    {
        public double Size { get; set; } = 18;
        public double StrokeThickness { get; set; } = 2;
        public bool ClickRipple { get; set; } = true;
        public bool HideOnTouch { get; set; } = false;

        /// <summary>
        /// Показывать ли курсор с самого старта (по умолчанию да). На сенсорном киоске курсор
        /// можно выключить; менять на ходу — <see cref="DrmSoftCursor.IsEnabled"/>.
        /// Переменная окружения DRMKIT_SOFT_CURSOR (0/false/1/true) имеет приоритет.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}
