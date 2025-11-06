using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Avalonia.DrmKit.Input
{
    public class EvdevMouseListener
    {
        private volatile bool _running;
        private readonly List<Thread> _threads = new();
        private double _x, _y;
        private bool _pressed;

        public void Start()
        {
            if (!OperatingSystem.IsLinux()) return;
            _running = true;

            // старт с небольшим центром по первому TopLevel
            Dispatcher.UIThread.Post(() =>
            {
                var tl = LifetimeTopLevel();
                if (tl is null) return;
                _x = tl.Bounds.Width / 2;
                _y = tl.Bounds.Height / 2;
                SoftCursorHub.UpdatePosition(new Point(_x, _y));
            });

            foreach (var dev in Directory.EnumerateFiles("/dev/input").Where(p => Path.GetFileName(p).StartsWith("event")))
            {
                var t = new Thread(() => ReadLoop(dev)) { IsBackground = true };
                _threads.Add(t);
                t.Start();
            }
        }

        public void Stop()
        {
            _running = false;
            foreach (var t in _threads) try { t.Join(200); } catch { }
            _threads.Clear();
        }

        private void ReadLoop(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buf = new byte[24];
                while (_running)
                {
                    int n = fs.Read(buf, 0, buf.Length);
                    if (n != buf.Length) { Thread.Sleep(1); continue; }
                    var e = MemoryMarshal.Read<input_event>(buf);

                    if (e.type == 2) // EV_REL
                    {
                        if (e.code == 0) _x += e.value; // REL_X
                        if (e.code == 1) _y += e.value; // REL_Y
                        ClampAndUpdate();
                    }
                    else if (e.type == 3) // EV_ABS (тач/абс. мышь) — по желанию
                    {
                        // игнор пока
                    }
                    else if (e.type == 1) // EV_KEY (кнопки)
                    {
                        if (e.code == 272 /* BTN_LEFT */)
                        {
                            if (e.value == 1) { _pressed = true; Dispatcher.UIThread.Post(PressAt); }
                            else if (e.value == 0) { _pressed = false; Dispatcher.UIThread.Post(ReleaseAt); }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DrmKit][evdev-mouse] {path} error: {ex.Message}");
                Thread.Sleep(200);
            }
        }

        private void ClampAndUpdate()
        {
            Dispatcher.UIThread.Post(() =>
            {
                var tl = LifetimeTopLevel();
                if (tl is null) return;
                _x = Math.Clamp(_x, 0, tl.Bounds.Width - 1);
                _y = Math.Clamp(_y, 0, tl.Bounds.Height - 1);
                SoftCursorHub.UpdatePosition(new Point(_x, _y));
            });
        }

        // using Avalonia.VisualTree;
        // using Avalonia.Controls.Primitives;
        // using Avalonia.DrmKit.OnScreenKeyboard;  // добавь в using

        private void PressAt()
        {
            var tl = LifetimeTopLevel();
            if (tl is null) return;

            SoftCursorHub.SetPressed(true);

            var p = new Point(_x, _y);

            // 1) Берём верхний интерактивный визуал
            var hit = tl.InputHitTest(p) as Visual;

            // Если попали в SoftCursorLayer — поднимаемся выше
            if (hit is SoftCursorLayer && hit.Parent is Visual vp)
                hit = vp;

            // 2) Пытаемся найти ближайшую кнопку (или ToggleButton) вверх по дереву
            Button? findButton(Visual? v)
            {
                for (var cur = v; cur != null; cur = cur.Parent as Visual)
                    if (cur is Button b) return b;
                return null;
            }
            ToggleButton? findToggle(Visual? v)
            {
                for (var cur = v; cur != null; cur = cur.Parent as Visual)
                    if (cur is ToggleButton t) return t;
                return null;
            }

            // Прямой клик по кнопке экранной клавиатуры — без смены фокуса!
            var btn = findButton(hit);
            if (btn is not null)
            {
                var inOsk = btn.FindAncestorOfType<Avalonia.DrmKit.OnScreenKeyboard.OnScreenKeyboard>(includeSelf: true) != null;
                if (inOsk)
                {
                    Console.WriteLine("[DrmKit][mouse] OSK Button.Click -> " + (btn.Content?.ToString() ?? btn.ToString()));
                    btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    return;
                }
            }

            var tbtn = findToggle(hit);
            if (tbtn is not null)
            {
                var inOsk = tbtn.FindAncestorOfType<Avalonia.DrmKit.OnScreenKeyboard.OnScreenKeyboard>(includeSelf: true) != null;
                if (inOsk)
                {
                    Console.WriteLine("[DrmKit][mouse] OSK ToggleButton.Toggle");
                    tbtn.IsChecked = !(tbtn.IsChecked ?? false);
                    return;
                }
            }

            // 3) Если клик пришёл в область OSK, но не в кнопку — просто удерживаем фокус на TextBox и выходим
            if (hit?.FindAncestorOfType<Avalonia.DrmKit.OnScreenKeyboard.OnScreenKeyboard>(includeSelf: true) is not null)
            {
                var focusedTb = (tl.FocusManager?.GetFocusedElement()) as TextBox;
                focusedTb?.Focus();
                return;
            }

            // 4) Обычный UI вне OSK — фокусируем и кликаем, как раньше
            var ctrl = hit as Control ?? hit?.FindAncestorOfType<Control>(includeSelf: true);
            if (ctrl is null) return;

            var focusable = ctrl;
            while (focusable is not null && !focusable.Focusable)
                focusable = focusable.FindAncestorOfType<Control>();

            (focusable ?? ctrl).Focus();

            switch (ctrl)
            {
                case ToggleButton tb:
                    tb.IsChecked = !(tb.IsChecked ?? false);
                    break;
                case Button b:
                    b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, this));
                    break;
                default:
                    break;
            }
            if (hit is not null)
            {
                var type = hit.GetType().FullName;
                var inOsk = hit.FindAncestorOfType<Avalonia.DrmKit.OnScreenKeyboard.OnScreenKeyboard>(true) != null;
                Console.WriteLine($"[DrmKit][mouse] Hit: {type}, inOSK={inOsk}");
            }
        }

        private void ReleaseAt()
        {
            SoftCursorHub.SetPressed(false);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct input_event
        {
            public long time_sec;
            public long time_usec;
            public ushort type;
            public ushort code;
            public int value;
        }

        private static TopLevel? LifetimeTopLevel()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d && d.Windows.Count > 0)
                return d.Windows[0];
            if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime s)
                return TopLevel.GetTopLevel(s.MainView);
            return null;
        }
    }
}
