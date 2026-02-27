# DrmKit.Avalonia

Helpers for running **Avalonia** apps directly on **Linux DRM/Framebuffer** (kiosk / embedded, no X11 / Wayland).

- evdev **keyboard**: EN/RU, Alt+Shift, CapsLock, text input into focused control;
- **on‑screen keyboard overlay** (`KeyboardOverlay`) for touch UI;
- **soft cursor** overlay with auto‑hide and proper pointer over events;
- **evdev mouse fallback** for DRM when there is no windowing system;
- `SmartPlatform` — runtime switch: DRM ↔ desktop with CLI options.

> Main namespace: `Avalonia.DrmKit`  
> Recommended: Avalonia 11, .NET 8, Linux with `/dev/dri/card*` and `/dev/input/event*`.

---

## Install

```bash
dotnet add package DrmKit.Avalonia
```

---

## Quick start

**Program.cs**:

```csharp
using Avalonia;
using Avalonia.DrmKit;

internal static class Program
{
    public static int Main(string[] args)
        => SmartPlatform.Start(BuildAvaloniaApp(), args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI()
            .UseDrmSoftCursor()         // soft cursor overlay for DRM
            .UseLinuxKeyboardListener() // evdev keyboard (EN/RU, Alt+Shift, CapsLock)
            .UseEvdevMouseFallback();   // evdev mouse + pointerover fix for DRM
}
```

Run in DRM mode:

```bash
# user should be in 'video' and 'input' groups
./YourApp --system=drm --drm=card1 --resolution=1280x720

# debug evdev input
DRMKIT_DEBUG=1 ./YourApp --system=drm --drm=card1
```

If DRM cannot be initialized, `SmartPlatform` falls back to the standard Avalonia desktop backend.

---

## On‑screen keyboard overlay

Attach overlay keyboard to any `TextBox`:

```xml
<TextBox
    xmlns:osk="clr-namespace:Avalonia.DrmKit.OnScreenKeyboard;assembly=Avalonia.DrmKit"
    osk:KeyboardOverlay.IsEnabled="True"
    Watermark="Tap to enter text" />
```

Behaviour:

- when a `TextBox` with `KeyboardOverlay.IsEnabled="True"` receives focus,
  a shared on‑screen keyboard panel appears at the bottom of the main window;
- when focus leaves all such `TextBox` controls, the keyboard hides;
- physical keyboard state (EN/RU, CapsLock) is synchronized with the overlay.

Layouts:

- **EN** and **RU (йцукен)**;
- switching: **Alt+Shift** on physical keyboard or button on the overlay;
- CapsLock is synchronized both ways.

---

## Soft cursor & auto‑hide

`UseDrmSoftCursor()` adds an overlay layer that renders a software cursor on top of the window.

Features:

- always visible in DRM even without hardware cursor;
- correct `PointerEntered` / `PointerExited` / `PointerMoved` for Avalonia controls;
- **auto‑hide** after idle (default: 3 seconds) with smooth fade‑out;
- theming for light/dark themes and full customization via styles/templates.

Example style in `App.axaml`:

```xml
<Application.Styles>
  <Style Selector="t|SoftCursorLayer">
    <Setter Property="AutoHideDelay" Value="0:0:3" /> <!-- idle timeout -->
    <Setter Property="StrokeBrush"   Value="White" />
    <Setter Property="FillBrush"     Value="#80FFFFFF" />
    <Setter Property="PressedFillBrush" Value="#C0FFFFFF" />
  </Style>
</Application.Styles>
```

To fully replace visuals (text, image, custom shape), set
`DisableDefaultRendering="True"` and provide your own `Template` or `Content`.

---

## SmartPlatform CLI (very short)

`SmartPlatform` understands several useful command‑line options:

```text
--system=auto|drm|desktop
--drm=card0|card1|/dev/dri/card1|1
--resolution=1280x720
```

Typical DRM run:

```bash
./YourApp --system=drm --drm=card0 --resolution=1920x1080
```

If no DRM card is found (no `/dev/dri/card*`), the library automatically starts
the regular Avalonia desktop backend instead.

---

## Кратко по‑русски

- Пакет для запуска Avalonia в **чистом DRM/Framebuffer**.
- Даёт: экранную клавиатуру‑оверлей, evdev‑клавиатуру, мышь через evdev,
  софт‑курсор с авто‑скрытием и нормальными pointerover‑событиями.
- Интеграция — один вызов `SmartPlatform.Start(...)` + три extension‑метода
  для `AppBuilder`.

---

## Input behavior defaults (current)

On-screen keyboard (`KeyboardOverlay`):

- opens on pointer/touch focus by default;
- does not auto-open from keyboard `Tab` focus by default;
- `Esc` hides overlay without rollback;
- text rollback is only performed by explicit `Cancel` on overlay keyboard.

Useful attached properties:

- `osk:KeyboardOverlay.OpenOnProgrammaticFocus` (default: `False`)
- `osk:KeyboardOverlay.OpenOnKeyboardFocus` (default: `False`)
- `osk:KeyboardOverlay.InputGateTimeoutMs` (default: `300`)

DRM keyboard backend (`DrmKeyboardOptions`) defaults:

- `UseRoutedControlKeys = true`
- `EnableRawControlKeysFallback = false`
- `TabAsNavigation = true`
- `EnterAsTextFallback = false`
