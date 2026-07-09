# Changelog

All notable changes to this project are documented in this file.

The format follows Keep a Changelog and SemVer.

## [Unreleased]
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
