# Changelog

All notable changes to this project are documented in this file.

The format follows Keep a Changelog and SemVer.

## [12.0.7] - 2026-07-10
### Added
- Multi-contact touch feedback overlay: a semi-transparent circle per active contact appears on
  `TouchBegin`, follows `TouchUpdate` and fades out (~200 ms, fade + slight scale-up) after
  `TouchEnd`; `TouchCancel` clears indicators immediately. Theme-agnostic (light disc + brighter
  ring), attached to the same TopLevel host panel as the soft cursor, near-zero per-frame
  allocations, and the ~60 Hz animation timer runs only while a fade is in progress.
  - New `DrmTouchOptions`: `ShowTouchFeedback` (default `true`), `TouchFeedbackSizePx` (44),
    `TouchFeedbackOpacity` (0.38), with env overrides `DRMKIT_TOUCH_FEEDBACK`,
    `DRMKIT_TOUCH_FEEDBACK_SIZE`, `DRMKIT_TOUCH_FEEDBACK_OPACITY`.
- Soft cursor hiding while touching: `HideCursorWhileTouching` (default `true`, env
  `DRMKIT_TOUCH_HIDE_CURSOR`) hides the soft cursor while any touch contact is active; it
  reappears on the next real mouse/relative input.
- `SYN_DROPPED` recovery via `EVIOCGMTSLOTS`: after cancelling all contacts, the kernel per-slot
  state (tracking ids + positions) is queried and contacts that are physically still down are
  re-announced as `TouchBegin` at their current positions — held fingers keep working through a
  resync instead of staying dead until re-touch. On ioctl failure the previous cancel-only
  behavior applies.
- `/dev/input` hotplug: an inotify watcher (`IN_CREATE`/`IN_DELETE`/`IN_ATTRIB`, single watcher
  thread) starts a reader for new `event*` nodes (same init path as startup, including axis
  calibration, with open retries over ~1 s for permission races) and stops/cleans up readers of
  removed nodes, cancelling their active contacts. If inotify is unavailable the failure is
  logged once and the startup device set is kept.

### Changed
- All events produced by one evdev `SYN_REPORT` frame are dispatched as a single
  `Dispatcher.UIThread.Post` (in order, Input priority) instead of one post per event; relative
  mouse motion is accumulated per packet and emitted as one move at the frame boundary (flushed
  early before button/wheel events so ordering is preserved). Nothing is throttled or dropped.

### Fixed
- Keyboard scan-code mapping: evdev code 102 (`KEY_HOME`) was mapped to Right Ctrl, and End,
  PageUp, PageDown and Insert were not mapped at all — on a physical keyboard Home/End did not
  work in text boxes. Codes now follow `linux/input-event-codes.h` (Right Ctrl is 97), plus
  keypad Enter/Divide/Multiply, Scroll Lock, Print Screen, Pause and the Windows/Menu keys;
  the physical-key map got the same fix.
- On-screen keyboard keys were see-through on hover/press: the pressed style set `Opacity`
  0.8 and the Fluent theme replaced the key background with its own semi-transparent brush, so
  the screen behind the keyboard showed through the key. Hover/pressed now use opaque
  `Osk.KeyHoverBackground` / `Osk.KeyPressedBackground` (and `Osk.PrimaryKey*` variants) set
  on the template part; the dark-theme keyboard background is opaque as well.
- A reader thread whose device node vanished no longer spins on zero-byte reads or logs periodic
  read errors; it exits cleanly and cancels the device's remaining contacts.

## [12.0.6] - 2026-07-10
### Added
- Evdev multitouch protocol B support in `DrmUnifiedInputBackend`:
  - Per-slot contact tracking (`ABS_MT_SLOT`, `ABS_MT_TRACKING_ID`, `ABS_MT_POSITION_X/Y`),
    buffered per evdev packet and committed on `SYN_REPORT` as `TouchBegin`/`TouchUpdate`/`TouchEnd`
    with stable per-contact ids (kernel tracking ids). MT axes use their own calibration and the
    existing `SwapXY`/`InvertX`/`InvertY` mapping; the uncalibrated-axis drop rule applies per axis.
  - Slot capacity is read from `EVIOCGABS(ABS_MT_SLOT)` (maximum + 1), with a fallback of 10 slots
    (growing on demand) when the ioctl reports nothing and protocol B is detected at runtime.
  - Single-touch emulation events (`ABS_X`/`ABS_Y`/`BTN_TOUCH`) are ignored on devices that report
    MT slots — they duplicate the first contact. Devices without `ABS_MT_SLOT` keep the previous
    single-touch behavior unchanged (tap/swipe/jitter logic included).
  - Tap classification and move-jitter suppression apply only while exactly one contact is active;
    from the moment a second contact begins, updates are delivered raw so Avalonia's pinch/gesture
    recognizers receive unfiltered points. The final position is still flushed before `TouchEnd`.
  - `SYN_DROPPED` resync now cancels **all** active contacts (`TouchCancel` per contact) and resets
    the slot state before re-arming.
  - Only the first (primary) contact drives the shared soft-cursor position; secondary contacts
    never warp it.
- `MultiTouchSlotTracker` unit tests: two-finger sequences, mid-frame slot switching, tracking id
  reuse, single-slot frames, `SYN_DROPPED` cancellation, slot-count fallback and growth, deferred
  begin, lift-frame position flush, primary-contact rules.

## [12.0.5] - 2026-07-09
### Added
- Touch orientation and calibration options in `DrmTouchOptions`:
  - `SwapXY` — swap raw X/Y axes for panels mounted rotated relative to the display.
  - `InvertX` / `InvertY` — invert the final screen coordinate (applied after normalization).
  - `AbsXMin`/`AbsXMax`/`AbsYMin`/`AbsYMax` — explicit raw axis range overrides for panels
    where the `EVIOCGABS` ioctl fails or reports a wrong range.
- Environment variable overrides for all of the above (win over code configuration):
  `DRMKIT_TOUCH_SWAP_XY`, `DRMKIT_TOUCH_INVERT_X`, `DRMKIT_TOUCH_INVERT_Y`,
  `DRMKIT_TOUCH_ABS_X_MIN`, `DRMKIT_TOUCH_ABS_X_MAX`, `DRMKIT_TOUCH_ABS_Y_MIN`, `DRMKIT_TOUCH_ABS_Y_MAX`.
- `Avalonia.DrmKit.Tests` (xunit) covering coordinate mapping, axis calibration precedence,
  environment overrides, and the SYN_DROPPED resync state machine; wired into CI before packing.

### Fixed
- Axis calibration precedence is now: explicit override → kernel `EVIOCGABS` → runtime observation.
  While an axis has no usable range, position updates are dropped with a one-time warning instead of
  emitting raw values as screen coordinates (first touches no longer land at wrong positions).
- `SYN_DROPPED` is now handled: the partial evdev packet and everything up to and including the next
  `SYN_REPORT` are discarded (standard resync), so stale half-frames no longer commit as jumped touches.
  An active touch is cancelled (`TouchCancel`) so a missed lift cannot leave the touch stuck down.
- The frame that carries the touch end now bypasses the move jitter threshold, so `TouchEnd` is
  preceded by an update at the true final position instead of a stale point.
- Touch gesture state moved from shared backend fields into per-device state, so two touch-capable
  devices can no longer corrupt each other's gesture tracking.
- `SYN_MT_REPORT` (and other non-`SYN_REPORT` sync codes) no longer commit frames early.

## [12.0.4] - 2026-06-16
### Changed
- Updated Avalonia packages to 12.0.4.
- Replaced `Avalonia.ReactiveUI` with `ReactiveUI.Avalonia`.
- Removed the unused sample `Avalonia.Diagnostics` package reference.
- Updated sample `TextBox.Watermark` usage to `PlaceholderText`.

### Fixed
- Fixed Avalonia 12 compatibility for ReactiveUI app builder setup.
- Fixed focus clearing for the on-screen keyboard using the Avalonia 12 focus API.

## [1.0.0] - 2026-02-27
### Added
- Official DRM-first input path for physical keyboard text input using XKB-backed `TextInput` flow.
- Runtime debug instrumentation for DRM input (`DRMKIT_DEBUG=1`) including evdev discovery, calibration, and key translation logs.
- Configurable keyboard behavior via `DrmKeyboardOptions`:
  - `UseRoutedControlKeys`
  - `EnableRawControlKeysFallback`
  - `TabAsNavigation`
  - `EnterAsTextFallback`
- On-screen keyboard behavior controls:
  - `KeyboardOverlay.OpenOnProgrammaticFocus`
  - `KeyboardOverlay.OpenOnKeyboardFocus`
  - `KeyboardOverlay.InputGateTimeoutMs`
- Linux runtime setup guidance for non-root use (`video`, `input`, and session/runtime requirements).

### Changed
- Reworked keyboard event pipeline for DRM mode so text is entered via `TextInput` while navigation keys stay routed.
- Improved physical keyboard compatibility in DRM mode (stable typing in focused `TextBox`, including language switch support).
- Updated overlay keyboard focus policy to avoid automatic opening on pure keyboard navigation unless explicitly enabled.
- Refined overlay keyboard lifecycle around focus transitions to reduce conflicts when cycling focus with `Tab`.
- Updated sample behavior and defaults documentation to match current input model.

### Fixed
- Fixed soft keyboard startup state where overlay could appear too early.
- Fixed layout conflict where target `TextBox` could be hidden by the on-screen keyboard.
- Fixed logical tree host/overlay attach instability that caused runtime exceptions on focus.
- Fixed DRM touch/pointer coordinate handling inconsistencies.
- Fixed cancel/rollback semantics so rollback happens only on explicit overlay Cancel action.
- Fixed multiple regressions where keyboard and overlay interactions could clear or restore text unexpectedly.

## [0.1.0] - 2025-11-06
### Added
- Initial public release: DRM startup, evdev keyboard/mouse, on-screen keyboard, and soft cursor.
