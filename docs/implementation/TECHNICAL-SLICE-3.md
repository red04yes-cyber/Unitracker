# Freebuff technical slice 3 — Activity ledger e missioni

## Stato

Questa è la prima vertical slice tecnica dell’issue #39, sul branch
`work/freebuff-issue-39-activity`, derivato da `work/freebuff-studyhub-logic`.
La correzione temporale successiva è documentata in
`TECHNICAL-SLICE-3-REVIEW.md`; le sezioni qui sotto sono aggiornate quando
riguardano il comportamento corrente.
Non è un mega-commit delle 60 voci dell’issue e non modifica la UI/UX di ChatGPT.

La UI resta fuori dal perimetro: non sono stati modificati `App.xaml`,
`MainWindow.xaml`, code-behind, temi, layout, navigazione o XAML sotto
`Presentation/Views/**`. La slice espone modelli, persistenza e servizi; i
binding necessari sono in `FREEBUFF-UI-CONTRACTS.md`.

## 1. Activity ledger unificato

`StudyData.StudyActivities` è il registro delle attività realmente svolte. Un
record contiene:

- ID stabile, tipo, titolo, materia, argomento;
- collegamenti opzionali a task, missione, esame, sessione focus ed evento di
  calendario;
- inizio, fine, fuso orario e secondi effettivi;
- origine (`Manual`, `Timer`, `Calendar`, `Import`), stato e risultato;
- valutazioni soggettive facoltative 1–5 per focus, difficoltà, qualità ed
  energia;
- nota, tag, flag `IsPrivate`, revisione e timestamp di creazione/modifica;
- soft delete tramite `DeletedAt`, senza cancellare la storia.

Le attività manuali sono di prima classe: l’app non può conoscere le ore svolte
fuori dall’app. `AddManual` richiede un intervallo valido e calcola i secondi
effettivi dal timestamp se non forniti. Non accetta secondi effettivi superiori
all’intervallo registrato.

### Sincronizzazione con il timer

`ActivityLedgerService.SyncFocusSession(data, session, now)` crea o aggiorna
l’unica attività collegata a una `StudySessionData` tramite `SessionId`:

- chiamate ripetute e riavvii non creano righe duplicate;
- la sessione completata produce un’attività `Completed`;
- una sessione in pausa o in corso produce un’attività `Running`;
- i secondi effettivi usano `FocusSessionService.ElapsedSeconds`, quindi le
  pause sono già escluse;
- lo store rifiuta due record attività collegati alla stessa sessione.

`SyncFocusSessions` applica la sincronizzazione a tutte le sessioni. La UI deve
chiamarla dopo start/pausa/ripresa/fine e dopo il caricamento dell’app, prima di
mostrare i grafici basati sul ledger.

### Correzioni e cancellazione

`Update(data, updated, reason, now)` e `SoftDelete(data, id, reason, now)`
richiedono una motivazione. Ogni modifica manuale crea una
`ActivityRevisionData` con azione, motivo, snapshot JSON prima/dopo e timestamp.
La sincronizzazione automatica del timer non genera una revisione narrativa a
ogni polling; è un aggiornamento dello stesso intervallo, non una correzione
manuale.

Le attività cancellate restano nel database e nelle revisioni, ma sono escluse
da timeline, heatmap, riepiloghi, sovrapposizioni e sync futuro.

## 2. Timeline, heatmap e sovrapposizioni

`ActivityLedgerService.BuildTimeline(data, day, slotMinutes, now, timeZoneId)`
produce una giornata 00:00–24:00 con slot da 15, 30 o 60 minuti. Gli intervalli
sono i segmenti **Focus** della sessione; le pause restano gap e non vengono
conteggiate. Ogni slot espone:

- inizio/fine con offset;
- secondi registrati **unici** (unione degli intervalli, non somma delle
  attività sovrapposte);
- `HasOverlap` e gli ID delle attività presenti;
- secondi non registrati e secondi sovrapposti per la giornata.

La conversione della giornata usa il fuso richiesto; i giorni con cambio
dell’ora possono avere una durata reale diversa da 24 ore. La UI deve mostrare
l’intervallo e il fuso, non assumere sempre 96 slot.

`BuildHeatmap(data, from, to, now, timeZoneId)` aggregasecondi unici per giorno e
ora, con il numero di attività distinte. Il limite è 367 giorni per evitare
aggregazioni non controllate.

`FindOverlaps` restituisce le coppie di attività che condividono un intervallo.
Le sovrapposizioni sono un dato da mostrare, non vengono corrette o spostate
automaticamente.

`Summarize(data, from, to, now, timeZoneId)` usa l’unione degli intervalli focus
delle attività non cancellate e non annullate, con raggruppamenti per tipo e
materia. `RecordedSeconds` non somma le sovrapposizioni; `OverlapSeconds` espone
il tempo condiviso. `ByType` e `ByCourse` sono unioni per dimensione e possono
sommare più di `RecordedSeconds` quando due dimensioni si sovrappongono: la UI
non deve ricavare il totale sommando i gruppi.

Non usa le sessioni come seconda fonte: per evitare doppi conteggi, la UI non
deve sommare `ProgressService.FocusMinutes` e `ActivitySummary.RecordedSeconds`.
L’integrazione del ledger nel `ProgressSnapshot` esistente è una slice
successiva.

## 3. Missioni giornaliere e settimanali

`StudyData.StudyMissions` contiene missioni con:

- titolo, descrizione, materia/argomento, periodo (`Daily`, `Weekly`, `Custom`);
- inizio, scadenza, difficoltà 1–5, stato (`Planned`, `InProgress`, `Completed`,
  `Cancelled`);
- ricompensa volontaria non punitiva e fonte;
- step/checklist con posizione, completamento e timestamp.

`StudyMissionService` offre `Add`, `AddStep`, `SetStepCompleted`, `Complete`,
`Cancel` e `GetForPeriod`. Lo stato passa a `InProgress` quando almeno uno step
è completo e a `Completed` quando tutti gli step lo sono. Una missione senza
step non può essere completata con `Complete`; non viene inventato un
completamento. Non esistono punteggi XP, penalità o ricompense automatiche.

## 4. Persistenza e migrazione

Migrazioni incrementali:

- `20261105005756_AddActivityLedgerAndMissions`;
- `20261105011642_AddFocusSessionSegments`.

La prima aggiunge `StudyActivities`, `ActivityRevisions`, `StudyMissions` e
`MissionSteps` (FK cascade verso la missione). La seconda aggiunge
`StudySessionSegments` con FK cascade verso `StudySessions` e indice
`(SessionId, StartedAt)`: conserva la posizione temporale di focus e pause.

`StudyHubDbContext` configura indici su inizio attività, materia/data, sessione,
stato/scadenza missione e posizione degli step. `StudyDataEntityMapper` fa
round-trip di tag, riferimenti, revisioni e step, con validazione dei riferimenti
e dei rating. `SqliteStudyDataStore` elimina le tabelle figlie prima delle
padri nella stessa transazione.

Test aggiuntivo: un database già migrato fino alla slice 2, con profilo e corso
salvati, viene migrato alla slice 3 senza perdere quei dati.

## 5. Verifica

Comandi eseguiti:

```bash
dotnet test tests/UniToStudyHub.Tests/UniToStudyHub.Tests.csproj -c Release
dotnet build src/UniToStudyHub/UniToStudyHub.csproj -c Release -p:EnableWindowsTargeting=true
```

La suite comprende i test preesistenti più:

- quick-log manuale, correzione con revisione e soft delete;
- sync timer idempotente, senza duplicare i minuti;
- timeline con slot, overlap e tempo non registrato;
- heatmap oraria;
- transizioni degli step di missione;
- rifiuto di secondi effettivi superiori all’intervallo;
- pausa esclusa dalla timeline e dal riepilogo, con posizione verificata;
- pausa/ripresa/fine nello stesso istante senza segmenti a durata zero;
- attività a cavallo della mezzanotte e giornata DST di 23 ore;
- tre sovrapposizioni con secondi unici e `OverlapSeconds`;
- round-trip SQLite di attività, revisioni, missioni e segmenti;
- cascade dei segmenti e rifiuto di segmenti sovrapposti;
- migrazione incrementale con dati utente preesistenti.

Risultato dell’ultima esecuzione: **121/121 test passati**, build WPF Release
senza warning/errori. Il risultato finale viene riconfermato dopo l’ultima
modifica.

## 6. Limiti espliciti

Non sono implementati in questa slice:

- Pomodoro a cicli con pause brevi/lunghe, skip, notifiche e suoni; esiste solo
  il timer focus già presente, non un controller Pomodoro completo;
- notifiche Windows, quick action globale e modalità focus;
- export CSV/XLSX/JSON, preview e protezione formula injection;
- grafici interattivi e schermate WPF: sono UI;
- simulazioni d’esame, question bank avanzata e schermate Accademia: i servizi
  esistenti restano disponibili, ma questa slice non li estende;
- Moodle accordion e token UI: sono UI e integrazione separata;
- integrazione del ledger in `ProgressService` e Weekly Review;
- retention/cancellazione selettiva e cifratura dei backup;
- ricostruzione esatta della posizione delle pause nelle sessioni legacy senza
  segmenti: il fallback inferisce un blocco focus iniziale;
- visualizzazione distinta delle due occorrenze dell’ora ambigua nella heatmap;
- test runtime WPF, smoke test Windows e CI osservata.

La UI non deve presentare queste aree come complete.

## 7. Prossima slice sicura

1. Collegare le schermate Studio Lab a `StudyActivities`/`StudyMissions` e
   chiamare `SyncFocusSessions` dopo il caricamento.
2. Implementare il controller Pomodoro persistente con fasi e break, riusando
   `StudySessionData` per la fase focus.
3. Esportazione CSV/XLSX con anteprima, filtri e formula-injection guard.
4. Integrare il ledger in `ProgressService` e nei grafici, con una sola fonte per
   i minuti.
5. Verifica UI/UX su Windows e smoke test della build pubblicata.

## 8. Contratti per ChatGPT

- **Registro:** `StudyActivities` è la fonte per timeline/heatmap; non creare un
  secondo store JSON.
- **Quick-log:** usare `ActivityLedgerService.AddManual`; mostrare origine,
  materia, tipo, nota, tag e `IsPrivate`.
- **Timer:** usare `FocusSessionService` e poi `SyncFocusSession`; non creare
  manualmente una seconda attività per la stessa sessione. `Segments` distingue
  focus e pause: non usare `EndedAt - StartedAt` come tempo studiato.
- **Legacy:** una sessione senza segmenti non ha una posizione delle pause
  ricostruibile; mostrare il fallback come tale, non come dettaglio esatto.
- **Totali:** usare `RecordedSeconds` per il totale unico e `OverlapSeconds` per
  il tempo condiviso; non sommare `ByType`/`ByCourse` per ottenere il totale.
- **Correzioni:** passare da `Update`/`SoftDelete` con motivazione; mostrare la
  revisione e non cancellare fisicamente la riga.
- **Timeline:** usare `BuildTimeline` e `FindOverlaps`; gli slot non registrati
  sono vuoti reali, non attività inventate.
- **Missioni:** usare `StudyMissionService`; mostrare step, stato e ricompensa
  volontaria senza presentarla come punteggio.
- **Privacy:** `IsPrivate` e le note non devono finire in export o log finché
  non esiste un’anteprima esplicita.
- **Mancanze:** non sommare minuti del ledger e delle sessioni; non mostrare
  Pomodoro, export o Accademia come completati.

La review completa, le formule e i limiti DST sono in
`TECHNICAL-SLICE-3-REVIEW.md`.
