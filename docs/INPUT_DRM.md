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

## Touch options (`DrmTouchOptions`)

Gesture tuning:

- `TapMaxDistancePx = 10`
- `TapMaxDurationMs = 220`
- `MoveJitterThresholdPx = 1.5`
- `EnableTapSwipeSeparation = true`

Orientation and calibration (for panels mounted rotated/mirrored relative to the
display, or with broken `EVIOCGABS` reporting):

| Option | Env variable | Meaning |
| --- | --- | --- |
| `SwapXY` | `DRMKIT_TOUCH_SWAP_XY` | Swap raw X/Y axes before mapping (raw X drives screen Y and vice versa). |
| `InvertX` | `DRMKIT_TOUCH_INVERT_X` | Invert the final screen X coordinate (after normalization/swap). |
| `InvertY` | `DRMKIT_TOUCH_INVERT_Y` | Invert the final screen Y coordinate (after normalization/swap). |
| `AbsXMin`/`AbsXMax` | `DRMKIT_TOUCH_ABS_X_MIN`/`_MAX` | Explicit raw range of the device X axis (before swap). |
| `AbsYMin`/`AbsYMax` | `DRMKIT_TOUCH_ABS_Y_MIN`/`_MAX` | Explicit raw range of the device Y axis (before swap). |

Environment variables always win over values set in code, so a specific unit can be
tuned without rebuilding the application. Boolean variables accept `1/0`, `true/false`,
`yes/no`, `on/off`. Axis overrides require both min and max, with max greater than min.

Axis range precedence: explicit override → kernel `EVIOCGABS` → runtime observation.
While an axis has no usable range yet (ioctl failed, no override, and not enough
observed samples), position updates are dropped and a warning is logged once per axis —
this prevents garbage coordinates on the first touches. If you see that warning on
every boot, set the axis overrides.

Example for a 90°-rotated panel with a broken ioctl:

```bash
DRMKIT_TOUCH_SWAP_XY=1 \
DRMKIT_TOUCH_INVERT_Y=1 \
DRMKIT_TOUCH_ABS_X_MIN=0 DRMKIT_TOUCH_ABS_X_MAX=4095 \
DRMKIT_TOUCH_ABS_Y_MIN=0 DRMKIT_TOUCH_ABS_Y_MAX=4095 \
./YourApp --system=drm
```

Robustness notes:

- Kernel `SYN_DROPPED` events trigger a standard evdev resync: the partial packet and
  everything up to and including the next `SYN_REPORT` are discarded. An active touch is
  cancelled so a missed lift cannot leave it stuck down.
- The frame carrying the touch end bypasses the jitter threshold, so `TouchEnd` always
  fires at the true final finger position.
- Touch gesture state is tracked per device.

## Multitouch

Devices reporting evdev multitouch **protocol B** (`ABS_MT_SLOT`, e.g. the WaveShare
WS170120 kiosk panel) get full multi-contact support:

- Contacts are tracked per slot. `ABS_MT_SLOT` switches the current slot,
  `ABS_MT_TRACKING_ID >= 0` begins a contact (-1 ends it), `ABS_MT_POSITION_X/Y` update it.
  All changes are buffered and committed on `SYN_REPORT`, one touch event per changed
  contact (`TouchBegin`/`TouchUpdate`/`TouchEnd`). The Avalonia touch point id is the
  kernel tracking id, stable for the contact's lifetime.
- Slot capacity comes from `EVIOCGABS(ABS_MT_SLOT)` (maximum + 1); if the ioctl reports
  nothing, protocol B is detected from the first `ABS_MT_SLOT` event with a fallback of
  10 slots (growing on demand).
- Single-touch emulation (`ABS_X`/`ABS_Y`/`BTN_TOUCH`) is ignored on MT-slot devices —
  it duplicates the first contact. Devices **without** `ABS_MT_SLOT` keep the plain
  single-touch path described above.
- Gesture passthrough: tap classification and `MoveJitterThresholdPx` suppression apply
  only while exactly one contact is active. The moment a second contact begins, all
  updates are delivered raw so Avalonia's gesture recognizers (pinch/rotate) receive
  unfiltered points.
- `SYN_DROPPED` cancels **all** active contacts (`TouchCancel` per contact) and resets
  slot state before the resync re-arms.
- Only the first (primary) contact moves the shared soft cursor; secondary contacts
  never warp it.

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
