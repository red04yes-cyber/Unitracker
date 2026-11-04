# Sistema, Impostazioni e Backup — UI/UX Design Specification

> Scope: WPF presentation only. Settings persistence, backup creation, integrity verification, restore and security remain in services/infrastructure.
> Placement: existing **Impostazioni** destination and the persistent settings shortcut in the application header.

## 1. Product intent

Settings should make the application's behavior understandable and controllable. The Backup Center should make data protection visible without implying guarantees that the implementation does not provide.

The experience must be calm, precise and transparent. Do not claim that backups are encrypted, cloud-synced or include credentials unless the technical implementation explicitly guarantees it.

## 2. Information architecture

Use a settings page with compact left-side sub-navigation and one focused content panel:
1. **Profilo universitario** — degree, academic year, enrollment year and graduation target.
2. **Organizzazione dello studio** — weekly hours, maximum daily minutes and study preferences.
3. **Connessioni** — Moodle URL, connection status and token actions.
4. **Privacy e AI** — show only when a real provider/privacy configuration contract is available.
5. **Dati e backup** — local data location, backup history, verification and restore.

Do not display unfinished sections as active controls. A planned area may be documented, but should not be represented as a functional form until persistence and commands exist.

## 3. General settings layout

Header:
- Eyebrow: `PREFERENZE`.
- Title: `Impostazioni`.
- Subtitle: `Configura StudyHub e gestisci i tuoi dati.`

Use a narrow settings navigation rail and a larger form area. Group related fields with section headings and concise descriptions. Place `Salva modifiche` near the edited form, not in a distant global footer.

For multi-field profile updates, submit all related fields in one save operation. Show unsaved changes, validation errors beside the relevant field and a success state only after persistence completes.

## 4. Profile and study preferences

Profile fields may include, when supported:
- corso di laurea and course code;
- anno di iscrizione and anno accademico corrente;
- CFU totali previsti;
- data obiettivo laurea;
- preferenze di studio;
- ore settimanali disponibili;
- limite giornaliero in minuti.

Validation must be contextual and understandable. For example, CFU total must be positive when configured; an unset total should be represented as `Non configurato`, not silently replaced with a default.

Preferences that affect planning should explain that generated plans are proposals and do not automatically change confirmed events.

## 5. Moodle connection

Show connection state using real service feedback:
- not configured;
- configured but not verified in this session, if that distinction exists;
- connected / token verified;
- connection failed.

The token input should be masked. Never echo it back after saving. Explain that the secret is stored separately using Windows DPAPI only if that remains true in the actual implementation. Keep `Verifica e salva` separate from `Sincronizza` if those operations are distinct.

When a connection fails, preserve the entered URL and show a useful, sanitized error message. Never expose token contents or raw sensitive exceptions.

## 6. Backup Center

The Backup Center is a first-class data-safety surface, not a generic file list.

### Summary area

Show:
- local backup directory;
- number of backup files;
- most recent backup date and size, when available;
- latest integrity state.

Do not imply cloud backup. If the local backup directory is unavailable, show the path error and a recoverable explanation.

### Backup history

Each row should show:
- file name (truncated safely, full path in details/copy action);
- creation date and time;
- file size;
- integrity state;
- actions supported by the current service.

Integrity states:
- `Da verificare` — a backup returned by `ListBackups()`; the service currently reports `IntegrityVerified == false` until verification is run;
- `Verificato` — the latest explicit integrity check returned true;
- `Non valido` — verification failed;
- `Errore` — the check could not complete.

A SHA-256 value may be available in details for audit/copy, but it is not a substitute for a successful SQLite integrity check. Do not label the backup as encrypted: the current service exposes integrity validation and hashing, not backup encryption.

### Create backup

Primary action: `Crea backup`.

While running, disable only conflicting backup actions and show progress as indeterminate unless a real percentage is available. On success, show the created file name, timestamp, size and the actual integrity result. If verification fails, do not show a success badge.

### Verify backup

Verification must be an explicit action for a listed backup. Show the result against the selected file and timestamp. Do not change a backup from `Da verificare` to `Verificato` before the service returns success.

### Restore backup

Restore is destructive and requires a deliberate confirmation flow:
1. User selects a backup.
2. UI displays file name, date, size and integrity state.
3. Restore remains disabled until the selected backup has passed verification.
4. Confirmation explains that current local data will be replaced by the selected snapshot.
5. The user confirms the exact selected file.
6. The UI displays progress and prevents duplicate restore requests.
7. On success, show a clear completion message and offer a reload/restart path if required by the application lifecycle.
8. On failure, preserve the error state and show the safety-backup location when the service provides it.

The current `BackupService.RestoreAsync` implementation creates a safety backup before replacing the live database, but its `Task` return value does not expose the safety backup path on success. To fulfill the UI requirement to show that path, the technical contract should return a `RestoreResult` (or otherwise expose the created `BackupInfo`). Do not invent the path in the ViewModel.

### Important scope and security notes

- The current backup service excludes the Moodle token because it is stored separately in DPAPI. State this clearly near the backup actions: `Le credenziali Moodle non sono incluse nel backup.`
- Do not promise that restoring a database restores external credentials, provider settings or files that are stored elsewhere.
- Do not expose raw database paths as the primary label; provide a details/copy action for the full path.
- Do not add schedule/automatic backup controls unless an actual scheduler exists.
- Do not make restore look reversible unless the safety backup is actually created and its outcome is surfaced by the service.

## 7. Empty, loading and error states

- No backups: explain that no local backup files have been created and show `Crea backup`.
- Backup directory unavailable: show the real path and a recoverable error.
- Backup unverified: use neutral status and offer `Verifica integrità`.
- Integrity failed: show `Non valido`, disable restore and preserve the file for diagnosis.
- Create failed: preserve the list and show a sanitized reason.
- Restore in progress: lock conflicting operations and make the selected file explicit.
- Restore failed: show the failure and safety-copy location when available.
- No network: local backup features should remain available if the service does not require a network connection.

## 8. Proposed presentation contract

Names below are proposals; use actual service/ViewModel members:
- Settings: `AcademicProfile`, `StudyPreferences`, `WeeklyStudyHours`, `MaxDailyStudyMinutes`, `MoodleConnectionStatus`, `IsSaving`, `HasUnsavedChanges`.
- Backup: `BackupDirectory`, `Backups`, `SelectedBackup`, `IsCreatingBackup`, `IsVerifyingBackup`, `IsRestoringBackup`, `RestoreResult`, `ErrorMessage`.
- Commands: `SaveProfileCommand`, `VerifyMoodleCommand`, `SyncMoodleCommand`, `CreateBackupCommand`, `RefreshBackupsCommand`, `VerifyBackupCommand`, `RestoreBackupCommand`.

The UI must use the actual `BackupInfo` values: `FilePath`, `CreatedAt`, `SizeBytes`, `Sha256`, `IntegrityVerified`. Do not infer integrity from file existence, size or hash.

## 9. Acceptance criteria

- Settings are grouped by user intent and do not expose inert controls.
- Multi-field profile updates validate and save as one coherent operation.
- Moodle credentials are masked and never echoed or logged.
- Backup history distinguishes unverified, verified and invalid files.
- Restore cannot be triggered on an unverified or invalid backup.
- Restore confirmation names the exact file and explains the effect on current data.
- The interface does not claim encryption, cloud sync or credential inclusion without technical support.
- The safety-backup path is surfaced only when the service makes it available.
- Layout remains usable at 1440×900 and 1024×700 with keyboard access and text scaling.
- Visual verification on Windows is required before calling the screen complete.
