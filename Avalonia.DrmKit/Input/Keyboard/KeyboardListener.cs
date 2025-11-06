
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Logging;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;


namespace Avalonia.DrmKit.Input
{
    public static class KeyboardMapper
    {
       
        public static Key MapScanCode(ushort scancode)
        {
            switch (scancode)
            {
                case 2: return Key.D1;
                case 3: return Key.D2;
                case 4: return Key.D3;
                case 5: return Key.D4;
                case 6: return Key.D5;
                case 7: return Key.D6;
                case 8: return Key.D7;
                case 9: return Key.D8;
                case 10: return Key.D9;
                case 11: return Key.D0;
                case 12: return Key.OemMinus;
                case 13: return Key.OemPlus;
                case 14: return Key.Back;
                case 15: return Key.Tab;
                case 16: return Key.Q;
                case 17: return Key.W;
                case 18: return Key.E;
                case 19: return Key.R;
                case 20: return Key.T;
                case 21: return Key.Y;
                case 22: return Key.U;
                case 23: return Key.I;
                case 24: return Key.O;
                case 25: return Key.P;
                case 26: return Key.OemOpenBrackets;
                case 27: return Key.OemCloseBrackets;
                case 28: return Key.Enter;
                case 29: return Key.LeftCtrl;
                case 30: return Key.A;
                case 31: return Key.S;
                case 32: return Key.D;
                case 33: return Key.F;
                case 34: return Key.G;
                case 35: return Key.H;
                case 36: return Key.J;
                case 37: return Key.K;
                case 38: return Key.L;
                case 39: return Key.OemSemicolon;
                case 40: return Key.OemQuotes;
                case 41: return Key.OemTilde;
                case 42: return Key.LeftShift;
                case 43: return Key.OemBackslash;
                case 44: return Key.Z;
                case 45: return Key.X;
                case 46: return Key.C;
                case 47: return Key.V;
                case 48: return Key.B;
                case 49: return Key.N;
                case 50: return Key.M;
                case 51: return Key.OemComma;
                case 52: return Key.OemPeriod;
                case 53: return Key.OemQuestion;
                case 54: return Key.RightShift;
                case 56: return Key.LeftAlt;
                case 57: return Key.Space;
                case 58: return Key.CapsLock;
                case 59: return Key.F1;
                case 60: return Key.F2;
                case 61: return Key.F3;
                case 62: return Key.F4;
                case 63: return Key.F5;
                case 64: return Key.F6;
                case 65: return Key.F7;
                case 66: return Key.F8;
                case 67: return Key.F9;
                case 68: return Key.F10;
                case 69: return Key.NumLock;
                case 71: return Key.NumPad7;
                case 72: return Key.NumPad8;
                case 73: return Key.NumPad9;
                case 74: return Key.Subtract;
                case 75: return Key.NumPad4;
                case 76: return Key.NumPad5;
                case 77: return Key.NumPad6;
                case 78: return Key.Add;
                case 79: return Key.NumPad1;
                case 80: return Key.NumPad2;
                case 81: return Key.NumPad3;
                case 82: return Key.NumPad0;
                case 83: return Key.Decimal;
                case 87: return Key.F11;
                case 88: return Key.F12;
                case 100: return Key.RightAlt;
                case 102: return Key.RightCtrl;
                case 103: return Key.Up;
                case 105: return Key.Left;
                case 106: return Key.Right;
                case 108: return Key.Down;
                case 1: return Key.Escape;
                default:
                    Logger.TryGet(LogEventLevel.Error, LogArea.Control)?.Log(null, $"Unknown scancode: {scancode}");
                    return Key.None;
            }
        }
    }

    public class KeyboardListener
    {
        static readonly bool EvDebug = Environment.GetEnvironmentVariable("DRMKIT_DEBUG") == "1";
        private readonly Dictionary<string, Thread> _inputThreads = new();
        private volatile bool _isRunning;

        public event EventHandler<NewKeyEventArgs>? KeyEvent;

        private readonly HashSet<ushort> _pressed = new(); // сырые коды
        private bool _capsLock;
        private bool _shift;
        private bool _alt;
        public event EventHandler<string>? TextInput;

        public void StartListening()
        {
            Console.WriteLine("[DrmKit] KeyboardListener.StartListening");
            if (!OperatingSystem.IsLinux())
                return;

            var devices = InputDeviceHelper.GetInputDevices();
            Console.WriteLine($"[DrmKit] Keyboard devices: {string.Join(", ", devices.Keys)}");
            if (!devices.Any())
            {
                // retry later
                new Thread(() =>
                {
                    while (!_isRunning)
                    {
                        Thread.Sleep(1000);
                        devices = InputDeviceHelper.GetInputDevices();
                        if (devices.Any())
                        {
                            StartListening();
                            return;
                        }
                    }
                }) { IsBackground = true }.Start();
                return;
            }

            _isRunning = true;
            foreach (var item in devices.Select(t => t.Key))
            {
                var inputThread = new Thread(() => InputThread(item)) { IsBackground = true };
                _inputThreads[item] = inputThread;
                inputThread.Start();
            }
            Avalonia.DrmKit.Input.KeyboardLayoutManager.CapsChanged += (_, on) => { _capsLock = on; };
        }

        public void StopListening()
        {
            _isRunning = false;
            foreach (var t in _inputThreads.Values)
                t.Join();
            _inputThreads.Clear();
        }

        private void InputThread(string path)
        {
            while (_isRunning)
            {
                try
                {
                    Console.WriteLine($"[DrmKit] Keyboard open: {path}");
                    using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    byte[] buffer = new byte[24]; // struct input_event (64-bit)
                    while (_isRunning)
                    {
                        int bytesRead = stream.Read(buffer, 0, buffer.Length);
                        if (bytesRead == buffer.Length)
                        {
                            var inputEvent = BytesToStruct<input_event>(buffer);
                            if (EvDebug && inputEvent.type != 0)
                                Console.WriteLine($"[DrmKit][ev] {Path.GetFileName(path)} type={inputEvent.type} code={inputEvent.code} val={inputEvent.value}");

                            if (inputEvent.type == 1) // EV_KEY
                                HandleKeyPress(inputEvent.code, inputEvent.value);
                        }
                        else
                        {
                            Thread.Sleep(5);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[DrmKit] Keyboard thread error on {path}: {ex.Message}");
                    // keep the device open; brief sleep to avoid busy loop
                    Thread.Sleep(20);
                }
            }
        }

        private void HandleKeyPress(ushort code, int value)
        {
            // модификаторы
            if (code is 42 or 54) _shift = value != 0;          // Shift
            if (code is 56 or 100) _alt = value != 0;          // Alt (Left/Right)
            if (code == 58 && value == 1)
            {
                _capsLock = !_capsLock;
                KeyboardLayoutManager.SetCaps(_capsLock);
            }

            // Alt+Shift в любом порядке → переключить раскладку (и обновить OSK)
            if (value == 1 && (((code is 42 or 54) && _alt) || ((code is 56 or 100) && _shift)))
            {
                var next = Avalonia.DrmKit.Input.KeyboardLayoutManager.Current.Equals("en", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
                Avalonia.DrmKit.Input.KeyboardLayoutManager.SetCurrent(next);   // ← событие в UI
                return;
            }

            var key = KeyboardMapper.MapScanCode(code);

            // печатные → только TextInput (чтобы не было дублей)
            if (value == 1 &&
                Avalonia.DrmKit.Input.KeyboardLayoutManager.TryMap(key, _shift, _capsLock, out var text) &&
                !string.IsNullOrEmpty(text))
            {
                Dispatcher.UIThread.Post(() => TextInput?.Invoke(this, text!));
                return;
            }

            // непечатные → KeyDown/Up
            switch (value)
            {
                case 0: Dispatcher.UIThread.Post(() => KeyEvent?.Invoke(this, new NewKeyEventArgs(key, InputElement.KeyUpEvent))); break;
                case 1: Dispatcher.UIThread.Post(() => KeyEvent?.Invoke(this, new NewKeyEventArgs(key, InputElement.KeyDownEvent))); break;
                case 2: Dispatcher.UIThread.Post(() => KeyEvent?.Invoke(this, new NewKeyEventArgs(key, InputElement.KeyDownEvent, true))); break;
            }
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

        private static T BytesToStruct<T>(byte[] rawData) where T : struct => MemoryMarshal.Read<T>(rawData);
    }

    public class NewKeyEventArgs : KeyEventArgs
    {
        public bool IsRepeat { get; }
        public NewKeyEventArgs(Key key, RoutedEvent? routedEvent, bool isRepeat = false)
        {
            Key = key;
            IsRepeat = isRepeat;
            RoutedEvent = routedEvent;
        }
        public override string ToString() => $"Key:{Key} IsRepeat:{IsRepeat}";
    }

    internal static class InputDeviceHelper
    {
        public static Dictionary<string, string> GetInputDevices()
        {
            var result = new Dictionary<string, string>();
            foreach (var path in GetEventDevicePaths())
                result[path] = "1";
            return result;
        }

        private static string[] GetEventDevicePaths()
        {
            try
            {
                return new DirectoryInfo("/dev/input")
                    .GetFiles()
                    .Where(f => f.Name.StartsWith("event"))
                    .Select(f => f.FullName)
                    .ToArray();
            }
            catch { return Array.Empty<string>(); }
        }
    }
}
