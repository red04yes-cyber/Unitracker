# Sprint A — Pomodoro, diario, progressi ed export

## Stato

Branch: `work/freebuff-issue-39-sprint-a`, derivato da
`work/freebuff-issue-39-ledger-review` (commit della Fase 0).

Nessuna UI è stata modificata: niente XAML, code-behind, temi, layout o
`Presentation/Views/**`. I contratti sono pronti per il collegamento UI.

## 1. Pomodoro persistente

Nuovi modelli:

- `PomodoroSettingsData`: durata focus, pausa breve, pausa lunga e numero di
  focus prima della pausa lunga.
- `PomodoroTimerData`: fase (`Focus`, `ShortBreak`, `LongBreak`), stato
  (`Running`, `Paused`, `Completed`), inizio finestra, inizio intervallo attivo,
  scadenza assoluta, secondi residui/attivi, cicli, sessione collegata.
- `PomodoroCycleData`: storico del focus e della pausa, con secondi reali,
  `WasSkipped`, `IsCompleted` e `CompletedAt`.

`PomodoroService` offre `Start`, `Pause`, `Resume`, `StartNextFocus`,
`CompletePhase`, `SkipPhase`, `Reconcile`, `Reset` e `Snapshot`.

Regole:

- il focus usa `FocusSessionService`; le pause sono segmenti `Pause` e non
  aumentano il tempo effettivo;
- una sola voce ledger viene sincronizzata per la sessione collegata, anche con
  sync ripetute;
- la pausa lunga scatta dopo il numero configurato di focus completati;
- dopo la fine di una pausa il timer resta in `Completed` e richiede
  `StartNextFocus`. Non viene avviato automaticamente un nuovo focus mentre
  l’app è chiusa: sarebbe tempo studiato inventato;
- `Reconcile(now)` usa le scadenze assolute persistite. Se l’app era chiusa
  durante il focus, chiude il focus alla scadenza, apre la pausa e, se la pausa
  è già scaduta, la chiude. Non emette notifiche arretrate e non crea un focus
  nuovo;
- `Reset` chiude la sessione e conserva la cronologia già completata.

Persistenza: tabelle `PomodoroSettings`, `PomodoroTimer` e `PomodoroCycles`,
con FK cascade verso `StudySessions` e indici su sessione/ciclo e completamento.
Migrazione: `20261105021044_AddPomodoroAndExportPersistence`.

## 2. Diario e registrazione rapida

`ActivityLedgerService.AddQuickLog` crea una `StudyActivityData` per studio,
esercizi, ripasso, lettura, simulazione o quiz. Accetta materia, argomento,
task, missione, note, risultato, tag, privacy e rating 1–5. Delega ad
`AddManual`, quindi conserva revisione, validazione degli intervalli e soft
delete.

`GetDiary(data, from, to, includePrivate, timeZoneId)` restituisce solo
intervalli reali, con filtro privacy opzionale. Non crea righe per giorni vuoti.

## 3. Progressi e review settimanali

`ActivitySummary` espone anche `ByTopic`. `ProgressSnapshot` espone:

- `RecordedSeconds`/`RecordedMinutes`: unione ledger, senza doppio conteggio;
- `OverlapSeconds`: tempo coperto da almeno due attività;
- `RecordedSecondsByType`, `ByCourse`, `ByTopic`;
- `Daily`: secondi unici, sovrapposizioni, sessioni completate, minuti
  pianificati e cicli Pomodoro per giorno;
- `EstimatedSessionMinutes`, `CompletedTaskEstimatedMinutes`,
  `CompletedPomodoroCycles`;
- `RecordedMinutesRemaining` rispetto all’obiettivo settimanale configurato.

`FocusMinutes` resta la metrica storica delle sessioni e non viene sommato a
`RecordedMinutes`. `WeeklyReviewService` usa il ledger per osservazioni su
tempo unico, sovrapposizioni, stime e cicli Pomodoro.

## 4. Export CSV, XLSX e JSON

`StudyDataExportService.Export(data, request, format, now)` restituisce un
`StudyExportBundle`. Il CSV è un bundle di file separati:
`activities.csv`, `sessions.csv`, `segments.csv`, `summary.csv` e, se richiesto,
`pomodoro-cycles.csv`. L’XLSX è un workbook Open XML reale con fogli Attività,
Sessioni, Segmenti, Riepilogo e Pomodoro. Il JSON ha `schemaVersion: "1.0"`,
filtri, identificatori stabili e offset espliciti.

Filtri: intervallo, materie, tipi, inclusione cicli Pomodoro, campi, note,
rating, tag, risultato e record eliminati. Le note private sono escluse per
default. Il CSV applica UTF-8 con BOM, quoting, gestione di virgole/virgolette/
newline e prefisso `'` per celle che iniziano con `=`, `+`, `-`, `@`, tab o CR.
L’XLSX usa celle inline string e formati numerici per date e durate; non viene
introdotta alcuna dipendenza XLSX esterna. Non vengono esportati token,
credenziali o segreti.

## 5. Verifica

```bash
dotnet test tests/UniToStudyHub.Tests/UniToStudyHub.Tests.csproj -c Release
dotnet build src/UniToStudyHub/UniToStudyHub.csproj -c Release -p:EnableWindowsTargeting=true
```

Risultato: **132/132 test passati**, build WPF Release **0 warning / 0 error**.

Coperti: transizioni focus/pausa, pausa lunga, ripresa da SQLite, fase scaduta
durante chiusura, reset con storico, quick log e privacy, secondi unici e
dimensioni, CSV injection, XLSX reale, JSON versionato, round-trip e migrazione
su database con dati preesistenti.

Non eseguiti: runtime WPF su Windows, smoke test visuale, crash injection a metà
transazione e osservazione della CI Windows. La pipeline è stata aggiornata ma
il risultato GitHub non è ancora stato verificato.

## 6. Contratti UI

- Il timer visuale deve interrogare `PomodoroService.Snapshot` e `Reconcile`;
  non deve gestire un countdown separato.
- Dopo una pausa conclusa mostrare “Prossimo focus” e chiamare
  `StartNextFocus`; non presentare un focus automatico come già avviato.
- Usare `RecordedMinutes` come totale unico e mostrare `OverlapSeconds`; non
  sommare `ByCourse`/`ByType`/`ByTopic`.
- `FocusMinutes` può essere mostrato come metrica delle sessioni, ma non
  sommato a `RecordedMinutes`.
- Le note private richiedono un’azione esplicita (`IncludePrivateNotes`) sia in
  UI sia nell’export.
- Il bundle CSV contiene più file: la UI deve salvarli tutti o proporre una
  cartella, non fingere un singolo CSV.
