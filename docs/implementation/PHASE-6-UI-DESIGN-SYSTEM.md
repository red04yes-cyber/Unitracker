# Phase 6 — Apple Premium UI foundation

## Scope of this slice

- Introduces a shared dark-first palette with coordinated light and high-contrast color mappings.
- Adds semantic tokens for page, sidebar, panel, elevated panel, inset input, focus, hover, success, warning, danger and information states.
- Adds reusable WPF control templates for buttons, text/password fields, combo boxes, date picker/calendar, check/radio controls, sliders, progress bars, lists, tables, expanders and tooltips.
- Adds a small presentation-only motion behavior for cancellable hover/entrance transitions.
- Makes Dark the startup theme unless Windows High Contrast is active; adds an explicit Dark/Light switch in the shell.
- Keeps business logic, persistence, services and ViewModels unchanged.

## Animation dependency

The chosen dependency is AnimationRx.Wpf.Reactive 3.2.0 (MIT). It exposes the WPF extension API under CP.AnimationRx.Reactive and targets .NET 10 Windows 10.0.19041.0. The app target framework is therefore set to net10.0-windows10.0.19041.0, making Windows 10 build 19041 (May 2020) the declared minimum API baseline. This is above the former generic WPF baseline; verify the supported OS range against the distribution target before release.

The library is used only in the presentation layer. Animation subscriptions are cancelled when an element unloads or a new hover animation replaces the previous one. No Rx types are introduced into ViewModels or domain services.

## Verification

- The existing test suite passed: 292/292.
- The first WPF build exposed an incorrect namespace assumption and an incompatible target-framework fallback. Those findings are being corrected in this slice; do not treat the first build as successful.
- A clean Windows Release build, publish, smoke test and package audit are still required after the correction.
- No manual visual verification on a physical Windows desktop has been performed.

## Next slice

Complete the shell redesign: compact/expandable pinned sidebar, adaptive window layout and persistent window bounds. Then refine the dashboard and each of the seven top-level sections without changing their bindings or command flows.
