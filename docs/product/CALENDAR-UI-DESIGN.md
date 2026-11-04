# Calendar — UI/UX Design Specification

> Branch: `work/calendar-uiux`
> Scope: presentation only (WPF / .NET 10 / MVVM).
> Owner: ChatGPT UI/UX. Application logic, persistence, ICS and Moodle integrations remain owned by the technical implementation branch.

## 1. Product intent

The calendar is the user's operational timeline: lectures, exam sessions, deadlines and planned study blocks in one coherent place. It must feel calm, precise and premium—not like a dense administrative timetable.

The interface must never fabricate events, exam dates, sync status or availability. Every event shown must come from a real application binding. Indicative planner suggestions must be visually and textually distinct from official dates.

## 2. Visual direction

- Light theme first; preserve the existing StudyHub semantic palette.
- Page background: `#F3F6FB`; surfaces: `#FFFFFF`; primary ink/navy: `#17243B`; accent: `#3157D5`; muted text: `#718096`; border: `#E4EAF2`.
- Use Segoe UI Variable, with 28–30 px page title, 14–15 px primary text and 11–12 px metadata.
- Cards: 12 px radius, hairline border, minimal shadow, 20–24 px internal padding.
- Keep the canvas airy: a compact toolbar, clear weekly grid, restrained event chips and ample whitespace.
- Category colors are semantic accents, not large saturated fills: exams (blue), lectures (teal), deadlines (amber), study blocks (violet), personal (slate). Always pair color with a text label or icon.
- Keyboard focus, hover and selected states must be visible and consistent with the shell.

## 3. Page hierarchy

### Header
- Eyebrow: `PIANIFICAZIONE`.
- Title: `Calendario`.
- Subtitle: `Lezioni, appelli e tempo dedicato allo studio, in un'unica vista.`
- Primary action: `+ Nuovo evento` (enabled only when the create-event use case is wired).
- Secondary action: `Oggi`.

### Toolbar
- Previous / next period controls.
- Human-readable period label (for example, `4–10 novembre 2026`).
- Segmented view selector: `Settimana`, `Mese`, `Giorno`.
- Search and filters only when backed by actual ViewModel properties.
- A compact sync/source status area; show the last successful sync timestamp only when supplied by the service.

### Weekly view (default)
- Seven day columns, Monday to Sunday, localized in Italian.
- Sticky day/date header and a distinct current-day marker.
- Time gutter with a configurable visible range; default working/study range 08:00–20:00.
- Subtle hour separators and stronger separators at 09:00, 13:00 and 18:00.
- Event cards positioned by their actual start/end times; support multi-hour height and overlap lanes.
- All-day events occupy a separate top row.
- Current-time line only when the local clock is available to the presentation layer.
- On narrow windows, preserve usability with horizontal day navigation rather than compressing text below legibility.

### Month view
- Seven-column month grid with compact event indicators and up to two readable event labels per day.
- Overflow indicator opens that day's agenda.
- Selected day shows a concise agenda panel.
- Events spanning multiple days must read as continuous, not duplicated independent events.

### Day view
- Clear day heading, all-day strip and detailed time rail.
- Agenda list alternative for accessibility and smaller windows.

## 4. Event presentation

Every event card should show, when available:
- title;
- start/end time;
- course or subject;
- location;
- event category;
- source/verification state.

Use a small left accent and a quiet tinted surface. Avoid giant colored blocks. Official exams must have a clear `Appello ufficiale` label; indicative suggestions must say `Pianificazione indicativa`. Imported items should expose their source in the detail panel.

## 5. Event detail and editing

Use a right-side detail panel on desktop (or modal on constrained widths), with:
- title and category;
- date and time;
- course association;
- location and notes;
- source, last updated and verification state;
- recurrence summary where supported.

Create/edit form fields must be driven by the application contract. Required-field validation appears inline. Destructive actions require confirmation and explain whether linked/imported data will be affected. Do not display controls that have no working command.

## 6. Filters and sources

Filter chips:
- Tutti
- Lezioni
- Appelli
- Scadenze
- Studio
- Personali

Optional source filters (only when supported):
- Manuale
- Moodle
- File ICS

Filters must be combinable, clearable and reflected in the visible event count. Do not imply that Moodle or ICS is connected until a real connection/import exists.

## 7. Empty, loading and error states

- Empty calendar: calm illustration-free state with a short explanation and a real action to add/import an event, if available.
- No events in selected period: `Nessun evento in questo periodo`, with navigation to adjacent periods.
- Loading: preserve page geometry and use skeleton rows/cards.
- Syncing: show source-specific progress and disable only conflicting actions.
- Sync failed: explain which source failed, preserve local data and offer retry if the service supports it.
- Save failed: keep the user's draft and show a recoverable error.
- Offline: clearly distinguish cached local events from unavailable remote data.

## 8. Accessibility and responsive behavior

- Full keyboard navigation for toolbar, grid, event cards and dialogs.
- Do not rely on color alone; expose category/source as text or accessible automation name.
- Respect Windows text scaling and high contrast.
- At widths below the reference desktop size, collapse secondary filters into a menu and switch the calendar to a 3-day or agenda layout; never force unreadable seven-column content.
- At minimum window size, keep the primary action and period navigation reachable without horizontal scrolling.

## 9. MVVM presentation contract

The View should bind to a dedicated CalendarViewModel, not grow the existing MainViewModel into a monolith.

Expected presentation-facing concepts (names are proposals; align with the technical branch before wiring):
- `VisibleEvents`, `SelectedDate`, `DisplayedPeriodLabel`
- `CurrentView` (Day / Week / Month)
- `SelectedEvent`
- `AvailableCategories`, `SelectedCategories`
- `IsLoading`, `IsSyncing`, `EmptyStateMessage`, `ErrorMessage`
- Commands: `PreviousPeriodCommand`, `NextPeriodCommand`, `GoToTodayCommand`, `SetViewCommand`, `SelectEventCommand`, `CreateEventCommand`, `EditEventCommand`, `DeleteEventCommand`, `RefreshCommand`

The technical implementation must confirm the actual contract before XAML bindings are connected. Do not add persistence, recurrence calculation, ICS parsing, Moodle access or domain rules to the View or code-behind.

## 10. Acceptance criteria

- Weekly view is the default and is readable at 1440×900 and 1024×700.
- Day/week/month navigation is consistent and preserves the selected date.
- Real events render at correct dates and times; overlap and all-day events are handled.
- Official and indicative dates are visually distinguishable.
- Empty, loading, error and offline states are designed.
- Keyboard focus and screen-reader labels are present.
- No fake/demo events or inert buttons are shipped.
- New UI files do not alter the existing shell, global theme or unrelated views.
- Visual verification on Windows is required before marking the UI complete.
