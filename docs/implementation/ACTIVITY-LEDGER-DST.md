# Activity ledger — daylight-saving-time correctness

## Scope

The activity timeline and hourly heatmap must use the selected time zone's real
UTC intervals. A local calendar day can contain 23 or 25 elapsed hours when
daylight saving time changes. Adding hours directly to a `DateTimeOffset` keeps
its original offset and therefore produces wall-clock labels that drift after
the transition.

## Behavior

- Timeline slots advance in UTC, then convert each boundary back to the
  requested time zone. A spring-forward day omits the nonexistent local hour;
  a fall-back day contains both occurrences, distinguished by their UTC offset.
- The heatmap keeps its stable 24 local-hour buckets. A missing spring-forward
  hour has zero recorded seconds. Both fall-back occurrences are combined into
  the same wall-clock-hour bucket, so no elapsed study time is lost.
- Activity intervals continue to be compared as absolute instants. Existing
  overlap and unique-time calculations are unchanged.

## Verification

`ActivityAndMissionTests` covers Europe/Rome transitions in 2026:

- 29 March: a 23-hour timeline, no local 02:00 slot, and heatmap attribution
  to local hours 01:00 and 03:00.
- 25 October: a 25-hour timeline with two local 02:00 slots carrying different
  offsets, and a heatmap that aggregates both occurrences into hour 02.

No UI, persistence schema, external dependency, or AI integration is changed.
