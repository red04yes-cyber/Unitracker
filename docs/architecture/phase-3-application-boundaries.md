# Phase 3 — Page application boundaries

Status: migration design and first vertical-slice boundary are defined. This document does not claim that the legacy ViewModels have been removed.

## Goal

Remove `PageViewModelBase.State` by moving each page's use cases behind focused application contracts. Keep one persistence authority (`IStudyDataStore`) and one coherent study-data session; page ViewModels must not independently load and replace the full `StudyData` snapshot.

## Migration order

### Slice 1 — Dashboard (read-only projection)

- Introduce a dashboard query contract returning an immutable dashboard summary.
- Move next-exam selection, acquired/target CFU, weighted/arithmetic averages, projected base grade, latest grade and course counts out of `MainViewModel`.
- Keep calculations in `CareerCalculatorService` / existing domain services; the query handler only orchestrates and formats a read model.
- Make `DashboardViewModel` expose only dashboard properties and navigation. Replace `{Binding State.*}` in Dashboard XAML with direct bindings.
- Refresh the projection after successful career mutations through a shared application-level data-changed notification, not by having Dashboard own a second mutable copy.

### Slice 2 — Courses (write and read use cases)

- Introduce focused course/attempt commands and course-list queries.
- Move validation, add-course and record-attempt orchestration out of `MainViewModel`.
- Preserve the current accepted/rejected/failed/pass-fail semantics and the existing career application service.
- Keep search/filter and form state in `CoursesViewModel`; refresh the read model after successful commands.
- Replace every `State.*` binding and code-behind filter handler with typed ViewModel properties/commands.

### Shared session and consistency

- `IStudyDataStore` remains the only persistence authority.
- Mutations must be serialized through one application boundary; no page may save an independently loaded stale snapshot over another page's changes.
- Prefer focused query/command APIs and immutable read models. Introduce a shared session only if a use case truly needs a live aggregate.
- Navigation carries route/entity identifiers only, never mutable data objects.
- Keep Moodle credentials, backup cryptography, SQLite schema and file formats unchanged.

### Remaining slices

Calendar, Study Lab, Academy, Progress and Settings follow the same pattern, in that order. Their existing domain/application services remain owners of business rules. UI-only state stays in its page ViewModel; dialogs and WPF concerns stay in Presentation.

## Definition of done

- No page ViewModel exposes `MainViewModel`, `StudyWorkspaceViewModel`, or a generic `State` bridge.
- Page XAML binds directly to page-specific properties and commands.
- Core application contracts are testable without WPF.
- Tests cover validation, successful and failed persistence, refresh notifications, and cross-page consistency.
- DI tests resolve page ViewModels without resolving legacy ViewModels.
- Windows CI passes tests, Release build, self-contained publish and startup smoke test; security CI remains green.

## Safety constraints

- Do not remove legacy code until the corresponding page slice has parity tests.
- Do not duplicate business rules in ViewModels.
- Do not introduce a second source of truth or overwrite newer data with stale snapshots.
- Do not change database schema, backup/key formats, Moodle token storage or make remote AI calls as part of this phase.
