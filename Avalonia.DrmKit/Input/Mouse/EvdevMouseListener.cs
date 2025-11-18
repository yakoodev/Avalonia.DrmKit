using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.DrmKit.Input;
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
    /// <summary>
    /// Очень простой evdev-слушатель мыши для DRM/fbdev:
    ///  - читает события из /dev/input/event*
    ///  - двигает SoftCursorLayer
    ///  - генерирует PointerMoved / PointerPressed / PointerReleased / PointerEntered / PointerExited.
    /// 
    /// Важно: SoftCursorLayer полностью игнорируется при хит-тесте, чтобы :pointerover
    /// работал по реальным контролам, а не по overlay.
    /// </summary>
    internal sealed class EvdevMouseListener
    {
        private volatile bool _running;
        private readonly List<Thread> _threads = new();

        private double _x;
        private double _y;
        private bool _pressed;

        // Один логический указатель для всей "мыши".
        private readonly Pointer _pointer =
            new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);

        // Текущий элемент под курсором (leaf в цепочке).
        private IInputElement? _currentOver;

        // Цепочка элементов от parent до leaf (без самого TopLevel / IInputRoot).
        private List<IInputElement>? _overChain;
        private static readonly List<IInputElement> s_emptyInputChain = new();

        public void Start()
        {
            if (!OperatingSystem.IsLinux())
                return;

            _running = true;

            // Стартовая позиция курсора — центр первого TopLevel.
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
                    IsBackground = true,
                    Name = $"DrmKit-EvdevMouse-{Path.GetFileName(dev)}"
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
                try
                {
                    t.Join(200);
                }
                catch
                {
                    // ignore
                }
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

                    switch (e.type)
                    {
                        case 2: // EV_REL – относительное движение
                            HandleRelativeEvent(e);
                            break;

                        case 3: // EV_ABS – сейчас не используем (тач / планшеты)
                            break;

                        case 1: // EV_KEY – кнопки мыши
                            HandleKeyEvent(e);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DrmKit][evdev-mouse] {path} error: {ex}");
                Thread.Sleep(200);
            }
        }

        private void HandleRelativeEvent(input_event e)
        {
            if (e.code == 0)          // REL_X
                _x += e.value;
            else if (e.code == 1)     // REL_Y
                _y += e.value;

            ClampAndUpdate();
        }

        private void HandleKeyEvent(input_event e)
        {
            // Нас интересует только левая кнопка.
            if (e.code != 272 /* BTN_LEFT */)
                return;

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

        /// <summary>
        /// Ограничиваем координаты в пределах окна, обновляем soft-курсор
        /// и дергаем ProcessPointerMove (PointerMoved/Entered/Exited).
        /// </summary>
        private void ClampAndUpdate()
        {
            Dispatcher.UIThread.Post(() =>
            {
                var tl = LifetimeTopLevel();
                if (tl is null)
                    return;

                // Если окно ещё не измерено – просто ничего не делаем.
                if (tl.Bounds.Width <= 0 || tl.Bounds.Height <= 0)
                    return;

                _x = Math.Clamp(_x, 0, Math.Max(0, tl.Bounds.Width - 1));
                _y = Math.Clamp(_y, 0, Math.Max(0, tl.Bounds.Height - 1));

                var p = new Point(_x, _y);

                SoftCursorHub.UpdatePosition(p);
                ProcessPointerMove(tl, p);
            });
        }

        /// <summary>
        /// Хит-тест и обработка PointerEntered/Exited/PointerMoved.
        /// </summary>
        private void ProcessPointerMove(TopLevel tl, Point p)
        {
            var root = (IInputRoot)tl;

            // Сбрасываем возможный pointer capture, чтобы он не ломал :pointerover.
            if (_pointer.Captured != null)
            {
                try
                {
                    _pointer.Capture(null);
                }
                catch
                {
                    // На всякий случай не валим поток, если что-то пошло не так.
                }
            }

            // Хит-тест по реальным контролам. SoftCursorLayer игнорируем:
            IInputElement? hitElement = null;

            if (root is InputElement visualRoot)
            {
                var hit = visualRoot.InputHitTest(p, v => v is not SoftCursorLayer);
                hitElement = hit as IInputElement;
            }

            var newChain = BuildInputChain(hitElement, root);
            var oldChain = _overChain ?? s_emptyInputChain;

            // Общий префикс (parent->…->leaf).
            var common = 0;
            while (common < oldChain.Count &&
                   common < newChain.Count &&
                   ReferenceEquals(oldChain[common], newChain[common]))
            {
                common++;
            }

            // --- PointerExited для элементов, которые больше не под курсором.
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

            // --- PointerEntered для новых элементов.
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

            // Последний элемент в цепочке – реальный leaf под курсором.
            var newLeaf = newChain.Count > 0 ? newChain[^1] : null;
            _currentOver = newLeaf;

            // Обычный PointerMoved для leaf'а.
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

        /// <summary>
        /// Собираем цепочку InputElement'ов от ближайшего родителя до leaf (TopLevel/IInputRoot не включаем).
        /// </summary>
        private static List<IInputElement> BuildInputChain(IInputElement? hitElement, IInputRoot root)
        {
            if (hitElement is null)
                return s_emptyInputChain;

            var chain = new List<IInputElement>();
            var v = hitElement as Visual;

            while (v != null && v != root)
            {
                if (v is IInputElement ie)
                    chain.Add(ie);

                v = v.GetVisualParent();
            }

            // Сейчас chain = [leaf, ..., parent]. Разворачиваем, чтобы было parent->…->leaf.
            chain.Reverse();
            return chain;
        }

        private void PressAt()
        {
            var tl = LifetimeTopLevel();
            if (tl is null)
                return;

            SoftCursorHub.SetPressed(true);

            var p = new Point(_x, _y);

            // Актуализируем состояние Over на момент нажатия.
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

            // Актуализируем Over на момент отпускания.
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

            // На всякий случай снимаем возможный capture,
            // чтобы он не влиял на последующие PointerEntered/Exited.
            try
            {
                if (_pointer.Captured != null)
                    _pointer.Capture(null);
            }
            catch
            {
                // ignore
            }
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
                tl,
                p,
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
            {
                return d.Windows[0];
            }

            if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime s)
            {
                return TopLevel.GetTopLevel(s.MainView);
            }

            return null;
        }
    }
}
