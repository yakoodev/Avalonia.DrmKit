using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Avalonia.DrmKit.Input;

internal sealed class XkbKeyboardProcessor : IDisposable
{
    private readonly IntPtr _context;
    private readonly IntPtr _keymap;
    private readonly IntPtr _state;

    public bool IsAvailable => _state != IntPtr.Zero;

    public XkbKeyboardProcessor(DrmKeyboardOptions options)
    {
        if (!OperatingSystem.IsLinux())
            return;

        try
        {
            _context = xkb_context_new(0);
            if (_context == IntPtr.Zero)
                return;

            var rules = BuildRuleNames(options);
            _keymap = xkb_keymap_new_from_names(_context, ref rules, 0);
            FreeRuleNames(ref rules);

            if (_keymap == IntPtr.Zero)
                return;

            _state = xkb_state_new(_keymap);
        }
        catch
        {
            // Optional capability. Backend falls back to simple key mapping.
        }
    }

    public string? ProcessKey(ushort evdevCode, int value)
    {
        if (_state == IntPtr.Zero)
            return null;

        var keycode = (uint)(evdevCode + 8);

        if (value == 0)
        {
            xkb_state_update_key(_state, keycode, XkbKeyDirection.Up);
            return null;
        }

        xkb_state_update_key(_state, keycode, XkbKeyDirection.Down);

        var len = xkb_state_key_get_utf8(_state, keycode, null, 0);
        if (len <= 0)
            return null;

        var sb = new StringBuilder(len + 1);
        xkb_state_key_get_utf8(_state, keycode, sb, (nuint)sb.Capacity);

        var text = sb.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        foreach (var c in text)
        {
            if (char.IsControl(c))
                return null;
        }

        return text;
    }

    public void Dispose()
    {
        if (_state != IntPtr.Zero)
            xkb_state_unref(_state);

        if (_keymap != IntPtr.Zero)
            xkb_keymap_unref(_keymap);

        if (_context != IntPtr.Zero)
            xkb_context_unref(_context);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XkbRuleNames
    {
        public IntPtr rules;
        public IntPtr model;
        public IntPtr layout;
        public IntPtr variant;
        public IntPtr options;
    }

    private static XkbRuleNames BuildRuleNames(DrmKeyboardOptions options)
    {
        return new XkbRuleNames
        {
            rules = StringToPtr(options.Rules),
            model = StringToPtr(options.Model),
            layout = StringToPtr(options.Layout),
            variant = StringToPtr(options.Variant),
            options = StringToPtr(options.Options)
        };
    }

    private static void FreeRuleNames(ref XkbRuleNames names)
    {
        FreePtr(names.rules);
        FreePtr(names.model);
        FreePtr(names.layout);
        FreePtr(names.variant);
        FreePtr(names.options);

        names.rules = IntPtr.Zero;
        names.model = IntPtr.Zero;
        names.layout = IntPtr.Zero;
        names.variant = IntPtr.Zero;
        names.options = IntPtr.Zero;
    }

    private static IntPtr StringToPtr(string? value)
        => string.IsNullOrWhiteSpace(value) ? IntPtr.Zero : Marshal.StringToHGlobalAnsi(value);

    private static void FreePtr(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero)
            Marshal.FreeHGlobal(ptr);
    }

    private enum XkbKeyDirection
    {
        Up = 0,
        Down = 1
    }

    [DllImport("libxkbcommon.so.0")]
    private static extern IntPtr xkb_context_new(int flags);

    [DllImport("libxkbcommon.so.0")]
    private static extern void xkb_context_unref(IntPtr context);

    [DllImport("libxkbcommon.so.0")]
    private static extern IntPtr xkb_keymap_new_from_names(IntPtr context, ref XkbRuleNames names, int flags);

    [DllImport("libxkbcommon.so.0")]
    private static extern void xkb_keymap_unref(IntPtr keymap);

    [DllImport("libxkbcommon.so.0")]
    private static extern IntPtr xkb_state_new(IntPtr keymap);

    [DllImport("libxkbcommon.so.0")]
    private static extern void xkb_state_unref(IntPtr state);

    [DllImport("libxkbcommon.so.0")]
    private static extern uint xkb_state_update_key(IntPtr state, uint key, XkbKeyDirection direction);

    [DllImport("libxkbcommon.so.0", CharSet = CharSet.Ansi)]
    private static extern int xkb_state_key_get_utf8(IntPtr state, uint key, StringBuilder? buffer, nuint size);
}
