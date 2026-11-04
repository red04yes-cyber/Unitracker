# Progressi — UI/UX Design Specification

> Scope: WPF presentation only. Metric calculations, persisted activity, career rules and analytics aggregation remain in domain/application services.
> Placement: existing **Progressi** destination. Keep the current application shell and visual language.

## 1. Product intent

Progressi should help the student understand what has changed, what is improving and what deserves attention next. It must not reduce academic performance to one opaque score.

Every metric needs a real source, a clear unit and a comprehensible definition. Distinguish completed facts, current snapshots, forecasts and simulations. Never display illustrative numbers as if they were personal results.

## 2. Visual direction

Use the StudyHub palette and shared styles:
- light canvas `#F3F6FB`, white cards, navy `#17243B`, blue `#3157D5`;
- semantic green for completed outcomes, amber for warnings, violet only for AI-generated insight;
- small, restrained charts with readable axes and labels;
- avoid gauges, leaderboards, streak pressure and decorative percentages.

The first viewport should show a concise overview, not a wall of charts. Supporting sections can expand below it.

## 3. Page hierarchy

Header:
- Eyebrow: `IL TUO PERCORSO`.
- Title: `Progressi`.
- Subtitle: `La tua carriera e il tuo metodo di studio, letti nel tempo.`

Provide two clear segments:
1. **Carriera** — CFU, voti, esami, tentativi and simulations.
2. **Metodo di studio** — focus sessions, reviews, syllabus coverage and study consistency.

A period selector may be used for time-based learning metrics (for example 7 giorni, 30 giorni, semestre, tutto). Career totals should not silently change meaning when the period selector changes.

## 4. Career overview

Show only values supported by the current data and service results:
- CFU acquisiti, with `CFU totali previsti` only when `DegreeTotalCfu` is configured and valid;
- media ponderata;
- media aritmetica, with its exact attempt-selection rule described in details;
- voto base/proiezione, explicitly labeled as a calculation and not a guaranteed final grade;
- exams passed and exam-attempt history, including rejected or failed attempts where the underlying history supports them.

Do not count credits twice. Keep eligibility exams and exams without a numeric grade distinct from grade averages.

A career timeline can show exam outcomes by date. Use real `ExamAttemptData` entries, including their status and decision. If there are no attempts, show an empty state rather than an empty chart with zero bars.

### What-if simulation

Place the simulator in a separate section or clearly labeled panel. Its result must always carry a visible label:
`Scenario simulato · Non salvato`.

Show:
- current baseline;
- hypothetical inputs;
- simulated result;
- difference from baseline;
- explicit `Applica` or save action only if the technical contract provides one.

Until the user confirms a supported operation, simulated values must not alter career totals, averages, history or charts.

## 5. Study-method overview

Use real stored study records to show, when available:
- tempo di concentrazione;
- sessioni completate;
- distribuzione delle sessioni per materia;
- ripassi completati / dovuti;
- copertura del syllabus by course or topic;
- trend over the selected period.

Do not use `TimeBudgetPlan.AvailableMinutes` or `RequiredMinutes` as actual study time. Those values represent a proposed capacity/plan and belong in a separate `Pianificazione` subsection with the label `Stima organizzativa`.

Do not infer energy, focus quality or learning mastery from elapsed time alone. Optional energy check-ins remain self-reported and must be labeled as such.

## 6. Exam readiness and syllabus coverage

For each course, the readiness component may show:
- score only when supplied by the service;
- `Completo` or `Parziale` from the actual result;
- completed / total syllabus topics;
- estimated remaining minutes when available;
- missing inputs and formula/details.

When `IsComplete == false`, label the value `Punteggio parziale` and expose the missing data. Never convert a null score into 0% or a full progress ring.

Coverage should be a count or percentage derived from `CompletedTopics` and `TotalTopics`. When the denominator is zero or unknown, show `Non disponibile`, not 0%.

## 7. Charts and comparisons

- Prefer a small number of meaningful charts: focus time by week, exam outcomes over time, topic coverage by course.
- Keep axis labels, units, selected period and denominators visible.
- Compare periods only when both periods use the same metric definition and data completeness.
- Avoid smoothing, extrapolation or predictive trend lines unless a dedicated service produces them and the UI labels them as estimates.
- Provide an accessible textual summary of every chart and keyboard-accessible detail values.

## 8. Metric provenance and definitions

Every KPI and chart must have a `Come viene calcolato?` detail or equivalent accessible tooltip containing:
- definition and formula;
- unit and period;
- source records;
- denominator, where applicable;
- handling of missing or excluded values.

Examples:
- weighted average: include only eligible numeric grades and explain the CFU weighting rule;
- acquired CFU: count the valid passed course credits once;
- focus time: sum completed/recorded session durations, not planned blocks;
- review consistency: state the actual due/completed denominator and the date range.

Definitions must match the domain service; the UI must not introduce its own alternative calculation.

## 9. Empty, loading and error states

- No career data: suggest adding a course or recording an exam only when those commands exist.
- Degree total not configured: show acquired CFU without a misleading completion percentage.
- No focus history: explain that recorded sessions will appear here; do not show zero-hour chart axes as progress.
- Incomplete readiness: show partial score and missing inputs.
- No reviews due: say `Nessun ripasso in scadenza`, distinguish it from no review history.
- Loading: preserve card dimensions and use skeletons.
- Calculation failure: preserve previously loaded values only when they are clearly marked as last updated; otherwise show a recoverable error.

## 10. Proposed presentation contract

Names below are proposals; align to actual ViewModels before wiring:
- Career: `CareerOverview`, `CareerSummary`, `ExamAttempts`, `WhatIfBaseline`, `WhatIfResult`, `DegreeTotalCfuConfigured`.
- Study: `FocusSummary`, `FocusHistory`, `ReviewSummary`, `SyllabusCoverageByCourse`, `ExamReadinessByCourse`.
- Planning: `TimeBudgetPlan`, `IsOverCapacity`, `Warnings` (displayed as estimates, not completed progress).
- Common: `SelectedPeriod`, `IsLoading`, `ErrorMessage`, `LastUpdatedAt`.
- Commands: `SelectPeriodCommand`, `OpenCourseProgressCommand`, `SimulateWhatIfCommand`, `ClearSimulationCommand`, `RefreshProgressCommand`.

Do not put aggregate calculations in XAML, converters or code-behind.

## 11. Acceptance criteria

- Career and learning progress are separated but accessible from one coherent destination.
- No single composite score hides the underlying evidence.
- Every metric has a definition, unit and traceable source.
- Partial readiness remains explicitly partial; missing data is visible.
- Planned minutes are never presented as completed focus time.
- What-if results are never persisted or presented as real results without user confirmation.
- Empty, partial, loading and error states are designed.
- UI is usable at 1440×900 and 1024×700, with keyboard navigation and text scaling.
- No demo values, fake charts or inert controls are shipped.
- Visual verification on Windows is required before calling the screen complete.
