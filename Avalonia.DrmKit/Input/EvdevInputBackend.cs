using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LinuxFramebuffer.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Avalonia.DrmKit.Input;

/// <summary>
/// Unified DRM input backend for LinuxFramebuffer.
/// Reads evdev events from /dev/input/event* and forwards them to Avalonia raw input pipeline.
/// </summary>
public class DrmUnifiedInputBackend : IInputBackend
{
    private readonly MouseDevice _mouse = new();
    private readonly KeyboardDevice _keyboard = new();
    private readonly TouchDevice _touch = new();

    private readonly XkbKeyboardProcessor _xkb;

    private IScreenInfoProvider? _screen;
    private IInputRoot? _inputRoot;
    private Action<RawInputEventArgs>? _onInput;

    private readonly object _sync = new();
    private readonly List<Thread> _deviceThreads = new();
    private readonly Dictionary<string, DateTimeOffset> _deviceErrorLoggedAt = new();
    private readonly Dictionary<string, DeviceInputState> _deviceStates = new();
    private volatile bool _running;
    private readonly bool _debugInput = Environment.GetEnvironmentVariable("DRMKIT_DEBUG") == "1";

    private double _cursorX;
    private double _cursorY;

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

    private bool _touchDown;
    private long _touchId;

    private const ushort EV_SYN = 0x00;
    private const ushort EV_KEY = 0x01;
    private const ushort EV_REL = 0x02;
    private const ushort EV_ABS = 0x03;

    private const ushort REL_X = 0x00;
    private const ushort REL_Y = 0x01;
    private const ushort REL_HWHEEL = 0x06;
    private const ushort REL_WHEEL = 0x08;

    private const ushort ABS_X = 0x00;
    private const ushort ABS_Y = 0x01;
    private const ushort ABS_MT_POSITION_X = 0x35;
    private const ushort ABS_MT_POSITION_Y = 0x36;
    private const ushort ABS_MT_TRACKING_ID = 0x39;

    private const ushort BTN_LEFT = 0x110;
    private const ushort BTN_RIGHT = 0x111;
    private const ushort BTN_MIDDLE = 0x112;
    private const ushort BTN_TOUCH = 0x14a;
    private const int EVIOCGABS_BASE = 0x40;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct InputAbsInfo
    {
        public int value;
        public int minimum;
        public int maximum;
        public int fuzz;
        public int flat;
        public int resolution;
    }

    private sealed class AxisCalibration
    {
        public int Min;
        public int Max;
        public bool HasKernelRange;
        public bool HasObservation;

        public void UpdateObservation(int raw)
        {
            if (!HasObservation)
            {
                Min = raw;
                Max = raw;
                HasObservation = true;
                return;
            }

            if (raw < Min) Min = raw;
            if (raw > Max) Max = raw;
        }

        public int Span => Max - Min;
    }

    private sealed class DeviceInputState
    {
        public string Path { get; init; } = string.Empty;
        public AxisCalibration AbsX { get; } = new();
        public AxisCalibration AbsY { get; } = new();
        public AxisCalibration MtX { get; } = new();
        public AxisCalibration MtY { get; } = new();
        public bool PreferMtAxes { get; set; }
        public bool HasTrackingId { get; set; }
        public bool IsKeyboardCapable { get; set; }
        public bool KeyboardCapableLogged { get; set; }
        public bool TouchDown { get; set; }
        public long TouchId { get; set; }
    }

    private static readonly int InputEventSize = Marshal.SizeOf<InputEvent>();

    public DrmUnifiedInputBackend()
        : this(new DrmKeyboardOptions())
    {
    }

    public DrmUnifiedInputBackend(DrmKeyboardOptions keyboardOptions)
    {
        _xkb = new XkbKeyboardProcessor(keyboardOptions ?? new DrmKeyboardOptions());
        if (OperatingSystem.IsLinux() && !_xkb.IsAvailable)
            Console.Error.WriteLine("[DrmKit][input] xkbcommon is unavailable. Keyboard text input will use limited fallback.");
    }

    ~DrmUnifiedInputBackend()
    {
        _xkb.Dispose();
    }

    public void Initialize(IScreenInfoProvider screen, Action<RawInputEventArgs> onInput)
    {
        _screen = screen;
        _onInput = onInput;
        _running = true;

        var size = screen.ScaledSize;
        _cursorX = size.Width / 2.0;
        _cursorY = size.Height / 2.0;

        var devices = EnumerateEventDevices().ToList();
        if (devices.Count == 0)
        {
            Console.Error.WriteLine("[DrmKit][input] No /dev/input/event* devices found.");
        }
        else if (_debugInput)
        {
            Console.WriteLine($"[DrmKit][input] Found {devices.Count} evdev devices: {string.Join(", ", devices)}");
        }

        foreach (var path in devices)
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
            if (_debugInput)
                Console.WriteLine($"[DrmKit][input] Opening device: {path}");

            using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            var deviceState = CreateDeviceState(path, fs);

            var buffer = new byte[InputEventSize];

            while (_running)
            {
                var read = fs.Read(buffer, 0, buffer.Length);
                if (read != buffer.Length)
                    continue;

                var ev = ByteArrayToInputEvent(buffer);
                ProcessInputEvent(ev, deviceState);
            }
        }
        catch (Exception ex)
        {
            if (ShouldLogDeviceError(path))
                Console.Error.WriteLine($"[DrmKit][input] Failed to read {path}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool ShouldLogDeviceError(string path)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            if (!_deviceErrorLoggedAt.TryGetValue(path, out var last) || (now - last) >= TimeSpan.FromSeconds(5))
            {
                _deviceErrorLoggedAt[path] = now;
                return true;
            }
        }

        return false;
    }

    private static unsafe InputEvent ByteArrayToInputEvent(byte[] buffer)
    {
        fixed (byte* ptr = buffer)
        {
            return Marshal.PtrToStructure<InputEvent>((IntPtr)ptr);
        }
    }

    private void ProcessInputEvent(InputEvent ev, DeviceInputState device)
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

            case EV_ABS:
                HandleAbsoluteEvent(ev, root, handler, device);
                break;

            case EV_KEY:
                HandleKeyEvent(ev, root, handler, device);
                break;

            case EV_SYN:
            default:
                break;
        }
    }

    private void HandleRelativeEvent(InputEvent ev, IInputRoot root, Action<RawInputEventArgs> handler)
    {
        lock (_sync)
        {
            switch (ev.code)
            {
                case REL_X:
                    _cursorX += ev.value;
                    EmitPointerMove(ev.time, root, handler);
                    break;
                case REL_Y:
                    _cursorY += ev.value;
                    EmitPointerMove(ev.time, root, handler);
                    break;
                case REL_WHEEL:
                    EmitWheel(ev.time, root, handler, new Vector(0, -ev.value));
                    break;
                case REL_HWHEEL:
                    EmitWheel(ev.time, root, handler, new Vector(ev.value, 0));
                    break;
            }
        }
    }

    private void HandleAbsoluteEvent(InputEvent ev, IInputRoot root, Action<RawInputEventArgs> handler, DeviceInputState device)
    {
        lock (_sync)
        {
            switch (ev.code)
            {
                case ABS_X:
                    if (device.PreferMtAxes)
                        break;

                    device.AbsX.UpdateObservation(ev.value);
                    _cursorX = MapAbsoluteToScreen(ev.value, device.AbsX, (int)Math.Round(_screen?.ScaledSize.Width ?? 0));
                    EmitPointerMove(ev.time, root, handler);
                    if (_touchDown)
                        EmitTouch(ev.time, root, handler, RawPointerEventType.TouchUpdate);
                    break;

                case ABS_Y:
                    if (device.PreferMtAxes)
                        break;

                    device.AbsY.UpdateObservation(ev.value);
                    _cursorY = MapAbsoluteToScreen(ev.value, device.AbsY, (int)Math.Round(_screen?.ScaledSize.Height ?? 0));
                    EmitPointerMove(ev.time, root, handler);
                    if (_touchDown)
                        EmitTouch(ev.time, root, handler, RawPointerEventType.TouchUpdate);
                    break;

                case ABS_MT_POSITION_X:
                    device.PreferMtAxes = true;
                    device.MtX.UpdateObservation(ev.value);
                    _cursorX = MapAbsoluteToScreen(ev.value, device.MtX, (int)Math.Round(_screen?.ScaledSize.Width ?? 0));
                    EmitPointerMove(ev.time, root, handler);
                    if (_touchDown)
                        EmitTouch(ev.time, root, handler, RawPointerEventType.TouchUpdate);
                    break;

                case ABS_MT_POSITION_Y:
                    device.PreferMtAxes = true;
                    device.MtY.UpdateObservation(ev.value);
                    _cursorY = MapAbsoluteToScreen(ev.value, device.MtY, (int)Math.Round(_screen?.ScaledSize.Height ?? 0));
                    EmitPointerMove(ev.time, root, handler);
                    if (_touchDown)
                        EmitTouch(ev.time, root, handler, RawPointerEventType.TouchUpdate);
                    break;

                case ABS_MT_TRACKING_ID:
                    device.HasTrackingId = true;
                    if (ev.value >= 0 && !_touchDown)
                    {
                        _touchId = ev.value;
                        _touchDown = true;
                        EmitTouch(ev.time, root, handler, RawPointerEventType.TouchBegin);
                    }
                    else if (ev.value < 0 && _touchDown)
                    {
                        EmitTouch(ev.time, root, handler, RawPointerEventType.TouchEnd);
                        _touchDown = false;
                    }
                    break;
            }
        }
    }

    private void HandleKeyEvent(InputEvent ev, IInputRoot root, Action<RawInputEventArgs> handler, DeviceInputState device)
    {
        lock (_sync)
        {
            var keyState = ev.value;
            var isDownOrRepeat = keyState != 0;
            var isRepeat = keyState == 2;

            if (ev.code is BTN_LEFT or BTN_RIGHT or BTN_MIDDLE)
            {
                HandleMouseButton(ev, root, handler, isDownOrRepeat);
                return;
            }

            if (ev.code == BTN_TOUCH)
            {
                if (device.HasTrackingId)
                    return;

                HandleTouchButton(ev, root, handler, isDownOrRepeat);
                return;
            }

            if (ev.code <= 0xFF)
                device.IsKeyboardCapable = true;

            if (device.IsKeyboardCapable && _debugInput && !device.KeyboardCapableLogged)
            {
                device.KeyboardCapableLogged = true;
                Console.WriteLine($"[DrmKit][input] Device marked keyboard-capable: {device.Path}");
            }

            if (!device.IsKeyboardCapable)
                return;

            var key = KeyboardMapper.MapScanCode(ev.code);
            if (key == Key.None)
                return;

            UpdateModifierState(key, isDownOrRepeat);
            var modifiers = GetCurrentModifiers();

            var physicalKey = MapPhysicalKey(ev.code);
            var xkbText = _xkb.ProcessKey(ev.code, keyState);
            var keySymbol = string.IsNullOrEmpty(xkbText) ? null : xkbText;
            var isEditingControlKey = IsEditingControlKey(key);

            if (isEditingControlKey)
            {
                PostRoutedKeyToFocused(root, key, isDownOrRepeat);
                if (_debugInput)
                    Console.WriteLine($"[DrmKit][input][route] key={key} state={keyState} target=focused-control");
            }

            if (!isEditingControlKey)
            {
                var keyArgs = new RawKeyEventArgs(
                    _keyboard,
                    ToTimestamp(ev.time),
                    root,
                    isDownOrRepeat ? RawKeyEventType.KeyDown : RawKeyEventType.KeyUp,
                    key,
                    modifiers,
                    physicalKey: physicalKey,
                    keyDeviceType: KeyDeviceType.Keyboard,
                    keySymbol: keySymbol);

                Post(handler, keyArgs);
            }

            if (isDownOrRepeat)
            {
                var text = xkbText;
                if (string.IsNullOrEmpty(text))
                    text = TryConvertScancodeToTextFallback(ev.code, modifiers, isRepeat);

                if (_debugInput)
                {
                    Console.WriteLine(
                        $"[DrmKit][input][key] code={ev.code} key={key} state={keyState} " +
                        $"xkb='{xkbText ?? ""}' fallback='{(string.IsNullOrEmpty(xkbText) ? text ?? "" : "")}'");
                }

                if (!string.IsNullOrEmpty(text))
                {
                    PostTextInput(root, handler, ToTimestamp(ev.time), text!);
                }
            }
        }
    }

    private void PostTextInput(IInputRoot root, Action<RawInputEventArgs> handler, ulong timestamp, string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var focused = TryGetFocusedInputElement(root);
            if (_debugInput)
                Console.WriteLine($"[DrmKit][input][text] '{text}' focused={focused?.GetType().Name ?? "<null>"}");

            if (focused is IInputElement inputElement)
            {
                inputElement.RaiseEvent(new TextInputEventArgs
                {
                    RoutedEvent = InputElement.TextInputEvent,
                    Text = text
                });
                return;
            }

            handler(new RawTextInputEventArgs(_keyboard, timestamp, root, text));
        }, DispatcherPriority.Input);
    }

    private static IInputElement? TryGetFocusedInputElement(IInputRoot root)
    {
        if (root is TopLevel topLevel)
            return topLevel.FocusManager?.GetFocusedElement();

        if (root is Visual visualRoot)
        {
            var visualTopLevel = TopLevel.GetTopLevel(visualRoot);
            if (visualTopLevel is not null)
                return visualTopLevel.FocusManager?.GetFocusedElement();
        }

        var app = Application.Current;
        if (app?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var active = desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.Windows.FirstOrDefault();
            if (active is not null)
                return active.FocusManager?.GetFocusedElement();
        }
        else if (app?.ApplicationLifetime is ISingleViewApplicationLifetime single && single.MainView is not null)
        {
            var tl = TopLevel.GetTopLevel(single.MainView);
            if (tl is not null)
                return tl.FocusManager?.GetFocusedElement();
        }

        return null;
    }

    private static double MapAbsoluteToScreen(int raw, AxisCalibration axis, int screenSize)
    {
        if (screenSize <= 1)
            return 0;

        var min = axis.Min;
        var max = axis.Max;
        if (axis.Span <= 0)
            return Math.Clamp(raw, 0, screenSize - 1);

        var normalized = (raw - min) / (double)(max - min);
        return Math.Clamp(normalized * (screenSize - 1), 0, screenSize - 1);
    }

    private DeviceInputState CreateDeviceState(string path, FileStream fs)
    {
        var state = new DeviceInputState
        {
            Path = path
        };

        TryReadAbsCalibration(fs, ABS_MT_POSITION_X, state.MtX);
        TryReadAbsCalibration(fs, ABS_MT_POSITION_Y, state.MtY);
        TryReadAbsCalibration(fs, ABS_X, state.AbsX);
        TryReadAbsCalibration(fs, ABS_Y, state.AbsY);

        state.PreferMtAxes = state.MtX.HasKernelRange && state.MtY.HasKernelRange;

        lock (_sync)
            _deviceStates[path] = state;

        if (_debugInput)
        {
            Console.WriteLine(
                $"[DrmKit][input] Device calib {path}: " +
                $"MT=({FormatAxis(state.MtX)},{FormatAxis(state.MtY)}), " +
                $"ABS=({FormatAxis(state.AbsX)},{FormatAxis(state.AbsY)}), preferMT={state.PreferMtAxes}");
        }

        return state;
    }

    private static string FormatAxis(AxisCalibration axis)
        => axis.HasKernelRange ? $"{axis.Min}..{axis.Max}" : "n/a";

    private static void TryReadAbsCalibration(FileStream fs, ushort code, AxisCalibration axis)
    {
        try
        {
            var fd = fs.SafeFileHandle.DangerousGetHandle().ToInt32();
            var request = BuildEviocgabsRequest(code);
            if (ioctl(fd, request, out var abs) == 0 && abs.maximum > abs.minimum)
            {
                axis.Min = abs.minimum;
                axis.Max = abs.maximum;
                axis.HasKernelRange = true;
                axis.HasObservation = true;
            }
        }
        catch
        {
            // Kernel range probing is optional. Runtime observation fallback will be used.
        }
    }

    private static ulong BuildEviocgabsRequest(ushort absCode)
    {
        const int iocNrBits = 8;
        const int iocTypeBits = 8;
        const int iocSizeBits = 14;
        const int iocNrShift = 0;
        const int iocTypeShift = iocNrShift + iocNrBits;
        const int iocSizeShift = iocTypeShift + iocTypeBits;
        const int iocDirShift = iocSizeShift + iocSizeBits;
        const uint iocRead = 2;

        var size = (uint)Marshal.SizeOf<InputAbsInfo>();
        var nr = (uint)(EVIOCGABS_BASE + absCode);

        return (iocRead << iocDirShift)
               | ((uint)'E' << iocTypeShift)
               | (nr << iocNrShift)
               | (size << iocSizeShift);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, ulong request, out InputAbsInfo absInfo);

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

        ClampCursor();

        var args = new RawPointerEventArgs(
            _mouse,
            ToTimestamp(ev.time),
            root,
            type,
            new Point(_cursorX, _cursorY),
            GetCurrentModifiers());

        Post(handler, args);
    }

    private void HandleTouchButton(InputEvent ev, IInputRoot root, Action<RawInputEventArgs> handler, bool isDown)
    {
        if (isDown && !_touchDown)
        {
            _touchDown = true;
            _touchId++;
            EmitTouch(ev.time, root, handler, RawPointerEventType.TouchBegin);
            return;
        }

        if (!isDown && _touchDown)
        {
            EmitTouch(ev.time, root, handler, RawPointerEventType.TouchEnd);
            _touchDown = false;
        }
    }

    private void EmitPointerMove(TimeVal time, IInputRoot root, Action<RawInputEventArgs> handler)
    {
        ClampCursor();

        var args = new RawPointerEventArgs(
            _mouse,
            ToTimestamp(time),
            root,
            RawPointerEventType.Move,
            new Point(_cursorX, _cursorY),
            GetCurrentModifiers());

        Post(handler, args);
    }

    private void EmitWheel(TimeVal time, IInputRoot root, Action<RawInputEventArgs> handler, Vector delta)
    {
        ClampCursor();

        var args = new RawMouseWheelEventArgs(
            _mouse,
            ToTimestamp(time),
            root,
            new Point(_cursorX, _cursorY),
            delta,
            GetCurrentModifiers());

        Post(handler, args);
    }

    private void EmitTouch(TimeVal time, IInputRoot root, Action<RawInputEventArgs> handler, RawPointerEventType type)
    {
        ClampCursor();

        var args = new RawTouchEventArgs(
            _touch,
            ToTimestamp(time),
            root,
            type,
            new Point(_cursorX, _cursorY),
            RawInputModifiers.None,
            _touchId);

        Post(handler, args);
    }

    private void ClampCursor()
    {
        var size = _screen?.ScaledSize ?? default;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        _cursorX = Math.Clamp(_cursorX, 0, size.Width - 1);
        _cursorY = Math.Clamp(_cursorY, 0, size.Height - 1);
    }

    private static string? TryConvertScancodeToTextFallback(ushort evdevCode, RawInputModifiers modifiers, bool isRepeat)
    {
        if (isRepeat)
            return null;

        var shift = (modifiers & RawInputModifiers.Shift) != 0;

        if (TryMapLetter(evdevCode, out var lower))
            return shift ? char.ToUpperInvariant(lower).ToString() : lower.ToString();

        if (TryMapDigit(evdevCode, shift, out var digitOrSymbol))
            return digitOrSymbol.ToString();

        if (TryMapPunctuation(evdevCode, shift, out var punctuation))
            return punctuation.ToString();

        return evdevCode switch
        {
            57 => " ",
            _ => null
        };
    }

    private static bool IsEditingControlKey(Key key)
        => key is Key.Back
            or Key.Delete
            or Key.Left
            or Key.Right
            or Key.Up
            or Key.Down
            or Key.Home
            or Key.End
            or Key.PageUp
            or Key.PageDown;

    private static void PostRoutedKeyToFocused(IInputRoot root, Key key, bool isDown)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var focused = TryGetFocusedInputElement(root);
            if (focused is not InputElement inputElement)
                return;

            inputElement.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = isDown ? InputElement.KeyDownEvent : InputElement.KeyUpEvent,
                Key = key
            });
        }, DispatcherPriority.Input);
    }

    private static bool TryMapLetter(ushort code, out char c)
    {
        c = code switch
        {
            16 => 'q', 17 => 'w', 18 => 'e', 19 => 'r', 20 => 't',
            21 => 'y', 22 => 'u', 23 => 'i', 24 => 'o', 25 => 'p',
            30 => 'a', 31 => 's', 32 => 'd', 33 => 'f', 34 => 'g',
            35 => 'h', 36 => 'j', 37 => 'k', 38 => 'l',
            44 => 'z', 45 => 'x', 46 => 'c', 47 => 'v', 48 => 'b',
            49 => 'n', 50 => 'm',
            _ => '\0'
        };

        return c != '\0';
    }

    private static bool TryMapDigit(ushort code, bool shift, out char c)
    {
        c = '\0';
        switch (code)
        {
            case 2: c = shift ? '!' : '1'; return true;
            case 3: c = shift ? '@' : '2'; return true;
            case 4: c = shift ? '#' : '3'; return true;
            case 5: c = shift ? '$' : '4'; return true;
            case 6: c = shift ? '%' : '5'; return true;
            case 7: c = shift ? '^' : '6'; return true;
            case 8: c = shift ? '&' : '7'; return true;
            case 9: c = shift ? '*' : '8'; return true;
            case 10: c = shift ? '(' : '9'; return true;
            case 11: c = shift ? ')' : '0'; return true;
            default: return false;
        }
    }

    private static bool TryMapPunctuation(ushort code, bool shift, out char c)
    {
        c = '\0';
        switch (code)
        {
            case 12: c = shift ? '_' : '-'; return true;
            case 13: c = shift ? '+' : '='; return true;
            case 26: c = shift ? '{' : '['; return true;
            case 27: c = shift ? '}' : ']'; return true;
            case 39: c = shift ? ':' : ';'; return true;
            case 40: c = shift ? '"' : '\''; return true;
            case 41: c = shift ? '~' : '`'; return true;
            case 43: c = shift ? '|' : '\\'; return true;
            case 51: c = shift ? '<' : ','; return true;
            case 52: c = shift ? '>' : '.'; return true;
            case 53: c = shift ? '?' : '/'; return true;
            default: return false;
        }
    }

    private static ulong ToTimestamp(TimeVal tv)
    {
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

    private static PhysicalKey MapPhysicalKey(ushort evdevCode)
    {
        return evdevCode switch
        {
            1 => PhysicalKey.Escape,
            2 => PhysicalKey.Digit1,
            3 => PhysicalKey.Digit2,
            4 => PhysicalKey.Digit3,
            5 => PhysicalKey.Digit4,
            6 => PhysicalKey.Digit5,
            7 => PhysicalKey.Digit6,
            8 => PhysicalKey.Digit7,
            9 => PhysicalKey.Digit8,
            10 => PhysicalKey.Digit9,
            11 => PhysicalKey.Digit0,
            12 => PhysicalKey.Minus,
            13 => PhysicalKey.Equal,
            14 => PhysicalKey.Backspace,
            15 => PhysicalKey.Tab,
            16 => PhysicalKey.Q,
            17 => PhysicalKey.W,
            18 => PhysicalKey.E,
            19 => PhysicalKey.R,
            20 => PhysicalKey.T,
            21 => PhysicalKey.Y,
            22 => PhysicalKey.U,
            23 => PhysicalKey.I,
            24 => PhysicalKey.O,
            25 => PhysicalKey.P,
            26 => PhysicalKey.BracketLeft,
            27 => PhysicalKey.BracketRight,
            28 => PhysicalKey.Enter,
            29 => PhysicalKey.ControlLeft,
            30 => PhysicalKey.A,
            31 => PhysicalKey.S,
            32 => PhysicalKey.D,
            33 => PhysicalKey.F,
            34 => PhysicalKey.G,
            35 => PhysicalKey.H,
            36 => PhysicalKey.J,
            37 => PhysicalKey.K,
            38 => PhysicalKey.L,
            39 => PhysicalKey.Semicolon,
            40 => PhysicalKey.Quote,
            41 => PhysicalKey.Backquote,
            42 => PhysicalKey.ShiftLeft,
            43 => PhysicalKey.Backslash,
            44 => PhysicalKey.Z,
            45 => PhysicalKey.X,
            46 => PhysicalKey.C,
            47 => PhysicalKey.V,
            48 => PhysicalKey.B,
            49 => PhysicalKey.N,
            50 => PhysicalKey.M,
            51 => PhysicalKey.Comma,
            52 => PhysicalKey.Period,
            53 => PhysicalKey.Slash,
            54 => PhysicalKey.ShiftRight,
            56 => PhysicalKey.AltLeft,
            57 => PhysicalKey.Space,
            58 => PhysicalKey.CapsLock,
            59 => PhysicalKey.F1,
            60 => PhysicalKey.F2,
            61 => PhysicalKey.F3,
            62 => PhysicalKey.F4,
            63 => PhysicalKey.F5,
            64 => PhysicalKey.F6,
            65 => PhysicalKey.F7,
            66 => PhysicalKey.F8,
            67 => PhysicalKey.F9,
            68 => PhysicalKey.F10,
            87 => PhysicalKey.F11,
            88 => PhysicalKey.F12,
            100 => PhysicalKey.AltRight,
            102 => PhysicalKey.ControlRight,
            103 => PhysicalKey.ArrowUp,
            105 => PhysicalKey.ArrowLeft,
            106 => PhysicalKey.ArrowRight,
            108 => PhysicalKey.ArrowDown,
            111 => PhysicalKey.Delete,
            _ => PhysicalKey.None
        };
    }

    private static void Post(Action<RawInputEventArgs> handler, RawInputEventArgs args)
    {
        Dispatcher.UIThread.Post(() => handler(args), DispatcherPriority.Input);
    }
}

[Obsolete("Use DrmUnifiedInputBackend. This alias will be removed in a future major version.")]
public sealed class EvdevInputBackend : DrmUnifiedInputBackend
{
    public EvdevInputBackend()
    {
    }

    public EvdevInputBackend(DrmKeyboardOptions keyboardOptions)
        : base(keyboardOptions)
    {
    }
}
