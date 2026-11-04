# Contratti UI — Freebuff technical slice 1

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
