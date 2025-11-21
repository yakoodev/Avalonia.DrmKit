using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LinuxFramebuffer.Input;
using Avalonia.Threading;

namespace Avalonia.DrmKit.Input
{
    /// <summary>
    /// IInputBackend для DRM: читает события напрямую из /dev/input/event*
    /// и транслирует их в стандартный Avalonia Input pipeline.
    ///
    /// Покрывает:
    /// - клавиатуру (RawKeyEventArgs + RawTextInputEventArgs);
    /// - мышь (RawPointerEventArgs: Move / ButtonDown / ButtonUp).
    /// </summary>
    public sealed class EvdevInputBackend : IInputBackend
    {
        
        private readonly MouseDevice _mouse = new MouseDevice();
        private readonly KeyboardDevice _keyboard = new KeyboardDevice();

        private IScreenInfoProvider? _screen;
        private IInputRoot? _inputRoot;
        private Action<RawInputEventArgs>? _onInput;

        private double _cursorX;
        private double _cursorY;
        private readonly object _sync = new();

        private readonly List<Thread> _deviceThreads = new();
        private volatile bool _running;

        // ---- состояние модификаторов ----
        private bool _leftShift;
        private bool _rightShift;
        private bool _leftCtrl;
        private bool _rightCtrl;
        private bool _leftAlt;
        private bool _rightAlt;
        private bool _meta;

        private bool _leftButton;
        private bool _rightButton;
        private bool _middleButton;

        // ---- константы evdev ----
        private const ushort EV_SYN = 0x00;
        private const ushort EV_KEY = 0x01;
        private const ushort EV_REL = 0x02;

        private const ushort REL_X = 0x00;
        private const ushort REL_Y = 0x01;

        private const ushort BTN_LEFT = 0x110;
        private const ushort BTN_RIGHT = 0x111;
        private const ushort BTN_MIDDLE = 0x112;

        [StructLayout(LayoutKind.Sequential)]
        private struct TimeVal
        {
            public long tv_sec;
            public long tv_usec;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct InputEvent
        {
            public TimeVal time;
            public ushort type;
            public ushort code;
            public int value;
        }

        private static readonly int InputEventSize = Marshal.SizeOf<InputEvent>();

        // ---------------- IInputBackend ----------------

        public void Initialize(IScreenInfoProvider screen, Action<RawInputEventArgs> onInput)
        {
            _screen = screen;
            _onInput = onInput;
            _running = true;

            // Стартовая позиция курсора – центр экрана.
            var size = screen.ScaledSize;
            _cursorX = size.Width / 2.0;
            _cursorY = size.Height / 2.0;

            foreach (var path in EnumerateEventDevices())
            {
                var thread = new Thread(DeviceThreadProc)
                {
                    IsBackground = true,
                    Name = $"DrmKit.Evdev.{Path.GetFileName(path)}"
                };

                thread.Start(path);
                _deviceThreads.Add(thread);
            }
        }

        public void SetInputRoot(IInputRoot inputRoot)
        {
            _inputRoot = inputRoot;
        }

        // ---------------- работа с /dev/input ----------------

        private static IEnumerable<string> EnumerateEventDevices()
        {
            if (!Directory.Exists("/dev/input"))
                yield break;

            foreach (var file in Directory.EnumerateFiles("/dev/input", "event*"))
                yield return file;
        }

        private void DeviceThreadProc(object? state)
        {
            var path = (string)state!;

            try
            {
                using var fs = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                var buffer = new byte[InputEventSize];

                while (_running)
                {
                    var read = fs.Read(buffer, 0, buffer.Length);
                    if (read != buffer.Length)
                        continue;

                    var ev = ByteArrayToInputEvent(buffer);
                    ProcessInputEvent(ev);
                }
            }
            catch (Exception)
            {
                // Можно залогировать через Avalonia.Logging, если нужно.
            }
        }

        private static unsafe InputEvent ByteArrayToInputEvent(byte[] buffer)
        {
            fixed (byte* ptr = buffer)
            {
                return Marshal.PtrToStructure<InputEvent>((IntPtr)ptr);
            }
        }

        // ---------------- разбор событий ----------------

        private void ProcessInputEvent(InputEvent ev)
        {
            var root = _inputRoot;
            var handler = _onInput;

            if (root is null || handler is null)
                return;

            switch (ev.type)
            {
                case EV_REL:
                    HandleRelativeEvent(ev, root, handler);
                    break;

                case EV_KEY:
                    HandleKeyEvent(ev, root, handler);
                    break;

                case EV_SYN:
                    // сейчас ничего не делаем, можно использовать для буферизации
                    break;
            }
        }

        private void HandleRelativeEvent(InputEvent ev, IInputRoot root, Action<RawInputEventArgs> handler)
        {
            lock (_sync)
            {
                var size = _screen?.ScaledSize ?? default;

                if (ev.code == REL_X)
                    _cursorX += ev.value;
                else if (ev.code == REL_Y)
                    _cursorY += ev.value;
                else
                    return;

                if (size.Width > 0 && size.Height > 0)
                {
                    _cursorX = Math.Clamp(_cursorX, 0, size.Width - 1);
                    _cursorY = Math.Clamp(_cursorY, 0, size.Height - 1);
                }

                var position = new Point(_cursorX, _cursorY);
                var modifiers = GetCurrentModifiers();

                // ⚠ если сигнатура отличается в твоей версии Avalonia — поправь конструктор.
                var args = new RawPointerEventArgs(
                    _mouse,
                    ToTimestamp(ev.time),
                    root,
                    RawPointerEventType.Move,
                    position,
                    modifiers);

                Post(handler, args);
            }
        }

        private void HandleKeyEvent(InputEvent ev, IInputRoot root, Action<RawInputEventArgs> handler)
        {
            var isDown = ev.value != 0;

            // Кнопки мыши тоже приходят как EV_KEY.
            if (ev.code == BTN_LEFT || ev.code == BTN_RIGHT || ev.code == BTN_MIDDLE)
            {
                HandleMouseButton(ev, root, handler, isDown);
                return;
            }

            var key = MapEvdevKey(ev.code);
            if (key == Key.None)
                return;

            UpdateModifierState(key, isDown);

            var modifiers = GetCurrentModifiers();
            var type = isDown ? RawKeyEventType.KeyDown : RawKeyEventType.KeyUp;

            var keyArgs = new RawKeyEventArgs(
                _keyboard,
                ToTimestamp(ev.time),
                root,
                type,
                key,
                modifiers);

            Post(handler, keyArgs);

            // Простейший text input (латиница/цифры/пробел).
            if (isDown)
            {
                var text = TryConvertKeyToText(key);
                if (!string.IsNullOrEmpty(text))
                {
                    var textArgs = new RawTextInputEventArgs(
                        _keyboard,
                        ToTimestamp(ev.time),
                        root,
                        text!);

                    Post(handler, textArgs);
                }
            }
        }

        private void HandleMouseButton(InputEvent ev, IInputRoot root, Action<RawInputEventArgs> handler, bool isDown)
        {
            RawPointerEventType type;

            switch (ev.code)
            {
                case BTN_LEFT:
                    _leftButton = isDown;
                    type = isDown ? RawPointerEventType.LeftButtonDown : RawPointerEventType.LeftButtonUp;
                    break;

                case BTN_RIGHT:
                    _rightButton = isDown;
                    type = isDown ? RawPointerEventType.RightButtonDown : RawPointerEventType.RightButtonUp;
                    break;

                case BTN_MIDDLE:
                    _middleButton = isDown;
                    type = isDown ? RawPointerEventType.MiddleButtonDown : RawPointerEventType.MiddleButtonUp;
                    break;

                default:
                    return;
            }

            var position = new Point(_cursorX, _cursorY);
            var modifiers = GetCurrentModifiers();

            var args = new RawPointerEventArgs(
                _mouse,
                ToTimestamp(ev.time),
                root,
                type,
                position,
                modifiers);

            Post(handler, args);
        }

        // ---------------- вспомогательное ----------------

        private void Post(Action<RawInputEventArgs> handler, RawInputEventArgs args)
        {
            // Гоним всё в UI-поток, как это делает X11 / Win32 backend.
            Dispatcher.UIThread.Post(() => handler(args), DispatcherPriority.Input);
        }

        private static ulong ToTimestamp(TimeVal tv)
        {
            // миллисекунды от эпохи; важно только относительное сравнение
            checked
            {
                return (ulong)(tv.tv_sec * 1000 + tv.tv_usec / 1000);
            }
        }

        private void UpdateModifierState(Key key, bool isDown)
        {
            switch (key)
            {
                case Key.LeftShift:
                    _leftShift = isDown;
                    break;
                case Key.RightShift:
                    _rightShift = isDown;
                    break;
                case Key.LeftCtrl:
                    _leftCtrl = isDown;
                    break;
                case Key.RightCtrl:
                    _rightCtrl = isDown;
                    break;
                case Key.LeftAlt:
                    _leftAlt = isDown;
                    break;
                case Key.RightAlt:
                    _rightAlt = isDown;
                    break;
                case Key.LWin:
                case Key.RWin:
                    _meta = isDown;
                    break;
            }
        }

        private RawInputModifiers GetCurrentModifiers()
        {
            RawInputModifiers mods = 0;

            if (_leftShift || _rightShift)
                mods |= RawInputModifiers.Shift;

            if (_leftCtrl || _rightCtrl)
                mods |= RawInputModifiers.Control;

            if (_leftAlt || _rightAlt)
                mods |= RawInputModifiers.Alt;

            if (_meta)
                mods |= RawInputModifiers.Meta;

            if (_leftButton)
                mods |= RawInputModifiers.LeftMouseButton;

            if (_rightButton)
                mods |= RawInputModifiers.RightMouseButton;

            if (_middleButton)
                mods |= RawInputModifiers.MiddleMouseButton;

            return mods;
        }

        /// <summary>
        /// Очень простая US-раскладка: только буквы, цифры и пробел.
        /// CapsLock/Shift пока не учитываем (можно доработать).
        /// </summary>
        private static string? TryConvertKeyToText(Key key)
        {
            if (key >= Key.A && key <= Key.Z)
            {
                var c = (char)('a' + (key - Key.A));
                return c.ToString();
            }

            if (key >= Key.D0 && key <= Key.D9)
            {
                var c = (char)('0' + (key - Key.D0));
                return c.ToString();
            }

            if (key == Key.Space)
                return " ";

            return null;
        }

        /// <summary>
        /// Минимальная таблица evdev-код → Avalonia.Key.
        /// Здесь можно просто перенести всю твою маппу из текущего KeyboardListener.
        /// </summary>
        private static Key MapEvdevKey(ushort code) => code switch
        {
            1 => Key.Escape,
            2 => Key.D1,
            3 => Key.D2,
            4 => Key.D3,
            5 => Key.D4,
            6 => Key.D5,
            7 => Key.D6,
            8 => Key.D7,
            9 => Key.D8,
            10 => Key.D9,
            11 => Key.D0,
            12 => Key.OemMinus,
            13 => Key.OemPlus,
            14 => Key.Back,
            15 => Key.Tab,
            16 => Key.Q,
            17 => Key.W,
            18 => Key.E,
            19 => Key.R,
            20 => Key.T,
            21 => Key.Y,
            22 => Key.U,
            23 => Key.I,
            24 => Key.O,
            25 => Key.P,
            26 => Key.OemOpenBrackets,
            27 => Key.OemCloseBrackets,
            28 => Key.Enter,
            29 => Key.LeftCtrl,
            30 => Key.A,
            31 => Key.S,
            32 => Key.D,
            33 => Key.F,
            34 => Key.G,
            35 => Key.H,
            36 => Key.J,
            37 => Key.K,
            38 => Key.L,
            39 => Key.OemSemicolon,
            40 => Key.OemQuotes,
            41 => Key.OemTilde,
            42 => Key.LeftShift,
            43 => Key.OemBackslash,
            44 => Key.Z,
            45 => Key.X,
            46 => Key.C,
            47 => Key.V,
            48 => Key.B,
            49 => Key.N,
            50 => Key.M,
            51 => Key.OemComma,
            52 => Key.OemPeriod,
            53 => Key.OemQuestion,
            54 => Key.RightShift,
            56 => Key.LeftAlt,
            57 => Key.Space,
            58 => Key.CapsLock,
            59 => Key.F1,
            60 => Key.F2,
            61 => Key.F3,
            62 => Key.F4,
            63 => Key.F5,
            64 => Key.F6,
            65 => Key.F7,
            66 => Key.F8,
            67 => Key.F9,
            68 => Key.F10,
            87 => Key.F11,
            88 => Key.F12,
            _ => Key.None
        };
    }
}
