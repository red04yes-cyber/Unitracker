# Contratti UI — Freebuff technical slice 1 + 2 + 3

La UI è responsabilità di ChatGPT. Questo documento elenca i contratti tecnici disponibili; **non** impone un layout e
non modifica i file visuali. I binding già presenti in `MainWindow.xaml` restano invariati.

## Dati condivisi

`StudyData` è il DTO principale caricato/salvato da `IStudyDataStore`. Le nuove proprietà sono additive e compatibili
con JSON/SQLite legacy.

### Profilo

| Proprietà | Tipo | Significato |
|---|---|---|
| `DegreeProgramName` | `string` | Nome del corso di laurea |
| `DegreeProgramCode` | `string` | Codice/classe del corso |
| `EnrollmentYear` | `int?` | Anno di iscrizione |
| `CurrentAcademicYear` | `int?` | Anno accademico corrente |
| `DegreeTotalCfu` | `int` | CFU totali previsti; `0` = non configurato |
| `GraduationTarget` | `DateTime` | Data obiettivo |
| `StudyPreferences` | `string` | Preferenze libere |
| `WeeklyStudyHours` | `int` | Disponibilità settimanale |
| `MaxDailyStudyMinutes` | `int` | Limite giornaliero |

### Materia

`Course` espone `Status` (`CourseStatus`), `Archived`, `Teacher`, `AcademicYear`, `Session`, `EstimatedStudyHours`,
`AppealDate`, `SyllabusSummary`, `SyllabusUrl`, `Notes`, `Source`, `UpdatedAt`. `Completed` resta per compatibilità.

### Tentativi

`CareerExamData.Attempts` è lo storico. `ExamAttemptData` espone stato, esito, decisione, voto, lode, idoneità e
timestamp. La UI deve mostrare gli attempt anche se rifiutati o falliti.

### Syllabus e domande

`SyllabusTopicData`: `CourseId`, `Title`, `Description`, `Status`, `EstimatedMinutes`, `SourceUrl`, `LastStudiedAt`.
`QuestionBankItemData`: `CourseId`, `SyllabusTopicId`, `Text`, `Type`, `Difficulty`, `Options`, `CorrectAnswer`,
`Explanation`, `Source`, `IsOfficial`.

## Servizi e comandi

### `AcademicProfileService`

- `Update(StudyData, AcademicProfileUpdate)`
- `AddCourse(StudyData, Course)`
- `UpdateCourse(StudyData, Course)`
- `SetStatus(StudyData, courseId, CourseStatus)`
- `Archive(StudyData, courseId)`
- `Restore(StudyData, courseId)`
- `RemoveCourse(StudyData, courseId, removeCareerHistory)`

Suggerimento UI: il comando Salva profilo deve inviare tutti i campi in un’unica operazione; i comandi archivio e
ripristino devono restare separati dall’eliminazione. Dopo una modifica, la UI deve chiamare `IStudyDataStore.SaveAsync`
e ricaricare/refreshare gli indicatori.

### `CareerApplicationService`

- `CareerSummary GetSummary(StudyData)`
- `decimal? GetArithmeticAverage(StudyData)`
- `CareerOverview GetOverview(StudyData)`
- `CareerExamData EnsureCareerExam(StudyData, Course)`
- `ExamAttemptData RecordResult(...)` con `ExamOutcome`, `GradeDecision`, voto, lode, idoneità e timestamp
- `ExamAttemptData ScheduleAttempt(...)`
- `WhatIfResult Simulate(StudyData, IEnumerable<WhatIfCourseInput>)`
- `IReadOnlyList<ExamAttemptData> GetAttempts(StudyData, courseId)`

Suggerimento UI: pulsanti distinti “Accetta voto”, “Rifiuta voto”, “Registra fallimento”, “Registra idoneità”; il
rifiuto deve inviare `recordedAt: null`. Il risultato What-if deve essere marcato visivamente come simulazione e non
salvato finché l’utente non conferma un’operazione esplicita.

### `SyllabusTrackerService` e `ExamReadinessService`

- `SyllabusTopicData Add(...)`
- `void UpdateStatus(StudyData, topicId, SyllabusTopicStatus, DateTime? studiedAt)`
- `SyllabusCoverage GetCoverage(StudyData, courseId)`
- `IReadOnlyList<SyllabusTopicData> GetTopics(StudyData, courseId)`
- `ExamReadinessResult Calculate(StudyData, courseId)`

Binding utili: `ExamReadinessResult.Score` (nullable), `IsComplete`, `Components`, `MissingData`, `Formula`,
`Coverage.CompletedTopics`, `Coverage.TotalTopics`, `Coverage.EstimatedRemainingMinutes`. La UI deve mostrare
“punteggio parziale” quando `IsComplete == false` e non deve nascondere i dati mancanti.

### `QuestionBankService`

- `Add`, `Update`, `Remove`, `GetForCourse`
- `QuestionEvaluation Evaluate(question, answer)`

`QuestionEvaluation.IsCorrect` è nullable: per le domande aperte è `null` e `RequiresManualReview` è `true`.
La UI non deve presentare una risposta aperta come corretta o sbagliata senza una valutazione umana/AI configurata.

### `TimeBudgetService`

`TimeBudgetPlan Calculate(StudyData, DateTime? today)` restituisce `WeeklyMinutes`, `AvailableMinutes`,
`RequiredMinutes`, `Allocations`, `Warnings`, `IsOverCapacity` e `UnallocatedMinutes`. È un piano proposto, non un
insieme di eventi confermati.

### `BackupService`

- `CreateBackupAsync()`
- `ListBackups()` — gli elementi elencati hanno `IntegrityVerified == false` finché non vengono verificati
- `VerifyBackupAsync(path)`
- `RestoreAsync(path)`

La UI deve chiedere conferma prima del ripristino, mostrare il percorso del backup di sicurezza e non nascondere gli
errori di integrità. Il servizio non copia il token Moodle.

## Slice 2 — Calendario, Studio Lab, PDF/AI e progressi

### Calendario

`StudyData.CalendarEvents` è la sorgente unica; il vecchio `CalendarEventStore` viene letto solo per
l’import legacy una tantum. `MainViewModel` espone:

| Membro | Uso |
|---|---|
| `CalendarEvents` | collezione persistita |
| `VisibleCalendarEvents` | risultati del periodo corrente |
| `CalendarDate`, `CalendarPeriodDisplay` | navigazione e intestazione |
| `SelectedCalendarEvent` | selezione/modifica |
| `CalendarEventTitle`, `CalendarEventCategoryInput`, `CalendarEventDate`, `CalendarEventStartTime`, `CalendarEventEndTime`, `CalendarEventAllDay` | form |
| `AddCalendarEventCommand`, `DeleteCalendarEventCommand`, `PreviousCalendarPeriodCommand`, `NextCalendarPeriodCommand`, `TodayCalendarCommand` | comandi |

Ogni evento espone `Source` (`CalendarEventSource.Manual/Ics/Planner/Moodle`), `Verification`
(`CalendarEventVerification.Unverified/Indicative/Official`), `TimeZoneId`, `IsCancelled` e `ExternalId`.
La UI deve mostrare la provenienza e non presentare gli eventi `Planner`/`Indicative` come appelli ufficiali.
Per le viste usare `CalendarService.GetWeekRange`, `GetMonthRange` e `GetEventsInRange`; non creare un secondo
store o una seconda lista di eventi.

### Studio Lab

Servizi disponibili (nessun ViewModel dedicato è stato aggiunto in questa slice):

- `FocusSessionService`: `Start`, `Pause`, `Resume`, `Finish`, `Reset`, `Cancel`, `GetActive`, `Snapshot`.
  `FocusSnapshot` espone `State`, `ElapsedSeconds`, `RemainingSeconds`, `IsExpired`. Il timer visuale deve
  interrogare lo stato persistito, non possedere un countdown separato.
- `StudyTaskService`: `Add`, `Complete`, `Cancel`, `Remove`; task con corso, scadenza, minuti e priorità.
- `ErrorJournalService`: `Add`, `Review`, `Resolve`; errori collegati a corso/argomento.
- `FlashcardService`: `Add`, `Review`, `GetDue`; rating `Again/Hard/Good/Easy`, storico delle revisioni.
- `DistractionService`: `Add`, `Resolve`; `EnergyService`: `Add`, `GetRecent`.
- `StudyDirectorService.Propose` restituisce solo suggerimenti, non eventi creati automaticamente.

La UI deve salvare dopo ogni mutazione con `IStudyDataStore.SaveAsync` e mostrare lo stato vuoto quando non
ci sono sessioni, task o carte reali.

### PDF e AI

`PdfImportService.ImportAsync(path, data, courseId)` restituisce `PdfImportResult` con `Document`,
`WasDuplicate`, `RequiresOcr`, `Warnings`. `PdfDocumentData.Pages` contiene `PdfPageData.PageNumber/Text`;
`HasExtractableText` è `false` per PDF scansionati. `PdfIntelligenceService.Search` restituisce
`PdfSearchHit(PageNumber, Snippet)`; `PdfCitationService.CreateCitation` restituisce `SourceCitation` solo
se la pagina e la frase esistono davvero, e `Verify` ricontrolla la citazione.

`AiStudyService.AnswerFromDocumentAsync` e `SummarizeAsync` restituiscono `AiResponse` con `Status`:
`Configured`, `NotConfigured`, `Refused`, `Error`. Il default è `NotConfigured`: la UI deve mostrare la
configurazione mancante e non una risposta finta. `CitationPageHints` non è una prova: le citazioni vanno
validate. `PromptSafety.SystemPrompt` e `WrapDocument` delimitano i PDF come dati non attendibili.

### Progressi

`ProgressService.Calculate(data, from, to, now)` restituisce metriche derivate da sessioni completate,
task, flashcard e revisioni. `ErrorPatternService.Detect(data)` richiede un campione minimo di due errori per
lo stesso argomento/tipo. `WeeklyReviewService.Create(data, weekStart, now)` restituisce osservazioni e azioni
con avvisi quando i dati mancano. Mostrare sempre periodo, formula e “dati insufficienti” invece di numeri demo.

### Backup manifest

`BackupService.CreateBackupAsync` crea anche `<backup>.manifest.json`; `ReadManifestAsync` restituisce
`BackupManifest(FormatVersion, CreatedAt, Sha256, SizeBytes)`. `RestoreAsync` verifica il manifest quando
presente e rifiuta checksum non corrispondenti o formati futuri. Il manifest non è cifrato; non includere
segreti nella UI né nei messaggi.

## Slice 3 — Activity ledger e missioni

### Dati

`StudyData.StudyActivities`, `StudyMissions` e `ActivityRevisions` sono proprietà additive. `StudyActivityData`
espone `Type`, `Origin`, `Status`, `StartedAt`, `EndedAt`, `TimeZoneId`, `EffectiveSeconds`, riferimenti
(`CourseId`, `SyllabusTopicId`, `TaskId`, `MissionId`, `ExamId`, `SessionId`, `CalendarEventId`), rating 1–5,
tag, nota, `IsPrivate` e `IsDeleted`. `StudyMissionData` espone periodo, scadenza, difficoltà, stato, ricompensa
volontaria e `Steps`. `StudySessionData.Segments` è la sequenza persistita `Focus`/`Pause` con `StartedAt` e
`EndedAt`; non ricostruire le pause da `EndedAt - StartedAt`.

### `ActivityLedgerService`

- `AddManual(data, activity, now)` — quick-log manuale; imposta origine manuale e stato completato.
- `SyncFocusSession(data, session, now)` / `SyncFocusSessions(data, now)` — idempotente per `SessionId`.
- `Update(data, updated, reason, now)` e `SoftDelete(data, id, reason, now)` — richiedono una motivazione e
  producono una revisione.
- `GetForDay`, `FindOverlaps`, `BuildTimeline`, `BuildHeatmap`, `Summarize` — query e aggregazioni.

`ActivityTimeline.Slots` contiene `RecordedSeconds`, `HasOverlap` e `ActivityIds`; gli slot vuoti sono tempo non
registrato, non attività inventate. `ActivitySummary.RecordedSeconds` è l’unione degli intervalli focus, quindi non
somma le sovrapposizioni. `OverlapSeconds` è il tempo condiviso; `ByType` e `ByCourse` sono aggregazioni per
dimensione e non vanno sommate per ricavare il totale.

**Importante:** le pause sono gap tra segmenti focus; una sessione legacy senza segmenti usa un fallback focus
iniziale e non permette di ricostruire la posizione reale delle pause. Non sommare `ProgressService.FocusMinutes`,
`EffectiveSeconds` e i minuti del ledger. Fino alla slice di integrazione, usare il ledger per timeline/heatmap e
le sessioni per il `ProgressSnapshot` esistente, senza presentare i due numeri come un totale unico.

La review temporale, la formula DST e la migrazione `20261105011642_AddFocusSessionSegments` sono documentate in
`TECHNICAL-SLICE-3-REVIEW.md`.

### `StudyMissionService`

`Add`, `AddStep`, `SetStepCompleted`, `Complete`, `Cancel`, `GetForPeriod`. Una missione senza step non può essere
completata con `Complete`; lo stato è `Planned`, `InProgress`, `Completed` o `Cancelled`. La ricompensa è un testo
volontario, non un punteggio automatico.

### UI

Non esiste ancora un ViewModel dedicato: la schermata Studio Lab deve usare i servizi e salvare con
`IStudyDataStore.SaveAsync`. Non modificare `MainWindow.xaml` per questa slice; i contratti sono pronti per una
schermata separata. Gli export CSV/XLSX e il Pomodoro a cicli non sono ancora disponibili.

## Stati e messaggi richiesti

Per ogni comando la UI dovrebbe esporre:

- `IsBusy` e disabilitazione dei comandi durante I/O;
- messaggio di successo distinto dagli errori;
- stato vuoto quando non ci sono dati reali;
- `MissingData` per readiness e time budget;
- conferma esplicita per archivio/eliminazione/ripristino;
- indicazione “stima organizzativa” per date del planner e readiness.

## Note di compatibilità

I nomi `MainViewModel` e i binding esistenti non sono stati rilanciati in questa PR. Per una nuova schermata è
preferibile un ViewModel dedicato che componga questi servizi, invece di ampliare ulteriormente `MainViewModel`.
Non inserire logica di persistenza o calcolo direttamente nel code-behind.
