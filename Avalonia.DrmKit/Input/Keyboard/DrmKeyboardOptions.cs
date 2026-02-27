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

    /// <summary>
    /// Route editing control keys (Backspace/Delete/Arrows/Home/End/PageUp/PageDown)
    /// directly to the focused control using routed KeyDown/KeyUp events.
    /// </summary>
    public bool UseRoutedControlKeys { get; set; } = true;

    /// <summary>
    /// When true, also emit raw key events for editing control keys.
    /// Useful for compatibility with handlers that listen to raw pipeline.
    /// </summary>
    public bool EnableRawControlKeysFallback { get; set; } = false;

    /// <summary>
    /// Treat Tab as focus navigation only (do not emit '\t' from fallback text mapping).
    /// </summary>
    public bool TabAsNavigation { get; set; } = true;

    /// <summary>
    /// Emit newline from fallback text mapping for Enter when xkb text is unavailable.
    /// </summary>
    public bool EnterAsTextFallback { get; set; } = false;
}
