# Sprint A — Pomodoro, diario, progressi ed export

## Stato

Branch: `work/freebuff-issue-39-sprint-a`, derivato da
`work/freebuff-issue-39-ledger-review` (commit della Fase 0).
PR di review: [#41](https://github.com/red04yes-cyber/Unitracker/pull/41),
ancora in draft durante la review.

Nessuna UI è stata modificata: niente XAML, code-behind, temi, layout o
`Presentation/Views/**`. I contratti sono pronti per il collegamento UI.

Questa revisione corregge le scadenze del Pomodoro, l’obiettivo settimanale,
il timestamp delle transizioni, gli offset CSV/XLSX, la formula injection, la
privacy delle note di sessione e la durata dei segmenti ancora aperti.

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
- se `CompletePhase`, `SkipPhase` o `Reset` arrivano dopo `PhaseEndsAt`, la
  fase viene prima riconciliata alla scadenza persistita, mai al timestamp del
  click. Il Focus non riceve secondi successivi alla scadenza e il segmento
  Focus non viene spostato in avanti; una pausa già scaduta viene chiusa alla
  propria scadenza, senza avviare un Focus automatico;
- le azioni volontarie prima della scadenza continuano a chiudere la fase al
  timestamp dell’azione;
- le transizioni restituite da `Reconcile` riportano la vecchia scadenza della
  fase, non la scadenza della nuova pausa;
- `Reset` chiude la sessione e conserva la cronologia già completata.

Persistenza: tabelle `PomodoroSettings`, `PomodoroTimer` e `PomodoroCycles`,
con FK cascade verso `StudySessions` e indici su sessione/ciclo e completamento.
Migrazioni: `20261105021044_AddPomodoroAndExportPersistence` e
`20261105120000_AddSessionNotePrivacy`.

### Privacy delle note di sessione

`StudySessionData.NotesArePrivate` ha default conservativo `true`. La colonna
`StudySessions.NotesArePrivate` è aggiunta dalla migrazione incrementale
`20261105120000_AddSessionNotePrivacy` con default `1`: le note legacy non
diventano pubbliche implicitamente. `IncludePrivateNotes` è necessario per
esportare le note private; con `IncludeNotes = true` le note marcate pubbliche
sono incluse, quelle private no. La regola vale per CSV, XLSX e JSON.

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

L’obiettivo settimanale configurato usa `RecordedMinutes` e
`RecordedMinutesRemaining`: comprende quindi le attività manuali e usa i
secondi unici del ledger, non la somma delle attività sovrapposte.
`FocusMinutes` resta la metrica separata delle sessioni e non viene sommato a
`RecordedMinutes`. `WeeklyReviewService` distingue nelle osservazioni tempo
Focus, tempo di studio registrato unico, sovrapposizioni, obiettivo e tempo
residuo; usa il ledger anche per stime e cicli Pomodoro.

## 4. Export CSV, XLSX e JSON

`StudyDataExportService.Export(data, request, format, now)` restituisce un
`StudyExportBundle`. Il CSV è un bundle di file separati:
`activities.csv`, `sessions.csv`, `segments.csv`, `summary.csv` e, se richiesto,
`pomodoro-cycles.csv`. L’XLSX è un workbook Open XML reale con fogli Attività,
Sessioni, Segmenti, Riepilogo e Pomodoro. Il JSON ha `schemaVersion: "1.0"`,
filtri, identificatori stabili e offset espliciti.

Filtri: intervallo, materie, tipi, inclusione cicli Pomodoro, campi, note,
rating, tag, risultato e record eliminati. Le note private sono escluse per
default.

Date e formule:

- nel CSV i casi `DateTimeOffset` e `DateOnly` precedono il ramo generico
  `IFormattable`, quindi le date usano ISO 8601 con offset esplicito e le date
  senza orario usano `yyyy-MM-dd`;
- la protezione dalla formula injection considera anche whitespace e caratteri
  di controllo prima di `=`, `+`, `-` o `@`; i numeri negativi tipizzati restano
  numeri e non vengono prefissati;
- il CSV applica UTF-8 con BOM, quoting e gestione di virgole, virgolette e
  newline;
- l’XLSX mantiene la data tipizzata e aggiunge una colonna testuale `<nome> ISO`
  accanto a ogni timestamp, così l’offset originale è ricostruibile anche con
  offset diversi; il riepilogo include anche `Generato il (ISO)`;
- un segmento ancora aperto conserva `EndedAt = null`, ma la sua durata è
  calcolata fino a `GeneratedAt`, sia nei fogli CSV/XLSX sia nel JSON. Il
  segmento non viene trasformato in uno chiuso.

L’XLSX usa celle inline string e formati numerici per date e durate; non viene
introdotta alcuna dipendenza XLSX esterna. Non vengono esportati token,
credenziali o segreti.

## 5. Verifica

```bash
dotnet test tests/UniToStudyHub.Tests/UniToStudyHub.Tests.csproj -c Release
dotnet build src/UniToStudyHub/UniToStudyHub.csproj -c Release -p:EnableWindowsTargeting=true
```

Risultato: **144/144 test passati**, build WPF Release **0 warning / 0 error**,
`git diff --check` pulito.

Coperti: transizioni focus/pausa, pausa lunga, ripresa da SQLite, fase scaduta
durante chiusura, `CompletePhase`/`SkipPhase`/`Reset` dopo la scadenza, reset
prima della scadenza, timestamp delle transizioni, coerenza segmenti/secondi
accreditati/ciclo, obiettivo settimanale con attività manuali sovrapposte,
quick log e privacy, note di sessione legacy e nuove, secondi unici e
dimensioni, CSV injection con whitespace/control character, offset CSV e
colonne ISO XLSX, segmenti aperti nei tre formati, JSON versionato, round-trip
e migrazione su database con dati preesistenti.

Non eseguiti: runtime WPF su Windows, smoke test visuale e crash injection a
metà transazione. La CI Windows del commit di codice `3645d3f` è verde sia sul
run `push` #20 sia sul run `pull_request` #21: restore, test Release, build WPF
Release, publish self-contained `win-x64` e caricamento dell’artifact sono
completati con successo. La PR #41 resta in draft e non è stata sottoposta a
merge.

## 6. Contratti UI

- Il timer visuale deve interrogare `PomodoroService.Snapshot` e `Reconcile`;
  non deve gestire un countdown separato.
- Dopo una pausa conclusa mostrare “Prossimo focus” e chiamare
  `StartNextFocus`; non presentare un focus automatico come già avviato.
- Usare `RecordedMinutes` come totale unico e mostrare `OverlapSeconds`; non
  sommare `ByCourse`/`ByType`/`ByTopic`.
- `FocusMinutes` può essere mostrato come metrica delle sessioni, ma non
  sommato a `RecordedMinutes`.
- L’obiettivo settimanale mostrato in UI deve usare `RecordedMinutes` e
  `RecordedMinutesRemaining`; `FocusMinutesRemaining` resta solo la metrica
  delle sessioni.
- Le note private richiedono un’azione esplicita (`IncludePrivateNotes`) sia in
  UI sia nell’export. Le note di sessione sono private per default; la UI deve
  esporre `NotesArePrivate` per una scelta esplicita dell’utente.
- I timestamp XLSX hanno sia la cella tipizzata sia la colonna `<nome> ISO`:
  usare quest’ultima per ricostruire l’istante con offset.
- Un segmento aperto ha `EndedAt = null` e durata fino al momento dell’export;
  non va mostrato come completato.
- Il bundle CSV contiene più file: la UI deve salvarli tutti o proporre una
  cartella, non fingere un singolo CSV.

## Final QA (branch `work/freebuff-issue-39-sprint-a-qa`)

Collaudo sul commit `b11f6af`, senza modifiche UI/XAML e senza redesign.

Verifiche eseguite e superate:

- `dotnet test tests/UniToStudyHub.Tests/UniToStudyHub.Tests.csproj -c Release`:
  153 test (144 esistenti + 9 di QA), 0 falliti.
- `dotnet build src/UniToStudyHub/UniToStudyHub.csproj -c Release -p:EnableWindowsTargeting=true`:
  riuscita.
- Migrazione da `20261105011642_AddFocusSessionSegments` allo schema corrente su
  un database file già esistente: il corso preesistente è preservato e le
  tabelle `PomodoroSettings`, `PomodoroTimer`, `PomodoroCycles` vengono create.
- Salvataggio rifiutato (identificativi duplicati): il dataset precedente resta
  intatto, la transazione non sostituisce nulla.
- Pomodoro: segmenti Focus/Pausa separati dopo pausa e ripresa; dopo un riavvio
  la fase scaduta si chiude alla deadline persistita e anche la pausa si chiude
  alla propria scadenza, senza credito di tempo tardivo; `StartNextFocus`
  mantiene una sola riga ledger e continua i segmenti.
- Export: CSV con accenti, virgolette e newline; XLSX con cinque fogli separati e
  colonne ISO per offset espliciti; JSON con `schemaVersion` e note private
  escluse di default, incluse solo con `IncludePrivateNotes`.
- Progressi: intervalli sovrapposti contati una volta, `RecordedSeconds` unico,
  `OverlapSeconds` riportato, totali giornalieri coerenti con il totale.

Correzione emersa dal collaudo:

- `CsvBytes` dichiarava `encoderShouldEmitUTF8Identifier: true`, ma
  `Encoding.GetBytes` non emette il preamble: il BOM UTF-8 non veniva scritto.
  Ora il BOM è anteposto esplicitamente, così Excel legge correttamente accenti e
  caratteri speciali. Coperto da test.

Non eseguito in questo ambiente (Linux, senza runtime WPF):

- avvio e interazione WPF su Windows, pausa/ripresa reale, chiusura e riapertura
  dell'app, interruzione fisica della scrittura su disco, apertura dei file in
  Excel. Queste prove restano al workflow `build-windows.yml` su
  `windows-latest`, che il branch di QA attiva con il push `work/**`.
