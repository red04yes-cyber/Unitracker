# Study Workspace — UI completion for the exam simulator

This slice connects the existing question-bank, error-journal and activity-ledger services to the Study Workspace UI.

## User-visible behavior

- The exam simulator offers a configurable time limit: 0 (untimed), 10, 15, 20, 30, 45, 60 or 90 minutes.
- The displayed countdown is refreshed once per second from an absolute deadline. The deadline is checked again when an answer is submitted or manually graded; a late action closes the attempt instead of accepting the answer.
- Closed questions continue to use the existing automatic evaluator.
- Open questions pause progression after submission and expose explicit **Valuta: corretta** / **Valuta: da correggere** actions. An incorrect manual assessment is added to the error journal and saved immediately.
- Completed or expired attempts are recorded as `StudyActivityType.Simulation` entries in the existing activity ledger. This reuses the existing SQLite persistence and avoids a second history store. Each record contains the date, elapsed interval, outcome summary and whether the attempt was timed.
- The history panel shows the latest 20 simulator attempts for the selected course.
- The weak-topic panel uses `ErrorPatternService`; it shows unresolved repeated errors only (at least two entries sharing course, topic and error type). It is evidence-based and is not an AI diagnosis.

## Binding contract

The XAML binds to these `StudyWorkspaceViewModel` members:

- `SimulationTimeLimitOptions`, `SimulationTimeLimitMinutesInput`
- `SimulationCountdownDisplay`, `IsSimulationActive`, `SimulationQuestionDisplay`, `SimulationStatus`
- `SubmitSimulationAnswerCommand`
- `CanManuallyGradeCurrentAnswer`, `GradeSimulationAnswerCorrectCommand`, `GradeSimulationAnswerIncorrectCommand`
- `SimulationHistory` (`StartedAt`, `Result`)
- `WeakTopicPatterns` (`Topic`, `Count`)

The existing one-second WPF dispatcher tick now refreshes both the focus timer and the simulator countdown. Business rules and persistence remain in services and `StudyData`; the UI does not create a parallel store.

## Verification and limits

- The attempt history is persisted through the existing activity ledger.
- A timed attempt is closed at expiry and answer submission/manual grading are rejected at or after the deadline.
- The timer is not a background alarm: it advances while the desktop app is running. The absolute deadline is still used for the final guard when the user submits.
- Interactive WPF runtime/visual QA must be performed on Windows. A successful Windows CI build is not a substitute for manual runtime verification.
