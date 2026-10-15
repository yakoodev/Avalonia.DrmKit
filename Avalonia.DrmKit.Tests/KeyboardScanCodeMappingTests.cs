using Avalonia.DrmKit.Input;
using Avalonia.Input;
using Xunit;

namespace Avalonia.DrmKit.Tests;

/// <summary>
/// Коды клавиш evdev → клавиши Avalonia. Коды — по linux/input-event-codes.h.
/// Раньше 102 (KEY_HOME) отдавался как правый Ctrl, а End/PageUp/PageDown/Insert терялись:
/// на физической клавиатуре в поле ввода не работали Home и End.
/// </summary>
public class KeyboardScanCodeMappingTests
{
    [Theory]
    [InlineData(97, Key.RightCtrl)]
    [InlineData(102, Key.Home)]
    [InlineData(104, Key.PageUp)]
    [InlineData(107, Key.End)]
    [InlineData(109, Key.PageDown)]
    [InlineData(110, Key.Insert)]
    [InlineData(111, Key.Delete)]
    [InlineData(14, Key.Back)]
    [InlineData(105, Key.Left)]
    [InlineData(106, Key.Right)]
    [InlineData(96, Key.Enter)]
    public void Editing_keys_map_to_their_avalonia_keys(ushort code, Key expected)
        => Assert.Equal(expected, KeyboardMapper.MapScanCode(code));

    [Theory]
    [InlineData(97, PhysicalKey.ControlRight)]
    [InlineData(102, PhysicalKey.Home)]
    [InlineData(107, PhysicalKey.End)]
    [InlineData(104, PhysicalKey.PageUp)]
    [InlineData(109, PhysicalKey.PageDown)]
    [InlineData(110, PhysicalKey.Insert)]
    public void Editing_keys_map_to_their_physical_keys(ushort code, PhysicalKey expected)
        => Assert.Equal(expected, DrmUnifiedInputBackend.MapPhysicalKey(code));
}
