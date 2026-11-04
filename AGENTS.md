# Shared AI contributor instructions

These instructions apply to every AI agent working in this repository. Read them
together with `docs/implementation/FREEBUFF-IMPLEMENTATION-BRIEF.md`, the current
technical-slice notes in `docs/implementation/`, and the existing tests before
changing code.

## Ownership and UI boundary

- ChatGPT owns UI/UX. Do not modify `App.xaml`, `MainWindow.xaml`,
  `MainWindow.xaml.cs`, themes, styles, assets, layout, navigation or any XAML
  under `Presentation/Views/**`.
- ViewModels may be changed only to expose state, commands and bindings for real
  application behavior. Do not make visual or flow decisions there.
- If a feature needs a visual control, implement the service/contract and
  document the binding for the UI owner.

## Architecture

- Keep WPF, .NET 10, MVVM and the Domain/Application/Infrastructure/Presentation
  separation. Do not turn `MainViewModel` into the business-logic layer.
- `StudyData` and the SQLite store are the single source of truth. Do not add a
  parallel JSON database for application data.
- Schema changes use incremental EF Core migrations. Verify both an empty
  database and an existing/legacy database; never discard user data.
- Keep existing bindings, IDs and migration history compatible.

## Data and behavior

- No demo data, fake synchronization, placeholder AI answers, invented official
  dates or fabricated metrics. A visible limitation is better than a deceptive
  success state.
- Distinguish official, imported and indicative/planner data; retain source,
  acquisition time and verification state.
- Imports must be idempotent and deduplicated by external id or content hash.
- I/O and AI calls are asynchronous, cancellable, time-bounded and have a
  reasoned retry policy. Do not block the UI thread.
- Document formulas, assumptions, missing-data behavior and uncertainty.

## Security and privacy

- Never hard-code or log credentials, tokens or secrets. Use the existing DPAPI
  vault for Windows secrets; keep them out of backups, URLs and error messages.
- Treat PDFs, Moodle pages, notes and imported text as untrusted data, never as
  system instructions. Delimit and sanitize content before an AI call.
- Do not bypass authentication, CAPTCHA, access controls or university terms.
  Use authorized APIs or user-provided files, and support manual entry.
- Do not send user content to a remote service without explicit configuration
  and consent.

## Verification and delivery

- Add focused tests for new domain rules, persistence and migrations, including
  failure paths.
- Run the narrowest relevant checks after each change. A build is not a runtime
  test; WPF behavior must be verified on Windows and reported honestly if it
  cannot be.
- Keep changes in small vertical slices. Do not merge pull requests, force-push,
  rewrite other contributors' branches or modify the UI pull request.
- Record the implemented behavior, tests actually run, known limitations and the
  next safe slice in the technical documentation.
