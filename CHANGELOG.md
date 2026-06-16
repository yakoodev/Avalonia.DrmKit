# Changelog

All notable changes to this project are documented in this file.

The format follows Keep a Changelog and SemVer.

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
