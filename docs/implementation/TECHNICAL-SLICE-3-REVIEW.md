# Review tecnica — Activity ledger e posizione delle pause

## Stato

Branch: `work/freebuff-issue-39-ledger-review`, derivato dal commit `1be9fca`
di `work/freebuff-issue-39-activity`.

Questa review chiude il problema temporale della prima slice dell’issue #39. Non
modifica la UI: nessun XAML, code-behind, tema, layout o file sotto
`Presentation/Views/**` è stato toccato. Non sostituisce la UI di ChatGPT.

## 1. Problema trovato

`ActivityLedgerService.GetRecordedIntervals` usava l’intervallo
`StartedAt`–`EndedAt` (o `now` per un’attività in corso). `EffectiveSeconds`
escludeva le pause dal totale, ma non ne conservava la posizione. Di
conseguenza:

- una sessione 09:00–09:30 con pausa 09:10–09:20 aveva 20 minuti effettivi,
  ma timeline e heatmap potevano mostrare attività anche durante la pausa;
- `Summarize` poteva sommare intervalli sovrapposti;
- un’attività a cavallo della mezzanotte poteva essere associata a un solo
  giorno;
- i riepiloghi per tipo e materia non usavano secondi unici.

La sola sostituzione della durata dell’intervallo con `EffectiveSeconds` non è
sufficiente: senza segmenti temporali la posizione della pausa è perduta.

## 2. Correzione del modello

`StudySessionData.Segments` contiene una sequenza persistita di
`StudySessionSegmentData`:

| Campo | Significato |
|---|---|
| `Id` | identificatore stabile, univoco nel database |
| `Type` | `Focus` oppure `Pause` |
| `StartedAt` | inizio assoluto del segmento |
| `EndedAt` | fine assoluta; `null` solo per il segmento aperto |

`FocusSessionService` apre un segmento `Focus` allo start, lo chiude alla pausa
e apre un segmento `Pause`; alla ripresa chiude la pausa e apre un nuovo focus.
Finish, cancel e reset chiudono il segmento aperto. Le sessioni legacy senza
segmenti restano leggibili.

La persistenza usa la nuova tabella `StudySessionSegments`, collegata a
`StudySessions` con FK `ON DELETE CASCADE` e indice
`(SessionId, StartedAt)`. La migrazione incrementale è:

`20261105011642_AddFocusSessionSegments`

La validazione dello store rifiuta:

- ID segmento mancanti o duplicati;
- segmenti con fine non successiva all’inizio;
- segmenti sovrapposti o più di un segmento aperto;
- una sessione `Running` con pausa aperta;
- una sessione `Paused` con focus aperto.

## 3. Formule temporali

### Intervalli focus

Per un’attività collegata a una sessione con segmenti, gli intervalli sono
esclusivamente i segmenti `Focus`. Le pause sono gap reali. Un segmento focus
ancora aperto termina a `now`; un’attività completata usa la fine del segmento.

Per una sessione legacy senza segmenti, o per un’attività manuale senza
segmenti, si usa un unico intervallo inferito:

```
end = min(EndedAt ?? now, StartedAt + EffectiveSeconds)
```

Questa inferenza è esplicita: la posizione originale delle pause non è
ricostruibile e viene assunta all’inizio dell’intervallo. Non viene presentata
come una ricostruzione esatta.

### Giorno e mezzanotte

`GetForDay`, `BuildTimeline`, `FindOverlaps` e `Summarize` usano i confini
assoluti del giorno nel fuso richiesto. Un’attività che attraversa la
mezzanotte viene tagliata sui due giorni: compare in entrambi senza essere
duplicata nel database. La somma dei due tratti ricostruisce l’intervallo
originale.

### Fusi e DST

I confini sono calcolati con `TimeZoneInfo`, non con offset fissi. Il giorno
del passaggio all’ora legale a Roma ha 23 ore e il `BuildTimeline` espone 23
slot da un’ora, non 24. Il test di regressione usa `Europe/Rome` e verifica
`TotalSeconds == 23 * 3600`.

Limite noto: la heatmap oraria etichetta le celle con l’ora locale del giorno;
nelle ore ambigue del ritorno all’ora solare può aggregare l’ora ripetuta in
una sola etichetta. Il totale dei secondi resta unico; una visualizzazione che
distingue le due occorrenze richiede una futura API con offset esplicito.

### Secondi unici e sovrapposizioni

Per ogni insieme di intervalli:

- `RecordedSeconds` è l’unione degli intervalli, calcolata una sola volta;
- `OverlapSeconds` è il tempo in cui sono attivi almeno due intervalli;
- `ByType` e `ByCourse` usano l’unione degli intervalli del gruppo.

La somma dei gruppi `ByType` o `ByCourse` può quindi superare
`RecordedSeconds` quando attività di gruppi diversi si sovrappongono. Non è un
doppio conteggio: è la stessa ora osservata da due dimensioni. La UI deve
mostrare `OverlapSeconds` e non sommare i gruppi per ricavare il totale.

## 4. Fase 0 — verifica della PR #40

Il test di migrazione incrementale ora parte dalla migrazione
`20261105005756_AddActivityLedgerAndMissions`, inserisce una sessione legacy e
una voce `StudyActivity` già persistite, applica
`20261105011642_AddFocusSessionSegments` e verifica che:

- entrambe le righe siano ancora presenti e leggibili;
- non siano state duplicate o modificate;
- `StudySessionSegments` resti vuota per la sessione legacy;
- `SqliteStudyDataStore.LoadAsync` ricarichi la sessione con `Segments` vuoto.

Il fallback legacy è coperto anche a livello di servizio: una sessione senza
segmenti usa `EffectiveSeconds` per un unico blocco focus iniziale; la timeline
non inventa attività nella parte restante dell’intervallo e la posizione delle
vecchie pause resta esplicitamente non ricostruibile.

La CI Windows `build-windows.yml` ora copre anche le pull request verso
`work/**`, esegue restore e test Release prima della build WPF Release e ha
permessi `contents: read`. Il risultato effettivo della CI non è ancora
osservato: va verificato su GitHub dopo il push.

## 5. Test di regressione aggiunti

- pausa 09:10–09:20 esclusa da timeline e riepilogo, con segmento pausa
  verificato nella posizione corretta;
- pausa, ripresa e fine nello stesso istante non lasciano segmenti di durata
  zero, che la validazione dello store rifiuterebbe;
- attività 23:30–00:30 presente in entrambi i giorni, 30 minuti per giorno;
- giornata DST di 23 ore con 23 slot orari;
- tre attività sovrapposte: 3 ore uniche e 90 minuti di sovrapposizione;
- snapshot JSON `BeforeJson`/`AfterJson` di una correzione;
- soft delete già coperto dalla suite della prima slice;
- sync idempotente già coperto dalla suite della prima slice;
- round-trip SQLite di sessione e segmento focus aperto;
- cancellazione della sessione con cascade sui segmenti;
- rifiuto di segmenti sovrapposti in validazione;
- migrazione incrementale da un database della slice 2, con dati utente
  preesistenti e nuova tabella vuota.

## 6. Verifica reale

Eseguiti:

```bash
dotnet test tests/UniToStudyHub.Tests/UniToStudyHub.Tests.csproj -c Release
dotnet build src/UniToStudyHub/UniToStudyHub.csproj -c Release -p:EnableWindowsTargeting=true
```

Risultato: **122/122 test passati**, build WPF Release **0 warning / 0 error**.

Non eseguibili in questo ambiente Linux:

- runtime WPF e smoke test su Windows;
- verifica della CI Windows;
- prova di crash del processo durante una transazione SQLite. Lo store usa una
  transazione, ma non è stato simulato un crash a metà scrittura.

## 7. Contratti UI aggiornati

- `StudySessionData.Segments` è la fonte per distinguere focus e pause.
- `FocusSnapshot` continua a esporre `ElapsedSeconds` e `RemainingSeconds`;
  non usare `EndedAt - StartedAt` come tempo studiato.
- `ActivityTimeline` e `ActivityHeatmap` usano solo segmenti focus.
- `ActivitySummary.RecordedSeconds` è il totale unico; `OverlapSeconds` è il
  tempo condiviso; `ByType`/`ByCourse` sono aggregazioni per dimensione.
- Per le sessioni legacy mostrare un avviso se si visualizza la posizione delle
  pause: non è ricostruibile.
- Non sommare `ProgressService.FocusMinutes`, `EffectiveSeconds` e
  `ActivitySummary.RecordedSeconds` come se fossero grandezze indipendenti.
