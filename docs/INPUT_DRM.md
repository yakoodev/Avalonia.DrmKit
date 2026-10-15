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
  cancelled so a missed lift cannot leave it stuck down. On multitouch devices the backend
  then recovers held contacts — see "SYN_DROPPED recovery" below.
- The frame carrying the touch end bypasses the jitter threshold, so `TouchEnd` always
  fires at the true final finger position.
- Touch gesture state is tracked per device.

## Touch feedback overlay

Every active touch contact gets a visual indicator: a semi-transparent light circle with a
brighter ring (theme-agnostic — readable on dark kiosk UIs and light themes alike). It
appears on `TouchBegin`, follows the finger, and fades out quickly (~200 ms fade +
slight scale-up) after `TouchEnd`. `TouchCancel` (resync, device removal) removes
indicators immediately.

| Option | Env variable | Default | Meaning |
| --- | --- | --- | --- |
| `ShowTouchFeedback` | `DRMKIT_TOUCH_FEEDBACK` | `true` | Show the per-contact indicator overlay. |
| `HideCursorWhileTouching` | `DRMKIT_TOUCH_HIDE_CURSOR` | `true` | Hide the soft cursor while any contact is active; it reappears on the next real mouse/relative input. |
| `TouchFeedbackSizePx` | `DRMKIT_TOUCH_FEEDBACK_SIZE` | `44` | Indicator diameter in pixels. |
| `TouchFeedbackOpacity` | `DRMKIT_TOUCH_FEEDBACK_OPACITY` | `0.38` | Fill opacity (0..1); the ring is drawn slightly brighter. |

The overlay attaches to the TopLevel that hosts the DRM input root (the same host panel the
soft cursor uses), is not hit-test visible, and is fully inert when disabled. Its animation
timer (~60 Hz) runs only while an indicator is fading; steady-state rendering performs no
per-frame allocations.

## Turning the soft cursor off

On touch-only kiosks the soft cursor only gets in the way. It can be disabled:

| Where | How | Default |
| --- | --- | --- |
| `SoftCursorOptions.Enabled` (passed to `UseDrmSoftCursor`) | initial state | `true` |
| `DRMKIT_SOFT_CURSOR` env variable | `0`/`false`/`off` or `1`/`true`/`on`; overrides the option | unset |
| `DrmSoftCursor.IsEnabled` | switch at runtime, from any thread | — |

While disabled the cursor stays hidden and mouse movement does not bring it back. The cursor
layer is still installed, so turning it back on needs no restart.

## Device hotplug

`/dev/input` is watched with inotify (`IN_CREATE`/`IN_DELETE`/`IN_ATTRIB`) from a single
background thread:

- A new `event*` node starts a reader thread through the same initialization path as at
  startup (axis calibration, MT slot probing). Opening retries a few times over ~1 s, since
  a just-created node may get its permissions from udev a moment later (`IN_ATTRIB`
  retriggers the attempt).
- A removed node stops its reader, cleans up its state and cancels its active contacts
  (`TouchCancel` per contact), so nothing stays stuck down. A reader whose device vanished
  exits cleanly (no error-log spinning).
- If inotify cannot be initialized, the failure is logged once and the backend keeps the
  device set found at startup.

## SYN_DROPPED recovery (EVIOCGMTSLOTS)

When the kernel drops events (`SYN_DROPPED`), contacts the app knows about may be stale, so
they are all cancelled. On MT protocol B devices the backend then queries the kernel's
current per-slot state via `EVIOCGMTSLOTS` (`ABS_MT_TRACKING_ID`, `ABS_MT_POSITION_X/Y`)
and re-announces contacts that are physically still down as `TouchBegin` at their current
positions — a held finger keeps dragging through a resync instead of going dead until it is
lifted and re-touched. The cancel + re-begin pair is delivered in one dispatcher batch. If
the ioctl fails, behavior falls back to cancel-only (as before 12.0.7).

## Event dispatch

All raw events produced by one evdev `SYN_REPORT` frame (e.g. moves/updates of several
contacts) are posted to the UI thread as a single dispatcher work item and delivered in
emission order — one post per frame instead of one per event. Relative mouse motion is
coalesced the same way (one move per packet), with an early flush before button/wheel
events so ordering is preserved. Nothing is throttled or dropped.

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
- `SYN_DROPPED` cancels **all** active contacts (`TouchCancel` per contact), resets slot
  state, and re-announces physically held contacts from the kernel state (see
  "SYN_DROPPED recovery" above) before the resync re-arms.
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
