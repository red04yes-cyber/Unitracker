# Unitracker — Roadmap

## Stato delle fasi

| Fase | Ambito | Stato |
| --- | --- | --- |
| 1 | Design system, branding e shell visiva | Completata |
| 2 | Presentation layer, navigazione tipizzata e sette viste | Completata; resta il bridge legacy esplicitamente documentato |
| 3 | Confini applicativi per pagina e ownership condivisa dei dati | In corso: `StudyDataSession` è presente; migrazione dei ViewModel e rimozione dei monoliti ancora da completare |
| 4 | Centro notifiche, promemoria Windows, statistiche e motivazione | Progettazione pronta; implementazione subordinata alla chiusura dei writer legacy della Fase 3 |

## Backlog prioritario

### Fase 3 — completamento architetturale

1. Spostare letture e comandi di Dashboard e Courses dietro contratti applicativi dedicati.
2. Migrare Calendar, Study Lab, Academy, Progress e Settings.
3. Fare passare tutti i writer tramite `StudyDataSession` ed eliminare i salvataggi legacy su snapshot.
4. Rimuovere `PageViewModelBase.State`, `MainViewModel` e `StudyWorkspaceViewModel` dopo i test di parità.
5. Verificare refresh cross-page, DI, build e smoke test WPF tramite CI Windows.

### Fase 4 — notifiche e analisi dello studio

1. Persistenza per regole promemoria, occorrenze notifiche, preferenze e obiettivi mensili.
2. Regole automatiche per esami, consegne, sessioni e obiettivo giornaliero.
3. Campanella shell, centro notifiche e gestione promemoria.
4. Notifiche locali/programmate Windows con deep link.
5. Analitiche per materia, costanza, calendario attività, andamento voti, obiettivi mensili e streak non competitiva.

La progettazione dettagliata è in [Phase 4 — Centro notifiche, promemoria e statistiche di studio](architecture/phase-4-notifications-and-progress-analytics.md).

## Vincoli condivisi

- Una sola fonte di verità per calendario, attività e dati di carriera.
- Le nuove operazioni di lettura/scrittura usano i contratti applicativi e `StudyDataSession`; nessun servizio pagina salva snapshot autonomi.
- Migrazioni SQLite additive e compatibili con i dati esistenti.
- Nessuna modifica non richiesta a Moodle, ai formati di backup o alla cifratura.
- Le date proposte dal planner restano distinguibili dagli appuntamenti ufficiali.
- Le statistiche contano attività reali e dichiarano sempre periodo e unità.
