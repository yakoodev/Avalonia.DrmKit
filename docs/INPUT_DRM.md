# DRM Input Notes

This document describes the current DRM input model used by `Avalonia.DrmKit`.

## Runtime prerequisites (Linux)

- User must be in groups: `video`, `input`
- For non-root runs, ensure a valid runtime dir:
  - `XDG_RUNTIME_DIR=/run/user/<uid>`
- Devices expected:
  - DRM: `/dev/dri/card*`
  - Input: `/dev/input/event*`

Example group setup:

```bash
sudo usermod -aG video,input $USER
newgrp video
newgrp input
```

Log out/in after changing groups.

## Keyboard path in DRM mode

- Physical keyboard events are read from evdev.
- Printable characters are sent through XKB-backed `TextInput`.
- Navigation/control keys are routed as key events.
- Layout switching (EN/RU) is supported and synchronized with OSK.

## Keyboard options (`DrmKeyboardOptions`)

- `UseRoutedControlKeys = true`
- `EnableRawControlKeysFallback = false`
- `TabAsNavigation = true`
- `EnterAsTextFallback = false`

These defaults are intended for predictable `TextBox` behavior in kiosk flows.

## On-screen keyboard (`KeyboardOverlay`) behavior

Default behavior:

- Opens on pointer/touch focus.
- Does not auto-open on keyboard `Tab` focus.
- `Esc` closes overlay without rollback.
- Rollback is performed only by explicit `Cancel` on overlay.

Attached properties:

- `KeyboardOverlay.OpenOnProgrammaticFocus` (default `False`)
- `KeyboardOverlay.OpenOnKeyboardFocus` (default `False`)
- `KeyboardOverlay.InputGateTimeoutMs` (default `300`)

## Debugging

Enable verbose DRM input logs:

```bash
DRMKIT_DEBUG=1 ./YourApp --system=drm
```

This prints device discovery, calibration, keyboard capability detection, and key translation details.
