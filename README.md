# DrmKit.Avalonia / Avalonia.DrmKit

Helpers for running **Avalonia** applications on **Linux DRM/Framebuffer**  
with on‑screen keyboard, soft cursor and evdev input.

- NuGet: `DrmKit.Avalonia`
- Main namespace: `Avalonia.DrmKit`
- Target: Linux (DRM/FB, kiosk/embedded)

---

- [Русский](#русский)
- [English](#english)

---

## Русский

### Что это

**DrmKit.Avalonia** — это набор утилит для приложений **Avalonia**, которые работают:

- без X11 и Wayland, напрямую поверх **Linux DRM/Framebuffer**;
- на встраиваемых устройствах, киосках, терминалах, панелях.

Пакет берёт на себя ввод и часть инфраструктуры, которую обычно делает windowing‑система:

- клавиатура через **evdev** (EN/RU, Alt+Shift, CapsLock, текстовый ввод);
- **экранная клавиатура‑оверлей** (`KeyboardOverlay`) для управления с тачскрина;
- **софт‑курсор** поверх окна, с авто‑скрытием и корректным `pointerover`;
- **fallback‑мышь через evdev** для DRM;
- `SmartPlatform` — единая точка входа, которая сама решает, запускать ли приложение
  в DRM или в обычном desktop‑режиме.

### Основные возможности

- **SmartPlatform**
  - auto‑detect: DRM ↔ desktop (X11/Wayland);
  - CLI‑аргументы: `--system=drm|desktop|auto`, `--drm=...`, `--resolution=...`;
  - если DRM не поднялся, аккуратный fallback в обычный Avalonia Desktop.

- **Клавиатура (evdev)**
  - EN / RU (в будущем добавлю кастомизацию);
  - переключение **Alt+Shift** (в любом порядке);
  - двусторонняя синхронизация **CapsLock**;
  - генерация `KeyDown` / `KeyUp` + `TextInput` в сфокусированный элемент.

- **Экранная клавиатура‑оверлей**
  - общий overlay‑слой внизу окна;
  - включается по аттачу `KeyboardOverlay.IsEnabled="True"` на `TextBox`;
  - понимает EN/RU, Caps, спец‑клавиши (`Backspace`, `Enter`, `Space`, и т.д.);
  - не крадёт фокус у `TextBox`, работает как продолжение физической клавиатуры.

- **Софт‑курсор**
  - рисует курсор поверх клиентской области окна;
  - корректно обрабатывает `PointerEntered` / `PointerExited` / `PointerMoved`;
  - **auto‑hide** по таймауту с плавным исчезновением;
  - стилизуется через ресурсы Avalonia (кисти, template, content).

- **Мышь через evdev**
  - читает `/dev/input/event*`;
  - двигает софт‑курсор;
  - генерирует события `PointerPressed` / `PointerReleased` / `PointerMoved`
    для контролов Avalonia.

### Установка

```bash
dotnet add package DrmKit.Avalonia
```

Требования:

- Linux с устройствами `/dev/dri/card*` (DRM) и `/dev/input/event*`;
- пользователь в группах `video` и `input`;
- .NET 8, Avalonia 11 (рекомендуется).

### Быстрый старт

**Program.cs**:

```csharp
using Avalonia;
using Avalonia.DrmKit;

internal static class Program
{
    public static int Main(string[] args)
        => SmartPlatform.Start(BuildAvaloniaApp(), args); // авто-определение платформы

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI()
            .UseDrmSoftCursor()         // софт-курсор поверх DRM
            .UseLinuxKeyboardListener() // evdev-клава: EN/RU, Alt+Shift, CapsLock
            .UseEvdevMouseFallback();   // мышь через evdev + pointerover fix
}
```

Запуск в DRM‑режиме:

```bash
# пользователь должен быть в группах 'video' и 'input'
./YourApp --system=drm --drm=card0 --resolution=1920x1080

# подробный лог по вводу
DRMKIT_DEBUG=1 ./YourApp --system=drm --drm=card0
```

Если DRM не удаётся инициализировать (нет `/dev/dri/card*` или ошибка драйвера),
`SmartPlatform` автоматически запустит обычный Avalonia‑backend.

### SmartPlatform и аргументы командной строки

Поддерживаемые опции (кратко):

```text
--system=auto|drm|desktop
--drm=card0|card1|/dev/dri/card1|1
--resolution=1280x720
```

Примеры:

```bash
# Явно просим DRM на первой карте
./YourApp --system=drm --drm=card0 --resolution=1280x720

# Автоматический выбор, но навязываем разрешение
./YourApp --resolution=1024x600
```

Если карта не указана, берётся первая доступная `/dev/dri/card*`.

### Экранная клавиатура‑оверлей

Подключение в XAML:

```xml
<Window
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:osk="clr-namespace:Avalonia.DrmKit.OnScreenKeyboard;assembly=Avalonia.DrmKit">

  <Grid>
    <TextBox
        osk:KeyboardOverlay.IsEnabled="True"
        Watermark="Коснитесь, чтобы ввести текст" />

    <!-- остальной UI -->
  </Grid>
</Window>
```

Поведение:

- при фокусе любого `TextBox` с `KeyboardOverlay.IsEnabled="True"`
  снизу окна показывается общая панель экранной клавиатуры;
- когда фокус уходит со всех таких `TextBox`, клавиатура автоматически скрывается;
- `Enter` на экранной клавиатуре закрывает её и снимает фокус (по желанию
  можно изменить в коде под свои сценарии).

Раскладки:

- **EN** / **RU**;
- переключение: `Alt+Shift` (физическая клавиатура) + кнопка на самой клавиатуре;
- CapsLock синхронизирован с физическим.

### Софт‑курсор

`UseDrmSoftCursor()` добавляет в визуальное дерево слой `SoftCursorLayer`.

Возможности:

- курсор рисуется поверх любого контента приложения;
- события `PointerEntered` / `PointerExited` корректно доходят до контролов;
- курсор автоматически скрывается через `AutoHideDelay` (по умолчанию ~3 секунды)
  и плавно исчезает.

Пример базовой стилизации (App.axaml):

```xml
<Application.Styles>
  <Style Selector="t|SoftCursorLayer">
    <!-- таймаут простоя -->
    <Setter Property="AutoHideDelay" Value="0:0:3" />

    <!-- базовые кисти -->
    <Setter Property="StrokeBrush"   Value="White" />
    <Setter Property="FillBrush"     Value="#80FFFFFF" />
    <Setter Property="PressedFillBrush" Value="#C0FFFFFF" />
  </Style>
</Application.Styles>
```

Полная кастомизация визуала:

- установить `DisableDefaultRendering="True"`;
- задать `Template` / `Content` (например, стрелку, изображение, иконку).

### Мышь через evdev

`UseEvdevMouseFallback()`:

- ищет устройства, которые ведут себя как мышь (`EV_REL` + кнопки);
- двигает soft cursor;
- выполняет хит‑тест по визуальному дереву и генерирует события:

  - `PointerMoved`;
  - `PointerPressed` / `PointerReleased` (левая кнопка).

При этом слой `SoftCursorLayer` исключается из хит‑теста, чтобы `:pointerover`
работал по реальным контролам, а не по оверлею.

### Известные ограничения и планы

- Ориентир — Linux DRM/Framebuffer; под чистым desktop‑режимом большинство
  возможностей не нужно (но и не мешает).
- Popup‑версия экранной клавиатуры может быть удалена в будущих версиях;
  рекомендованный способ работы — только `KeyboardOverlay`.
- Раскладки по умолчанию — только EN/RU; пользовательские раскладки пока не
  вынесены в отдельный API.

---

## English

### What is it?

**DrmKit.Avalonia** is a helper library for **Avalonia** applications that run:

- directly on **Linux DRM/Framebuffer** (no X11 / no Wayland),
- on embedded devices, kiosks and terminals.

It provides the missing input layer you usually get from a windowing system:

- keyboard input via **evdev** (EN/RU, Alt+Shift, CapsLock, text input);
- an **on‑screen keyboard overlay** (`KeyboardOverlay`) for touch usage;
- a **software cursor** overlay with auto‑hide and correct pointerover events;
- an **evdev mouse fallback** for DRM;
- `SmartPlatform` — single entry point that decides between DRM and desktop at runtime.

### Features (short)

- SmartPlatform (DRM / desktop detection, CLI options, graceful fallback).
- evdev keyboard (EN/RU, Alt+Shift, CapsLock sync, text input).
- On‑screen keyboard overlay for `TextBox`.
- Soft cursor overlay with auto‑hide and proper hit‑testing.
- evdev mouse fallback for DRM.

### Install & quick start

```bash
dotnet add package DrmKit.Avalonia
```

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
            .UseDrmSoftCursor()
            .UseLinuxKeyboardListener()
            .UseEvdevMouseFallback();
}
```

Run in DRM:

```bash
./YourApp --system=drm --drm=card0 --resolution=1920x1080
DRMKIT_DEBUG=1 ./YourApp --system=drm --drm=card0
```

Attach the on‑screen keyboard overlay:

```xml
<TextBox
    xmlns:osk="clr-namespace:Avalonia.DrmKit.OnScreenKeyboard;assembly=Avalonia.DrmKit"
    osk:KeyboardOverlay.IsEnabled="True" />
```

For more detailed Russian description (including styling and CLI options),  
see the [Русский](#русский) section above.
