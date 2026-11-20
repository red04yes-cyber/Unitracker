# ADR-0004 — Provenienza e verifica delle date d'esame

- **Data:** 2026-11-20
- **Stato:** Accepted — documenta la baseline già implementata su `main`
- **Ambito:** calendario, importazioni e pianificazione degli esami

## Contesto

Una data inserita manualmente, importata da un calendario o proposta dal planner non è automaticamente un appello ufficiale UniTo. Confondere origine e verifica può portare lo studente a pianificare un esame su una data non confermata.

## Decisione

- Ogni `CalendarEvent` conserva due attributi distinti: `Source` (origine) e `Verification` (stato di verifica).
- Le origini attualmente modellate includono `Manual`, `Moodle`, `ICS` e `Planner`.
- Gli stati modellati includono `Unverified`, `Indicative` e `Official`.
- `CalendarApplicationService.EnsureExamEvent` crea gli eventi derivati da `Exam.PlannedDate` con `Source = Planner` e `Verification = Indicative`, e annota esplicitamente che la data non è un appello ufficiale.
- Un'origine importata o manuale non viene promossa automaticamente a `Official`. Il valore deve essere riservato a una data supportata da una fonte effettivamente verificata.

## Alternative considerate

- **Un solo booleano `IsOfficial`:** rifiutato perché non distingue chi ha prodotto il dato da quanto sia stato verificato.
- **Considerare ufficiale ogni data pianificata:** rifiutato perché il planner produce proposte, non consulta il calendario ufficiale degli appelli.
- **Dedurre l'ufficialità dal nome della categoria `Exam`:** rifiutato perché la categoria descrive il tipo di evento, non la sua provenienza.

## Conseguenze

- La UI e gli export devono preservare e rendere comprensibili origine e verifica.
- Le proposte del planner non devono essere presentate come conferme dell'università.
- Un futuro connettore per appelli ufficiali dovrà documentare la fonte, la data di acquisizione e il criterio che consente di assegnare `Official`.
- L'enum `Official` è soltanto una capacità del modello: non prova che esista già una sincronizzazione ufficiale UniTo.

## Evidenze

- `src/UniToStudyHub/Models/CalendarEvent.cs`
- `src/UniToStudyHub/Services/CalendarApplicationService.cs`
- `tests/UniToStudyHub.Tests/Services/CalendarAndStudyLabTests.cs` (`ExamLinkIsIdempotentAndMarkedIndicative`)
