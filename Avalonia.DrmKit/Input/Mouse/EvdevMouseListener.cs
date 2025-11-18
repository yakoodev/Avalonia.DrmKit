using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.DrmKit.Input;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReactiveUI;
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

        // Один виртуальный указатель для всей «мыши»
        private readonly Pointer _pointer =
            new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);

        // Элемент, над которым сейчас висит указатель (для PointerEnter/Leave)
        private IInputElement? _currentOver;

        public void Start()
        {
            if (!OperatingSystem.IsLinux())
                return;

            _running = true;

            // Стартуем примерно из центра первого TopLevel
            Dispatcher.UIThread.Post(() =>
            {
                var tl = LifetimeTopLevel();
                if (tl is null)
                    return;

                _x = tl.Bounds.Width / 2;
                _y = tl.Bounds.Height / 2;
                SoftCursorHub.UpdatePosition(new Point(_x, _y));
            });

            foreach (var dev in Directory.EnumerateFiles("/dev/input")
                     .Where(p => Path.GetFileName(p).StartsWith("event", StringComparison.Ordinal)))
            {
                var t = new Thread(() => ReadLoop(dev))
                {
                    IsBackground = true
                };
                _threads.Add(t);
                t.Start();
            }
        }

        public void Stop()
        {
            _running = false;
            foreach (var t in _threads)
            {
                try { t.Join(200); }
                catch { }
            }
            _threads.Clear();
        }

        private void ReadLoop(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buf = new byte[Marshal.SizeOf<input_event>()];

                while (_running)
                {
                    int n = fs.Read(buf, 0, buf.Length);
                    if (n != buf.Length)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    var e = MemoryMarshal.Read<input_event>(buf);

                    // EV_REL – относительное движение
                    if (e.type == 2 /* EV_REL */)
                    {
                        if (e.code == 0) // REL_X
                            _x += e.value;
                        else if (e.code == 1) // REL_Y
                            _y += e.value;

                        ClampAndUpdate();
                    }
                    // EV_ABS – сейчас игнорируем (тач/абс. мышь)
                    else if (e.type == 3 /* EV_ABS */)
                    {
                        // по желанию можно добавить поддержку тача
                    }
                    // EV_KEY – кнопки мыши
                    else if (e.type == 1 /* EV_KEY */)
                    {
                        if (e.code == 272 /* BTN_LEFT */)
                        {
                            if (e.value == 1)
                            {
                                _pressed = true;
                                Dispatcher.UIThread.Post(PressAt);
                            }
                            else if (e.value == 0)
                            {
                                _pressed = false;
                                Dispatcher.UIThread.Post(ReleaseAt);
                            }
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

        /// <summary>
        /// Клэмп координат, обновление soft-курсора и генерация PointerMoved/Enter/Leave.
        /// </summary>
        private void ClampAndUpdate()
        {
            Dispatcher.UIThread.Post(() =>
            {
                var tl = LifetimeTopLevel();
                if (tl is null)
                    return;

                _x = Math.Clamp(_x, 0, tl.Bounds.Width - 1);
                _y = Math.Clamp(_y, 0, tl.Bounds.Height - 1);

                var p = new Point(_x, _y);

                // двигаем прорисованный курсор
                SoftCursorHub.UpdatePosition(p);

                // и обновляем PointerOver / PointerMoved / PointerEnter / PointerLeave
                ProcessPointerMove(tl, p);
            });
        }
        private List<IInputElement>? _overChain;
        private static readonly List<IInputElement> s_emptyInputChain = new();

        private static List<IInputElement> BuildInputChain(Visual? hit, IInputRoot root)
        {
            if (hit is null)
                return s_emptyInputChain;

            var chain = new List<IInputElement>();

            var v = hit;
            while (v != null && v != root)
            {
                if (v is IInputElement ie)
                    chain.Add(ie);

                v = v.GetVisualParent(); // extension из Avalonia.VisualTree
            }

            // Обычно TopLevel тоже IInputElement, можно его добавить в цепочку
            if (root is IInputElement rootIe)
                chain.Add(rootIe);

            // Сейчас chain = [leaf, ..., root], разворачиваем чтобы было root -> leaf
            chain.Reverse();
            return chain;
        }

        /// <summary>
        /// Хит-тест, PointerEnter/Leave и PointerMoved.
        /// </summary>
        private void ProcessPointerMove(TopLevel tl, Point p)
        {
            var root = (IInputRoot)tl;

            // кого реально попали
            var hitVisual = tl.InputHitTest(p) as Visual;

            // если попали в слой курсора – поднимаемся к родителю
            if (hitVisual is SoftCursorLayer && hitVisual.Parent is Visual parent)
                hitVisual = parent;

            // строим цепочки "root -> ... -> leaf"
            var newChain = BuildInputChain(hitVisual, root);
            var oldChain = _overChain ?? s_emptyInputChain;

            // ищем общую часть в начале (root -> …)
            var common = 0;
            while (common < oldChain.Count &&
                   common < newChain.Count &&
                   ReferenceEquals(oldChain[common], newChain[common]))
            {
                common++;
            }

            // --- PointerExited для тех, кто выпал из цепочки ---
            for (int i = oldChain.Count - 1; i >= common; i--)
            {
                if (oldChain[i] is Interactive interactive)
                {
                    var args = CreatePointerEventArgs(
                        InputElement.PointerExitedEvent,
                        interactive,
                        tl,
                        p,
                        PointerUpdateKind.Other);

                    interactive.RaiseEvent(args);
                }
            }

            // --- PointerEntered для новых элементов в цепочке ---
            for (int i = common; i < newChain.Count; i++)
            {
                if (newChain[i] is Interactive interactive)
                {
                    var args = CreatePointerEventArgs(
                        InputElement.PointerEnteredEvent,
                        interactive,
                        tl,
                        p,
                        PointerUpdateKind.Other);

                    interactive.RaiseEvent(args);
                }
            }

            _overChain = newChain;

            // последний в цепочке = реальный leaf под курсором
            var newLeaf = newChain.Count > 0 ? newChain[^1] : null;

            // обновляем PointerOverElement, чтобы всё было консистентно
            if (!ReferenceEquals(root.PointerOverElement, newLeaf))
                root.PointerOverElement = newLeaf;

            _currentOver = newLeaf;

            // обычный PointerMoved для leaf'а
            if (newLeaf is Interactive target)
            {
                var moveArgs = CreatePointerEventArgs(
                    InputElement.PointerMovedEvent,
                    target,
                    tl,
                    p,
                    PointerUpdateKind.Other);

                target.RaiseEvent(moveArgs);
            }
        }


        private void PressAt()
        {
            var tl = LifetimeTopLevel();
            if (tl is null)
                return;

            SoftCursorHub.SetPressed(true);

            var p = new Point(_x, _y);

            // Обновим состояние Over на всякий случай
            ProcessPointerMove(tl, p);

            var target = (_currentOver as Interactive) ?? (Interactive)tl;

            var props = new PointerPointProperties(
                RawInputModifiers.LeftMouseButton,
                PointerUpdateKind.LeftButtonPressed);

            var args = new PointerPressedEventArgs(
                target,
                _pointer,
                tl,
                p,
                (ulong)Environment.TickCount64,
                props,
                KeyModifiers.None)
            {
                RoutedEvent = InputElement.PointerPressedEvent
            };

            target.RaiseEvent(args);
        }

        private void ReleaseAt()
        {
            var tl = LifetimeTopLevel();
            if (tl is null)
                return;

            SoftCursorHub.SetPressed(false);

            var p = new Point(_x, _y);

            // снова синхронизируем Over
            ProcessPointerMove(tl, p);

            var target = (_currentOver as Interactive) ?? (Interactive)tl;

            var props = new PointerPointProperties(
                RawInputModifiers.None,
                PointerUpdateKind.LeftButtonReleased);

            var args = new PointerReleasedEventArgs(
                target,
                _pointer,
                tl,
                p,
                (ulong)Environment.TickCount64,
                props,
                KeyModifiers.None,
                MouseButton.Left)
            {
                RoutedEvent = InputElement.PointerReleasedEvent
            };

            target.RaiseEvent(args);
        }

        private PointerEventArgs CreatePointerEventArgs(
    RoutedEvent routedEvent,
    Interactive source,
    TopLevel tl,
    Point p,
    PointerUpdateKind kind)
        {
            var mods = _pressed ? RawInputModifiers.LeftMouseButton : RawInputModifiers.None;
            var props = new PointerPointProperties(mods, kind);

            return new PointerEventArgs(
                routedEvent,
                source,
                _pointer,
                tl,                    // rootVisual
                p,                     // позиция в root
                (ulong)Environment.TickCount64,
                props,
                KeyModifiers.None);
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
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d &&
                d.Windows.Count > 0)
                return d.Windows[0];

            if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime s)
                return TopLevel.GetTopLevel(s.MainView);

            return null;
        }
    }
}
