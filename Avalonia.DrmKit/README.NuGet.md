
# Avalonia.DrmKit

Утилиты для приложений **Avalonia** под **Linux DRM/Framebuffer**:
- клавиатура evdev (EN/RU, Alt+Shift, CapsLock);
- экранная клавиатура (overlay, стилизуемая, без Popup);
- софт‑курсор + мышь evdev (умный клик/фокус);
- авто‑детект запуска: DRM ↔ Desktop.

## Установка
```bash
dotnet add package Avalonia.DrmKit
```

## Быстрый старт
```csharp
AppBuilder.Configure<App>()
  .UsePlatformDetect()
  .UseReactiveUI()
  .UseDrmSoftCursor()
  .UseLinuxKeyboardListener()
  .UseEvdevMouseFallback();
```

## DRM запуск
```
./YourApp --system=drm --drm=card1 --resolution=1280x720
DRMKIT_DEBUG=1 ./YourApp --system=drm --drm=card1
```
