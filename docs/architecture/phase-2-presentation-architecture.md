# Phase 2 — Presentation architecture and navigation

Status: design contract for the next isolated implementation phase. This document does not claim that the current presentation layer has already been migrated.

## Current baseline

The current WPF startup builds a bare `ServiceCollection` in `App.xaml.cs`. Presentation DI registers only `MainViewModel` and `StudyWorkspaceViewModel`. The seven navigation sections still live in `MainWindow.xaml`; their bindings route through those two large view models.

The existing composition is a compatibility baseline for Phase 1. Do not add further page features to either monolith while Phase 2 is underway.

## Target shell

- Introduce `Microsoft.Extensions.Hosting` and create one `IHost` during WPF startup.
- Keep database migration, legacy migration, diagnostics and shutdown under the host lifetime. Do not create a second service provider.
- Register core/domain services once, then register presentation services through a dedicated `AddUniToStudyHubPresentation` extension.
- Replace the tab-hosting `MainWindow` with a shell bound only to `ShellViewModel`.
- Add `INavigationService` / `NavigationService` with an explicit route key, current page view model and navigation state. The service must not know about a WPF Window.
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

1. Add HostBuilder-based composition, `ShellViewModel`, route keys and `NavigationService`; keep the legacy content hosted temporarily so startup and all commands continue working.
2. Extract the seven existing page regions into separate `UserControl` files without changing their bindings first.
3. Move each page's bindings and commands into its specific view model, migrating one route at a time. Split the current Study Lab / Academy / Progress / Settings responsibilities out of `StudyWorkspaceViewModel`; do not leave that 1,200-line monolith behind.
4. Remove the legacy view models and old window-level navigation handlers only after every route uses its page-specific view model.

## Acceptance tests

- DI composition test: resolve shell, navigation service and all seven page view models without constructing a Window.
- Navigation test: every route resolves to the expected view model, and returning to a route preserves its page state.
- Binding/command tests per page, including Moodle sync and What-If projections.
- Persistence regressions for SQLite, calendar events, PDF metadata, focus/task state, profile and backups.
- Windows CI: test suite, Release WPF build, self-contained win-x64 publish and startup smoke test.
- Security CI: secret scanning and blocking dependency audit.

## Explicit non-goals

Phase 2 must not redesign the SQLite schema, change SQLCipher/DPAPI/AES backup formats, replace Moodle business logic, or introduce remote AI calls. Preserve the verified Phase 1 visual system through shared application resources and the same design tokens.
