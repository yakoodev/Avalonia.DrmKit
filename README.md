
# Avalonia.DrmKit

Набор утилит для запуска приложений **Avalonia** в Linux **DRM/Framebuffer** без X11/Wayland:
- сырая клава **evdev**: EN/RU, Alt+Shift, CapsLock (двусторонняя синхронизация с экранной);
- **экранная клавиатура** (overlay, без Popup), полностью стилизуемая;
- **софт‑курсор** с fallback мыши на evdev + хит‑тест и «умный клик»;
- авто‑детект и запуск: **DRM** ↔ Desktop (fallback при ошибке).

> Библиотека тестировалась с Avalonia 11.0.x / .NET 8.

## Установка

**NuGet (предпочтительно):**
```bash
dotnet add package Avalonia.DrmKit
```

**Локально из репозитория:**
```bash
dotnet pack ./Avalonia.DrmKit/Avalonia.DrmKit.csproj -c Release -o ./artifacts
dotnet add <ВашПроект> package Avalonia.DrmKit --source ./artifacts
```

## Быстрый старт

В `Program.cs`:

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
            .UseDrmSoftCursor()        // софт‑курсор поверх DRM
            .UseLinuxKeyboardListener()// сырая клава evdev + EN/RU + Alt+Shift + Caps
            .UseEvdevMouseFallback();  // мышь через evdev + хит‑тест
}
```

Запуск в DRM:
```bash
# пользователь должен быть в группах 'video' и 'input'
./YourApp --system=drm --drm=card1 --resolution=1280x720
# отладка
DRMKIT_DEBUG=1 ./YourApp --system=drm --drm=card1
```

## Экранная клавиатура (OSK)

Автопоявление для `TextBox`:
```xml
<TextBox xmlns:osk="clr-namespace:Avalonia.DrmKit.OnScreenKeyboard;assembly=Avalonia.DrmKit"
         osk:KeyboardOverlay.IsEnabled="True"
         Watermark="Коснитесь, чтобы ввести текст"/>
```

Cтили/ресурсы (можно переопределить в `App.axaml`):
- `Osk.Background`, `Osk.Border`
- `Osk.Key.*` — цвета, размеры
- классы: `osk-button`, `active`, `primary`, `wide`

## Требования для Linux DRM

- Пользователь в группах: `video`, `input`.
- Убедитесь, что путь `/dev/dri/cardX` существует (выберите нужную карту через `--drm=card1`).
- Переменная `XDG_RUNTIME_DIR` должна быть валидной для вашего пользователя (библиотека пытается исправить при запуске).

## Отладка

```
DRMKIT_DEBUG=1   # подробные логи evdev/клавиш/мыши
```

## Лицензия

MIT
