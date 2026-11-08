# Phase 2 — Presentation architecture and navigation

Status: shell, typed navigation, seven page views and seven page-specific ViewModel types are implemented. The data/command layer is still in a compatibility migration stage: the page ViewModels currently expose the shared `MainViewModel` through an explicitly named `State` adapter. They no longer share a view or a page-level DataContext, but the old business/presentation monoliths are not yet removed.

## Implemented shell

- WPF startup creates one `IHost` with `Host.CreateDefaultBuilder`; core and presentation services share that provider and host lifetime.
- Presentation registrations are grouped in `AddUniToStudyHubPresentation`.
- `INavigationService` / `NavigationService` use the typed `StudyRoute` values and have no WPF Window or control dependency.
- `ShellViewModel` owns the seven long-lived page ViewModels and keeps sidebar selection synchronized with the route service.
- `MainWindow` now owns global chrome, route selection, theme toggle and the focus refresh timer only.

## Extracted page views and ViewModels

Each original TabItem body now lives in its own XAML UserControl. The controls receive their own specific singleton ViewModel through the shell binding.

| Route | View | ViewModel |
| --- | --- | --- |
| Today | `Views/DashboardView.xaml` | `DashboardViewModel` |
| Courses | `Views/CoursesView.xaml` | `CoursesViewModel` |
| Calendar | `Views/CalendarView.xaml` | `CalendarViewModel` |
| Study Lab | `Views/StudyLabView.xaml` | `StudyLabViewModel` |
| Academy | `Views/AcademyView.xaml` | `AcademyViewModel` |
| Progress | `Views/ProgressView.xaml` | `ProgressViewModel` |
| Settings | `Views/SettingsView.xaml` | `SettingsViewModel` |

Dialog and confirmation handlers formerly hosted by `MainWindow.xaml.cs` now live in the shared page-control base, rather than in the global shell window. The XAML markup and bindings were moved intact to preserve the current workflow and appearance.

## Explicit compatibility boundary

- `PageViewModelBase.State` still points to the singleton `MainViewModel`. This is an intentional bridge while commands and state are relocated, not the final decoupled architecture.
- `MainViewModel` and `StudyWorkspaceViewModel` remain the owners for existing operations. They must be decomposed rather than copied into new view models.
- The shared data snapshot must remain single-owner while the page operations are moved; do not have each page load and replace the full `StudyData` aggregate independently.
- The window startup path initializes the existing MainViewModel before showing the shell. This bootstrap can move to a shared session/application coordinator after page use cases have been extracted.

## Next migration steps

1. Replace the `State` compatibility adapter with page-specific application contracts and focused ViewModels, starting with Dashboard and Courses.
2. Split calendar commands and projections into Calendar, Study Lab operations into StudyLab, local PDF/syllabus/questions into Academy, progress/What-If into Progress, and profile/Moodle/backup into Settings.
3. Introduce one shared study-data session/query boundary backed by the existing `IStudyDataStore`; preserve persistence ownership and avoid lost updates.
4. Replace the legacy TabControl with a shell ContentControl and application DataTemplates after route state has a real page ViewModel per route.
5. Remove `MainViewModel`, `StudyWorkspaceViewModel`, `PageViewModelBase.State`, and page-control compatibility handlers once their responsibilities have moved to the new APIs.

## Dependency ownership

- `CareerCalculatorService` and `CareerApplicationService` stay in shared domain/application composition. What-If UI belongs to Progress; calculations remain in the career service.
- Moodle API/token handling stays in `MoodleApplicationService`; Settings owns connection/sync commands. Courses and Calendar consume the persisted records.
- `IStudyDataStore` remains the persistence authority.
- No new mutable state is passed through navigation. Stable entity identifiers will be used once page use cases are separated.

## Acceptance tests

- DI test resolves all seven page ViewModels, the shell and navigation service without a Window.
- Composition test confirms every page ViewModel is a singleton and currently shares the same compatibility state instance.
- Navigation test verifies typed routes, invalid-route rejection and synchronized sidebar selection.
- Follow-up page tests must verify each page's commands and state without resolving `MainViewModel` or `StudyWorkspaceViewModel`.
- Windows CI: test suite, Release WPF build, self-contained win-x64 publish and startup smoke test.
- Security CI: secret scanning and blocking dependency audit.

## Explicit non-goals

No SQLite schema changes, key/backup format changes, replacement of Moodle business logic, or remote AI calls are part of this presentation migration. Preserve the Phase 1 visual design tokens and shared App resources.
