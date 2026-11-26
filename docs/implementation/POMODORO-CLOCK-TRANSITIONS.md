# Pomodoro clock-transition safety

## Scope

The persisted Pomodoro state machine uses DateTimeOffset wall-clock timestamps so it can reconstruct a phase after the application or Windows restarts. This slice hardens transitions when the supplied/current clock moves before a persisted phase or session transition.

## Behavior

- Pause is rejected before the current phase start.
- Resume is rejected before the persisted pause transition.
- Completing/skipping a phase is rejected before its active interval starts.
- Reset validates the phase and linked focus-session timestamps before modifying cycle history or closing the session.
- Starting the next focus requires the completed break timestamp, not merely the earlier time at which the linked focus session was paused.
- The linked focus session is transitioned before the timer is mutated, so a rejected focus-session timestamp cannot leave the timer and session in different states.
- New phase deadlines are calculated before creating or resuming a session. An out-of-range DateTimeOffset therefore cannot leave an orphan or partially resumed focus session.
- Snapshot elapsed time is clamped to the configured phase duration. The calculation compares the elapsed interval to the remaining capacity before converting to int, avoiding overflow after extreme wall-clock jumps.

## Limits

The timer still stores wall-clock timestamps, not a boot-aware monotonic clock. A clock adjustment that moves backwards but remains after the current phase start can still make the displayed remaining time increase until the next persisted transition. The changes here prevent invalid transitions and data corruption; they do not claim to measure monotonic elapsed time across Windows clock changes or restarts. A fully monotonic design would require a separately persisted, restart-aware time source and a migration.

## Verification

PomodoroClockRollbackTests covers focus and break pause/resume rollback, complete/skip/reset before a phase starts, starting the next focus before the break completes, extreme timestamp differences, and deadline overflow. The Windows .NET 10 build and security workflows remain the verification gate.
