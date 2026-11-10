# Subject workspace — Notion integration and implementation plan

## Scope and decisions

This work implements the subject-sheet direction agreed in the product discovery:

- Subjects can be presented as list or grid; cards show name, exam date and progress.
- A subject opens a dedicated workspace with progressive detail and separate tabs: Overview, Information, Syllabus, Study plan, Exams and grades, Materials, Statistics and AI.
- Syllabus is hierarchical (modules, chapters, subtopics). Each topic will distinguish work completion from mastery/preparedness.
- Study activities can be linked to topics manually and automatically.
- Exam history supports multiple attempts, accepted/rejected grades, weighted average and what-if scenarios.
- Materials have categories, tags, topic links, search and filters.
- The study plan is adaptive but proposed changes never overwrite user-locked decisions without confirmation.
- Analytics must show formulas, periods, denominators and missing-data caveats.
- AI is opt-in and cannot change grades, syllabus, materials or plans without approval.

The UI owner must expose bindings for these capabilities. This technical slice does not change XAML, styles, layout or navigation.

## Notion sources inspected

The connected workspace contains:

- Economia & Statistica | UniTo — course home and planning notes.
- Esami — L-33 Economia e Statistica — the live course data source.
- UniToStudyHub — Import Economia e Statistica — import manifest and five JSON attachments.

The live data source currently returns 27 rows. The Notion import manifest reports 5 passed marks / 36 CFU, 33 study activities and 13 lesson notes. These are source-reported counts; the attachments have not yet been imported into the app.

Important data-quality rules:

- The 27 rows are not 27 distinct curricular exams: the data includes a REFERENCE row, OFA and optional/lab records.
- REFERENCE — Analisi economica aggregata (ECM0375) is a catalogue reference, not a second exam to add beside the student's ECM0161 record.
- Microeconomia is recorded as ECM0305; ECM0467 is a separate reference discrepancy. Do not silently replace one code with the other.
- The Notion page states that no individual exam dates were available in the register. 30 November 2026–19 February 2027 is the general winter-session window, not an exam appointment.
- The 5 grade records do not have exam dates. The existing DateTime.UnixEpoch sentinel is used for an unknown date; it must not be rendered as a real exam date.
- The graduation target of November 2027 is a planning scenario, not a confirmed graduation session.

## Direct Notion connection

UniToStudyHub cannot reuse ChatGPT's connected Notion authorization. The desktop app needs its own Notion integration, shared with the relevant database/pages.

The integration uses the current Notion API version 2026-03-11. This version retains the data-source endpoints used here; its breaking changes concern block insertion positioning, the archived-to-in_trash rename and the transcription-to-meeting_notes rename. This read-only client does not use those renamed fields or write endpoints. See the [official Notion upgrade guide](https://developers.notion.com/guides/get-started/upgrade-guide-2026-03-11).

- GET /v1/data_sources/{data_source_id} reads the data-source schema.
- POST /v1/data_sources/{data_source_id}/query reads database rows with cursor pagination.
- GET /v1/blocks/{block_id}/children reads page content and nested blocks.

The data-source ID and import-bundle page ID are configuration, not secrets. The integration token is stored in the existing protected-secret directory using DPAPI and a Notion-specific entropy context. It is not written to StudyData, exports, backups, URLs or logs. Disconnect deletes only the token; already imported course data remains. Complete local-data deletion also removes the Notion token.

The import-bundle workflow reads the bundle page's file blocks, downloads short-lived Notion-hosted JSON attachments immediately, and sends no bearer token to the file host. The current file downloader accepts only HTTPS signed URLs on the Notion S3 host used by this workspace, enforces a 64 MB total package limit, and never persists or logs signed URLs. The five files are previewed together in their page order; earlier files win on duplicate IDs. The only apply path exposed by the integration is additive Merge.

Before connecting, the user must create an internal Notion integration with read-content capability and explicitly share the course database and the course pages with it. Notion integrations only see resources shared with them.

## Implemented technical slice

Files added:

- Services/NotionTokenVault.cs
- Services/NotionApiClient.cs
- Services/NotionApplicationService.cs
- tests/UniToStudyHub.Tests/Services/NotionIntegrationTests.cs

Files updated:

- Infrastructure/CompositionRoot.cs
- Services/PrivacyService.cs
- tests/UniToStudyHub.Tests/Infrastructure/CompositionRootTests.cs

Capabilities:

1. DPAPI-backed Notion token storage and disconnect.
2. Read-only data-source schema validation.
3. Cursor-paginated course query and mapping for the known course properties.
4. On-demand recursive page-block retrieval for course details and import-bundle pages.
5. Download of Notion-hosted JSON attachments through short-lived signed URLs, with host, content-type and size checks; the Notion bearer token is never sent to the file host.
6. Combined preview of the five ordered JSON files using the existing StudyDataImportService, including duplicate reporting and first-file-wins merge semantics.
7. Explicit additive Merge only after user confirmation; the integration does not expose destructive Replace.
8. Stable source identity based on the Notion page ID, duplicate prevention, preservation of non-empty local fields and monotonic status promotion for direct course-row sync.
9. Grade import uses the existing unknown-date sentinel; it does not invent an exam date.
10. Individual appeal dates are not copied into the calendar because the current Course model cannot preserve source URL, verification timestamp and verification state together.

The current course-row merge is deliberately conservative: it fills missing local metadata and does not overwrite non-empty user values. The JSON bundle import reuses StudyDataImportService and therefore keeps existing local records on ID conflicts. A future field-level sync policy can allow selected Notion-owned fields to refresh after explicit user configuration.

## UI binding contract still needed

The UI should expose:

- Notion integration token input (password field), data-source ID input, Connect / Test / Disconnect.
- Connection state, last successful retrieval and a non-sensitive error summary.
- Preview table with course name, code, CFU, career status, grade, source page and warnings.
- Bundle page ID input and an action to preview the five JSON attachments directly from Notion.
- Explicit Merge confirmation, with per-file names, record counts, duplicate counts, warnings and the final created/linked/skipped/grade count.
- Per-course action to load full Notion page content into a syllabus/material preview.
- A visible warning when a date is indicative, missing, unverified or only a general session window.

Never bind or display the raw token. Never trigger merge automatically after a successful connection.

## Next vertical slices

1. Subject shell and information — subject list/grid, course detail tabs, metadata and custom fields; persist selected subject and safe Notion source identity.
2. Syllabus and preparedness — module/chapter/topic hierarchy, completion percentage, self-rated preparedness, study activity links, preview/edit/deduplicate page-content import.
3. Exams and grades — multiple attempts, grade provenance, accepted/rejected state, weighted average and isolated what-if scenarios.
4. Materials — hierarchy, custom categories/tags, topic relationships, search and safe preview.
5. Adaptive study plan — transparent proposals based on exam date, syllabus, preparedness, difficulty, available time and actual study; lock/override controls.
6. Statistics and AI tutor — evidence-based analytics, source citations, opt-in tutor, user approval for all persisted changes.

Each slice requires focused domain tests, SQLite migration tests where applicable, Windows build and runtime verification, and a documented binding contract. No slice should add a parallel JSON database or fabricate official data.
