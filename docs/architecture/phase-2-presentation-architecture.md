# Phase 2 — Presentation architecture and navigation

Status: Phase 2 foundation implemented; page extraction and view-model isolation remain in progress. This document distinguishes the compatibility bridge from the final target architecture.

## Current baseline

The current WPF startup now creates one `IHost` with `Host.CreateDefaultBuilder` in `App.xaml.cs`. Core services and presentation services are registered into that host; startup migration, legacy JSON migration, diagnostics and shutdown are kept under the host lifetime.

The window is bound to `ShellViewModel`. `ShellViewModel` uses `INavigationService` / `NavigationService`, whose typed `StudyRoute` keys do not depend on WPF. The existing tab content is temporarily exposed as `LegacyContent` and remains the data context of the tab control. This is a deliberate migration bridge, not the final page isolation.

The seven navigation sections still live in `MainWindow.xaml`; their bindings still route through `MainViewModel` and `StudyWorkspaceViewModel`. Do not add new page features to either monolith while Phase 2 is underway.

## Target shell

- Keep one `IHost` for the WPF application's lifetime; do not create a second service provider.
- Register core/domain services once, then register presentation services through `AddUniToStudyHubPresentation`.
- Keep `MainWindow` bound only to `ShellViewModel`; remove the temporary `LegacyContent` bridge after the page migration.
- Keep `INavigationService` / `NavigationService` independent from any WPF Window or control.
- Replace the temporary tab-host with a shell `ContentControl` whose active page view model comes from navigation state.
- Use application-level DataTemplates to map page view model types to their `UserControl` views. Page views must not reach into `MainWindow.DataContext`.

## Page ownership

Each page gets its own view model and view. Register one view-model instance per page for the lifetime of the single application window, so changing routes does not discard unsaved UI state.

| Route | View / ViewModel | Primary responsibility |
| --- | --- | --- |
| Today | `DashboardView` / `DashboardViewModel` | Career summary, next planned exam, quick navigation |
| Courses | `CoursesView` / `CoursesViewModel` | Courses, CFU, exam attempts and recorded grades |
| Calendar | `CalendarView` / `CalendarViewModel` | Local calendar, planned exams and imported Moodle events |
| Study Lab | `StudyLabView` / `StudyLabViewModel` | Focus, tasks, flashcards, errors, distractions and energy check-ins |
| Academy | `AcademyView` / `AcademyViewModel` | Local PDFs, syllabus, question bank and readiness |
| Progress | `ProgressView` / `ProgressViewModel` | Study metrics, export and career What-If projections |
| Settings | `SettingsView` / `SettingsViewModel` | Academic profile, Moodle connection/sync and encrypted backups |

## Dependency injection boundaries

- Page view models depend on the smallest relevant application/domain services; they do not depend on `MainViewModel`, `StudyWorkspaceViewModel`, `MainWindow`, or another page view model.
- Keep `CareerCalculatorService` and `CareerApplicationService` in the shared domain composition. The What-If projection UI belongs to the Progress route; calculations remain in the existing career service.
- Moodle API/token handling stays in the existing Moodle application service. Settings owns connection and sync commands; Courses and Calendar display the resulting persisted records from their own query/session abstractions.
- Introduce a small shared study-data session/query boundary only if needed to prevent every page from independently loading and overwriting the same aggregate. It must use the existing `IStudyDataStore` as the sole persistence authority.
- Do not pass mutable application state through navigation parameters. Pass stable identifiers where a route needs a selected course/event; resolve the data through its page service.
- Keep presentation-only navigation/status state in the shell. Business state remains in domain/application services and the persisted study aggregate.

## Migration order

1. **Implemented foundation:** introduce HostBuilder-based composition, `ShellViewModel`, typed route keys and `NavigationService`; keep the legacy tab content hosted temporarily so startup and all commands continue working.
2. Extract the seven existing page regions into separate `UserControl` files without changing their bindings first.
3. Move each page's bindings and commands into its specific view model, migrating one route at a time. Split the current Study Lab / Academy / Progress / Settings responsibilities out of `StudyWorkspaceViewModel`; do not leave that 1,200-line monolith behind.
4. Replace the temporary tab host with shell content templates, then remove the legacy view models and old window-level navigation handlers only after every route uses its page-specific view model.

## Acceptance tests

- DI composition test: resolve the shell, navigation service and current compatibility view model without constructing a Window.
- Navigation test: route changes are typed, invalid routes are rejected, and the selected sidebar index remains synchronized.
- Final migration tests: every route resolves to the expected page view model, and returning to a route preserves its page state.
- Binding/command tests per page, including Moodle sync and What-If projections.
- Persistence regressions for SQLite, calendar events, PDF metadata, focus/task state, profile and backups.
- Windows CI: test suite, Release WPF build, self-contained win-x64 publish and startup smoke test.
- Security CI: secret scanning and blocking dependency audit.

## Explicit non-goals

Phase 2 must not redesign the SQLite schema, change SQLCipher/DPAPI/AES backup formats, replace Moodle business logic, or introduce remote AI calls. Preserve the verified Phase 1 visual system through shared application resources and the same design tokens.
