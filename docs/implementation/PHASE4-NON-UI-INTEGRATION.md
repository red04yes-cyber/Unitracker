# Phase 4 non-UI integration

## Scope and base

This branch starts from `develop` at `c22570b` and ports only the Phase 4
non-UI work present on `main` (`54ac3aa`). It is intentionally independent from
the UI pull requests.

Included:

- provider-agnostic AI selection (`AiProviderFactory`) for OpenAI, Gemini and
  local Ollama;
- non-secret provider preferences with atomic, private persistence;
- the Gemini `generateContent` adapter;
- `AiStudyService.AskAsync` with explicit consent, daily request limit,
  untrusted-context delimiters and no automatic sending;
- selective privacy reset, diagnostic bundle preview/creation and AI-consent
  revocation;
- validated JSON import preview plus additive merge or backed-up replace;
- composition-root registration and focused tests;
- independent Windows portable packaging script and project version metadata.

Excluded deliberately:

- `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`;
- themes, styles, assets and XAML under `Presentation/Views/**`;
- `StudyWorkspaceViewModel`;
- `.github/workflows/build-windows.yml`;
- any UI choice or changes to PR #69/#70.

## Architecture

`StudyData`/SQLite remains the single source of truth. AI keys remain outside
`StudyData` in `AiCredentialStore` (DPAPI on Windows); provider/model/consent
are kept in `ai-provider-preferences.json` without the key. The preferences
file is written atomically with private file permissions.

`AiProviderFactory` returns an unavailable provider unless all of the following
are true:

1. explicit consent is stored or supplied by the documented environment;
2. the provider is one of `openai`, `gemini`, `ollama`;
3. the model and endpoint are valid; Ollama is restricted to loopback;
4. the provider-specific credential can be resolved;
5. the daily request limit is valid.

`AiStudyService.AskAsync` marks the local study context as
`[UNTRUSTED_STUDY_CONTEXT]` and does not call a provider when consent is absent
or the daily limit is exhausted.

`PrivacyService.DeleteAllAsync` revokes persisted AI consent before deleting
credentials, so a restart cannot silently re-enable a provider.
`ResetAsync` is confirmed, scoped and optionally creates a verified safety
backup before changing records.

`StudyDataImportService` validates the complete export envelope with
`StudyDataEntityMapper.NormalizeAndValidate`, caps the payload at 64 MB and
100,000 records, and never imports credentials or physical files. Merge is
additive and keeps local records on conflicts; Replace requires a configured
backup service and creates a safety backup first.

## UI contract for the next UI slice

The following capabilities are ready but not exposed by this non-UI PR:

| Capability | Service contract | UI work required |
|---|---|---|
| Provider settings | `AiProviderPreferencesStore.Save/Load`, `AiCredentialStore.SaveKey` | provider/model/consent controls; key entry must be cleared after save; restart to apply |
| AI plan | `AiStudyService.AskAsync(objective, context)` | button/command, loading state, show proposal as non-official and do not auto-apply |
| Oral practice | `AiStudyService.AskAsync` with syllabus context | button/command and notice that questions are exercises, not official exams |
| Import preview | `StudyDataImportService.PreviewAsync` | file picker and display counts, conflicts, warnings and SHA-256 |
| Merge/Replace | `ApplyAsync(preview, strategy, password?)` | explicit confirmation before Replace; show safety backup path |
| Selective reset | `PrivacyService.ResetAsync` | confirmation, scope selector, optional safety backup |
| Diagnostic bundle | `PreviewDiagnosticBundle`, `CreateDiagnosticBundleAsync` | show preview; require explicit consent; let the user choose where to save |
| AI consent revocation | `PrivacyService.DeleteAllAsync` / provider preferences | revoke consent and terminate the current AI runtime |

`StudyWorkspaceViewModel` in this branch already exposes `AiStatusMessage`,
`AiResponseText`, and `AskPdfCommand`; the Phase 4 plan/oral commands and the
privacy/import commands were not copied because they require coordinated UI
changes. The UI owner should add those bindings after reviewing this contract.

## Verification

Focused tests:

```text
dotnet test tests/UniToStudyHub.Tests/UniToStudyHub.Tests.csproj -c Debug \
  --filter "FullyQualifiedName~AiProviderFactoryTests|FullyQualifiedName~GeminiProviderTests|FullyQualifiedName~AiStudyGuidanceTests|FullyQualifiedName~StudyDataImportServiceTests|FullyQualifiedName~PrivacyResetAndDiagnosticsTests"
```

Result on this Linux host: 14 passed, 11 failed. All 11 failures are
`PlatformNotSupportedException` from `DatabaseKeyVault.GetOrCreateKey` before
their assertions because the encrypted SQLite test database requires Windows
DPAPI. They are not reported as passing. AI provider, Gemini, guidance,
diagnostic-redaction and consent tests pass independently of that fixture.

Full Release suite on this host: 292 tests, 255 passed, 37 failed. Every one of
the 37 failures is the same Windows DPAPI `PlatformNotSupportedException`; no
assertion failed elsewhere. Release build: 0 warnings, 0 errors. Gitleaks
8.30.1 scanned 337 commits with no leaks; both NuGet dependency graphs reported
no vulnerable packages under `EnableWindowsTargeting=true`.

Windows UI/runtime, real Gemini/OpenAI calls, DPAPI backup/restore and the
portable PowerShell packaging script were not executed here. No GitHub Actions
run was triggered because no pull request was created and the pushed branch name
`integration/phase4-non-ui` is not in the workflow push filters.

## Limitations

- No live AI network call is made by the tests; provider tests use a local
  mock HTTP handler.
- Gemini is selected only after explicit consent and a resolvable key.
- Provider preferences are non-secret but remain local; they are not included in
  the import/export payload because they are device settings.
- The import does not transfer PDFs/Moodle files, only StudyData metadata.
- The UI on `develop` does not yet expose these services; this PR must not be
  merged as if the UI existed.