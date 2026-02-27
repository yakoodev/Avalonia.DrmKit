namespace Avalonia.DrmKit.Input;

/// <summary>
/// Keyboard layout configuration for xkbcommon in DRM backend.
/// Empty values use system defaults.
/// </summary>
public sealed class DrmKeyboardOptions
{
    public string Rules { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string Layout { get; set; } = string.Empty;

    public string Variant { get; set; } = string.Empty;

    public string Options { get; set; } = string.Empty;
}
