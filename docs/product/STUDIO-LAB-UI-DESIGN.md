# Studio Lab — UI/UX Design Specification

Branch: `work/study-lab-uiux`
Scope: WPF presentation only. Study logic, storage, timers, scheduling and domain rules remain the technical implementation's responsibility.

## Product direction
Studio Lab is a focused workspace for doing the next useful study action. The interface should feel encouraging, calm, premium and purposeful—not gamified to the point of distraction. Keep the existing StudyHub palette: navy `#17243B`, blue `#3157D5`, pale canvas `#F3F6FB`, white surfaces, restrained borders and Segoe UI Variable. Light theme first.

## Information architecture
1. **Focus** — active focus session, task context and quick start.
2. **Plan** — today's study blocks and missions.
3. **Recall** — flashcards and due reviews.
4. **Errors** — personal error journal and corrective actions.
5. **Habits** — focus history, streaks and energy check-ins.

Do not create separate navigation destinations unless the existing shell supports them; sub-navigation within the Studio Lab page is preferred.

## Main workspace
- Header eyebrow `STUDIO LAB`, title `Il tuo spazio di studio`, subtitle `Meno attrito. Più attenzione su ciò che conta.`
- Prominent focus card with task/course context, elapsed/remaining time, clear session state and primary action.
- A restrained circular or linear timer visualization. Avoid excessive motion; respect reduced-motion/accessibility preferences.
- Session presets (for example 25/5, 50/10, custom) must reflect actual configured values and not be decorative.
- Primary controls: Start, Pause, Resume, Finish. Confirm before discarding an active session.
- Session completion state offers a short break, a reflection/check-in or return to plan; do not force all steps.
- If no active task exists, present a clear empty state and an action to select/create a study task when supported.

## Today's plan and missions
- Compact agenda of planned study blocks with time, course, objective and state.
- Mission cards communicate one concrete action, estimated duration and progress.
- Distinguish scheduled items from suggested items; never label an AI-generated or estimated plan as official.
- Completion feedback should be subtle and accessible; no confetti or noisy effects.
- Show overdue or blocked status clearly, with an actionable explanation.

## Recall / spaced repetition
- Review queue summary: due now, upcoming and completed today, sourced from actual review data.
- Flashcard study surface with question first, reveal answer, then confidence/recall rating controls.
- Rating labels must be understandable (e.g. `Di nuovo`, `Difficile`, `Bene`, `Facile`) and map to the real scheduling contract.
- Show progress through the current queue and a leave-session confirmation if progress could be lost.
- No invented card content or fake queue counts.

## Error journal
- List/search/filter errors by course, topic, type and review state, only where backed by data.
- Detail panel: question/context, user's incorrect reasoning, correct explanation, source/reference and next review.
- Add/edit error flow with inline validation and a clear saved/error state.
- Link an error to a flashcard/review only when the application supports that relation.

## Energy and distractions
- Optional short check-in for energy/focus before or after a session; use simple accessible labels and not diagnostic language.
- Distraction capture should be frictionless: record a short item and return to focus without navigating away.
- Display captured distractions as a private list with a clear resolve/archive action.
- Avoid shaming language or punitive streak-loss UI.

## History and progress
- Focus history grouped by day/week with session duration and associated subject.
- Small, legible summaries: focused time, completed sessions, review consistency.
- Trends must be calculated from real stored records; avoid fake chart data.
- Streaks are optional, explain their definition and do not imply that missed days erase learning.

## Visual system and layout
- Desktop target: 1440×900; also readable at 1024×700.
- Use 12 px card radius, fine border `#E4EAF2`, minimal shadow, 20–24 px padding.
- Main focus card gets strongest hierarchy; supporting cards are quieter.
- Accent colors are semantic and paired with text/icons; never rely on color alone.
- Responsive behavior: at smaller widths stack cards, collapse secondary navigation and keep the timer/control actions reachable.
- Keyboard-first navigation, visible focus rings, accessible names, Windows text scaling and high contrast support.

## States
Design loading, no task, no reviews due, empty error journal, active/paused/completed timer, save failure, unavailable feature, offline/cached state and recoverable sync errors.
Preserve user drafts and active-session context on recoverable errors. Avoid inert buttons and placeholder metrics.

## MVVM binding contract (proposed; coordinate with technical branch)
Presentation concepts: `ActiveSession`, `SessionState`, `RemainingTime`, `SelectedTask`, `TodayPlan`, `Missions`, `DueReviews`, `FocusHistory`, `EnergyCheckIn`, `Distractions`, `Errors`, `IsLoading`, `ErrorMessage`.
Commands: `StartSessionCommand`, `PauseSessionCommand`, `ResumeSessionCommand`, `FinishSessionCommand`, `SelectTaskCommand`, `CompleteMissionCommand`, `RevealAnswerCommand`, `RateRecallCommand`, `SaveErrorCommand`, `AddDistractionCommand`, `SaveEnergyCheckInCommand`.
These are proposals, not assumptions: align to the actual ViewModel contract before wiring. Keep timer lifecycle, persistence, spaced repetition calculations and domain rules out of XAML/code-behind.

## Acceptance criteria
- The main screen prioritizes the active study action and has clear visual hierarchy.
- Timer states are explicit and commands bind to real functionality.
- Reviews and missions display real data only.
- Empty, loading, error and completed states are included.
- UI remains usable at 1024×700 and with keyboard navigation.
- No fake/demo metrics, fake flashcards or inert actions.
- No edits to unrelated shell/global theme/other views.
- Visual verification on Windows is required before calling the UI complete.
