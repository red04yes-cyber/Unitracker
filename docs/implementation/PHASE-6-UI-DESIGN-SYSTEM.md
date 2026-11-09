# Phase 6 — Apple Premium UI foundation

## Implemented in the foundation slice

- Dark-first semantic palette with coordinated Light and Windows High Contrast mappings.
- Reusable WPF templates for buttons, text/password fields, combo boxes, date picker/calendar, check/radio controls, sliders, progress bars, lists, tables, expanders and tooltips.
- Dark and Light theme switch; selected theme is stored with non-sensitive UI preferences.
- Motion behavior for cancellable hover lift and card entrance transitions.
- Compact/expandable sidebar with hover expansion and optional pinning.
- Custom line-icon navigation, contextual topbar title/subtitle and persistent window size/position.
- Business logic, persistence schema, services and ViewModels are not changed.

## Animation dependency

AnimationRx.Wpf.Reactive 3.2.0 (MIT) is used only in the presentation layer. Its WPF extension API is exposed under CP.AnimationRx.Reactive and targets .NET 10 Windows 10.0.19041.0. The app target is therefore net10.0-windows10.0.19041.0, declaring Windows 10 build 19041 (May 2020) as the minimum API baseline. The package and framework choice should be retained only if this baseline is acceptable for the distribution target.

## Verification

- Latest completed Windows workflow: 292/292 tests passed.
- Release build passed with 0 warnings and 0 errors.
- win-x64 self-contained publish passed.
- WPF startup smoke passed; the window was shown and the local database initialized.
- Portable ZIP and SHA-256 artifact created.
- Automated smoke is not a substitute for manual visual/interactive verification on a real Windows desktop.

## Remaining work

- Premium custom modal dialogs with keyboard/default/cancel semantics, animated entrance/exit and expandable technical details.
- Refine dashboard hierarchy, progress rings/charts, rich table status badges and all seven sections.
- Responsive breakpoints and keyboard navigation audit.
- Manual visual and interaction verification on Windows.
