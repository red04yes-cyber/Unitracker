# University planner — implementazione tecnica

## Scope e stato

Branch: `work/freebuff-university-planner`, derivato da `b11f6af` (base stabile
dopo lo Sprint A). Nessun merge automatico.

Questa slice implementa il nucleo reale del pianificatore universitario:
calendario e timeline, disponibilità, piano deterministico, argomenti,
riprogrammazione, timer collegato, analytics, dashboard e persistenza SQLite.
La UI visuale non è stata modificata: `AGENTS.md` assegna XAML, temi, layout,
navigazione e `Presentation/Views/**` al proprietario UI. I contratti per quella
integrazione sono in `FREEBUFF-UI-CONTRACTS.md`.

Non vengono introdotti AI, dati demo, percentuali inventate, un secondo database
o un archivio JSON alternativo. `StudyData` e SQLite restano l’unica fonte di
verità.

## Architettura

I servizi sono registrati in `CompositionRoot`:

- `UniversityPlannerService`: generazione piano, riprogrammazione, revert,
  completamento sessioni, focus collegato e dashboard.
- `StudyAnalyticsService`: metriche aggregate da ledger, calendario, sessioni,
  task, missioni e argomenti.
- `CalendarTimelineService`: timeline di una giornata reale, con clipping a
  mezzanotte e blocchi liberi.
- `CalendarService`, `FocusSessionService`, `ActivityLedgerService` e
  `ProgressService` restano i servizi esistenti; il planner li compone, non li
  duplica.

Le sessioni pianificate continuano a essere `CalendarEvent` con metadati
`PlanId`, `SyllabusTopicId`, `PlannerStatus`, `ActualMinutes`, `PriorityScore`,
`RescheduleCount` e `CompletedAt`. Non esiste un calendario parallelo.

## Modello dati

### `PlannerSettingsData`

Preferenze esplicite, persistite nella tabella `PlannerSettings`:

- durata preferita e minima della sessione;
- pausa tra sessioni;
- massimo sessioni al giorno;
- percentuale e giorni di riserva per il ripasso;
- inclusione del fine settimana;
- riprogrammazione automatica delle sessioni scadute;
- finestre settimanali `PlannerAvailabilityWindowData` (giorno, ora locale di
  inizio/fine, abilitata).

Un elenco di disponibilità vuoto è uno stato reale: il planner segnala che non
può pianificare e non inventa orari.

### `StudyPlanData`

Conserva materia, esame, data, minuti richiesti, riserva, minuti pianificati,
stato (`Draft`, `Active`, `InsufficientTime`, `Completed`, `Archived`),
spiegazione, warning e timestamp. I warning sono persistiti, non ricostruiti
dalla UI.

### Argomenti

`SyllabusTopicData` è esteso con:

- `ParentTopicId` e `Position` per gerarchia e ordinamento;
- `Difficulty`, `PreparationLevel`, `Priority` (1–5);
- `IsArchived`;
- stato `NeedsReview` oltre a `NotStarted`, `InProgress`, `Completed`.

Il tempo effettivo non viene scritto sull’argomento: deriva dalle sessioni
pianificate completate collegate, quindi non è una stima presentata come fatto.

### Riprogrammazione

`PlannerRescheduleData` è uno storico append-only: sessione, piano, intervallo
precedente, nuovo intervallo, motivo, timestamp, `IsReversed` e `ReversedAt`.
Nessuna riga viene cancellata durante un revert.

### Migrazione

La migrazione incrementale è
`20261109153614_AddUniversityPlanner`. Aggiunge le colonne planner, le tabelle
`PlannerSettings`, `StudyPlans`, `PlannerReschedules` e gli indici di ricerca.
I dati preesistenti restano invariati; i nuovi campi hanno default conservativi.
Lo snapshot EF è stato riallineato e `dotnet ef migrations
has-pending-model-changes` non rileva modifiche pendenti.

## Algoritmo deterministico

### Input e validazione

`CreatePlan(data, request, now)` richiede una materia esistente e una data esame
(da `request.ExamDate` o dall’esame pianificato). Se viene indicato un esame,
deve appartenere alla materia. Gli argomenti predefiniti sono quelli non
archiviati, non completati e con stima positiva; `TopicIds` può restringere
l’insieme e un id mancante o non valido produce un errore esplicito.

Le preferenze vengono validate: durata preferita 15–240 minuti, durata minima
10–240 e non superiore alla preferita, pausa 0–60, massimo 1–12 sessioni al
giorno, riserva 0–100%, giorni di riserva 0–30 e al massimo 28 finestre.

### Formula di priorità

```
punteggio = difficoltà × 3
          + (6 − preparazione) × 2
          + priorità
```

Punteggio più alto viene schedulato prima. Difficoltà e priorità più alte e
preparazione più bassa aumentano il punteggio. I valori mancanti o fuori scala
sono trattati come 3; l’app usa 3 anche come default dei nuovi argomenti. La
formula è riportata in `StudyPlanData.Explanation` e non viene ricalcolata dalla
UI con una formula diversa.

### Capacità e finestre

1. Il fabbisogno è la somma delle stime degli argomenti selezionati.
2. La riserva di ripasso è `ceil(fabbisogno × percentuale / 100)`.
3. L’ultimo giorno utile è `data esame − giorni di riserva − 1`.
4. Si usano solo le finestre di disponibilità del giorno corrispondente,
   eventualmente escludendo sabato e domenica.
5. Le finestre vengono ritagliate su `now` e unite se si sovrappongono.
6. Gli eventi di calendario non cancellati vengono espansi con la pausa
   configurata; lezioni, esami, scadenze e attività personali sottraggono quindi
   tempo reale.
7. Il carico giornaliero residuo parte da `MaxDailyStudyMinutes` e sottrae le
   sessioni planner già presenti. Anche `MaxSessionsPerDay` conta le sessioni
   planner già presenti.
8. Le sessioni rispettano la pausa e non vengono create in ore già occupate.

La durata di ogni blocco è il minimo tra durata preferita, minuti residui
dell’argomento, spazio disponibile e capacità giornaliera. Una coda finale più
corta della durata minima è ammessa solo se esaurisce davvero l’argomento;
altrimenti la finestra non viene usata per evitare sessioni arbitrarie troppo
brevi.

### Esito

Se il totale pianificato, riserva inclusa, non entra prima del margine:

- il piano diventa `InsufficientTime`;
- non viene creato alcun evento;
- viene registrato il deficit in minuti e un warning che spiega cosa manca;
- `PlannerResult.DeficitMinutes` è il valore mostrato all’utente.

Se non ci sono argomenti pianificabili, viene registrato un piano
`InsufficientTime` con spiegazione, senza eventi. Non viene mai creato un piano
“verde” con sessioni parziali spacciate per complete.

### Date, fusi orari e mezzanotte

Le finestre sono ore locali della macchina e vengono convertite con l’offset
locale della data, non assumendo giorni di 24 ore. Gli eventi conservano il
proprio `TimeZoneId`; la timeline usa il fuso richiesto (default locale) e
calcola l’inizio/fine giornata con l’offset reale, inclusi i giorni DST. Gli
eventi che attraversano la mezzanotte vengono ritagliati sul giorno mostrato,
mantenendo durata visibile e flag di clipping.

## Riprogrammazione

`RescheduleMissed(data, now, apply)` considera solo sessioni:

- non cancellate, con `Source == Planner`, `PlannerStatus == Scheduled` e
  `EndsAt < now`;
- collegate a un piano ancora `Active`.

Con `apply == false` restituisce solo proposte e non modifica nulla. Con
`apply == true` (o col default `AutoRescheduleMissed`):

1. marca la sessione `Missed`;
2. cerca il primo slot libero dalla giornata corrente fino all’ultimo giorno
   utile, rispettando disponibilità, weekend, capacità giornaliera, pausa,
   fuso e collisioni;
3. se lo trova, aggiorna l’evento, incrementa `RescheduleCount`, crea una riga di
   storico e mantiene lo stato `Scheduled`;
4. se non lo trova, imposta `NeedsAttention`, aggiunge un warning al piano e
   lascia decidere all’utente.

Lezioni, esami, scadenze e attività personali non vengono mai spostati. Le
sessioni completate non vengono toccate. Lo slot viene scelto usando anche le
altre sessioni spostate nella stessa chiamata, evitando duplicati e
sovrapposizioni all’interno del batch.

`RevertReschedule(data, historyId, now)` ripristina l’intervallo precedente solo
se la sessione è ancora esattamente nel nuovo intervallo e quello vecchio è
ancora libero. Lo storico resta e viene marcato `IsReversed`; se il vecchio
intervallo è già passato, la sessione torna `Missed`, non `Completed`.

## Timer collegato

`StartFocus(data, calendarEventId, now)` usa il `FocusSessionService` esistente:
la sessione è persistita con timestamp assoluti, stato, segmenti focus/pausa e
secondi accumulati. Sul focus vengono salvati `CalendarEventId` e
`SyllabusTopicId`, quindi il tempo è ricostruibile dopo un riavvio.

`FinishFocus` chiude il focus, sincronizza il ledger in modo idempotente e
calcola i minuti effettivi dalla sessione persistita, non da un contatore in
memoria. `CompletePlannedSession` registra `ActualMinutes` e `CompletedAt` e
aggiorna l’argomento solo dai dati reali. `CancelFocus` sincronizza il ledger e
marca la sessione pianificata `NeedsAttention`, senza dichiararla completata.

## Analytics

`StudyAnalyticsService.Calculate` produce oggi, settimana e mese. Ogni snapshot
contiene:

- minuti registrati dal ledger (unione degli intervalli, senza doppio conteggio);
- minuti pianificati e minuti effettivi delle sessioni planner completate;
- sessioni completate e interrotte;
- task e missioni completati, flashcard in scadenza, errori aperti;
- giorni di studio, giorni trascorsi e costanza;
- obiettivo settimanale;
- dettaglio giornaliero e per materia.

`PlannedVsActualMinutes = ActualPlannerMinutes − PlannedMinutes`; non usa il
tempo registrato dal ledger. `RecordedMinutes` e i minuti pianificati restano
metriche separate. Le percentuali di completamento degli argomenti derivano da
stati e stime persistiti, non da valori casuali.

## Dashboard

`BuildDashboard` restituisce:

- eventi di oggi;
- prossimi eventi nei successivi 30 giorni;
- prossime sessioni pianificate;
- prossimo esame non completato;
- avanzamento per materia (argomenti completati, stima residua, minuti
  pianificati e completati);
- minuti registrati nella settimana;
- warning dei piani attivi o insufficienti.

È un contratto dati: non impone layout e non modifica la navigazione.

## Persistenza e integrità

Il salvataggio SQLite resta transazionale. Le nuove tabelle vengono cancellate
nell’ordine corretto prima di un replace completo e vengono riscritte solo dopo
la validazione. I test verificano:

- database vuoto e migrazione completa;
- round-trip di impostazioni, piano, evento planner, storico e focus;
- migrazione incrementale da un database con dati legacy;
- default conservativi per campi nuovi;
- assenza di modifiche pendenti al modello EF.

## Verifica

Comandi eseguiti:

```bash
dotnet test tests/UniToStudyHub.Tests/UniToStudyHub.Tests.csproj -c Release --no-restore
dotnet build src/UniToStudyHub/UniToStudyHub.csproj -c Release -p:EnableWindowsTargeting=true --no-restore
dotnet ef migrations has-pending-model-changes --project tests/UniToStudyHub.Tests/UniToStudyHub.Tests.csproj --startup-project tests/UniToStudyHub.Tests/UniToStudyHub.Tests.csproj
git diff --check
```

Risultati da riportare nel commit/PR:

- suite completa: **153/153 test passati** (9 test planner, inclusi round-trip e
  migrazione legacy);
- build WPF Release: **0 warning, 0 errori**;
- EF: nessuna modifica pendente;
- `git diff --check`: pulito.

I test planner coprono piano fattibile, deficit senza eventi, priorità,
riprogrammazione con storico/revert, timer e ledger senza duplicazione, timeline
con evento a cavallo della mezzanotte, analytics pianificato/effettivo,
persistenza e migrazione incrementale. Non è stato eseguito il runtime WPF su
Windows: la build è una verifica statica del progetto Windows.

## Limiti noti e prossima slice

- UI/UX, XAML, temi, navigazione e viste non sono stati modificati per vincolo
  di proprietà; i contratti sono pronti per il proprietario UI.
- La ricorrenza degli eventi è rappresentata dal campo `RecurrenceRule` e
  dall’import ICS esistente; questa slice non introduce un motore di ricorrenza
  separato né modifica il comportamento dell’import.
- La pianificazione usa la disponibilità e il fuso locali della macchina. Una
  selezione esplicita di un fuso diverso è un’estensione futura, non un valore
  assunto qui.
- Il planner non sposta esami o lezioni e non cancella attività completate; la
  riprogrammazione è sempre una proposta applicabile e tracciata.
- La verifica end-to-end visuale e il comportamento su Windows restano da
  eseguire nella fase UI.
