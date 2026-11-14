# Calendar insights — 14 novembre 2026

## Ambito

La nuova `CalendarInsightsService` analizza la sorgente calendario già esistente (`StudyData.CalendarEvents`). Non crea
un secondo archivio, non sposta eventi e non salva proposte: restituisce un risultato immutabile da usare nei binding
WPF o in altri casi d'uso.

Il servizio è registrato come singleton in `CompositionRoot`.

## Funzionalità

### Analisi completa

`AnalyzeRange(events, start, endExclusive, now, minimumFreeWindow, displayTimeZone, includeAllDayEvents, upcomingDeadlineLimit)`
restituisce `CalendarRangeAnalysis` con:

- eventi attivi che intersecano il periodo;
- coppie in conflitto e durata esatta della sovrapposizione;
- intervalli liberi con durata minima configurabile;
- riepiloghi giornalieri;
- scadenze imminenti ordinate;
- prossimo evento, includendo un evento già in corso.

### Query riutilizzabili

- `FindConflicts(...)`: rileva sovrapposizioni tra eventi non annullati.
- `GetFreeWindows(...)`: calcola il complemento degli impegni e unisce intervalli sovrapposti o adiacenti.
- `GetDailySummaries(...)`: calcola il tempo occupato come unione degli intervalli, senza doppio conteggio.
- `GetUpcomingDeadlines(...)`: filtra la categoria Deadline e applica un limite esplicito.
- `GetNextEvent(...)`: restituisce l'evento in corso o il prossimo evento non terminato.

## Regole temporali e limiti

- Tutti gli intervalli sono semiaperti `[inizio, fine)`: due eventi consecutivi che si toccano non sono in conflitto.
- I confronti avvengono sugli istanti `DateTimeOffset`, non confrontando l'ora locale testuale.
- Gli eventi annullati o con intervallo non valido vengono ignorati.
- Gli eventi all-day non occupano automaticamente tutto il tempo disponibile: per includerli nei conflitti, nel tempo occupato e nel calcolo degli slot liberi passare `includeAllDayEvents: true`. Restano comunque visibili nel conteggio degli eventi e nelle scadenze.
- I riepiloghi sono calcolati nel fuso orario richiesto; una giornata può durare 23 o 25 ore nei cambi DST. Le date civili saltate da un cambio di fuso non generano tempo occupato fittizio.
- Un range può contenere al massimo 10.000 eventi attivi; la ricerca conflitti si ferma con errore esplicito oltre 25.000 coppie; i riepiloghi giornalieri coprono al massimo 366 giorni; le scadenze restituite sono limitate a 100. Questi limiti impediscono che importazioni locali anomale causino una crescita incontrollata del lavoro.
- Le finestre libere sono opportunità temporali, non raccomandazioni né prenotazioni. Il planner mantiene la propria logica di preview/apply e conferma.

## Contratto neutro per la UI

Un ViewModel può risolvere `CalendarInsightsService` tramite DI e pubblicare `CalendarRangeAnalysis` come stato di sola
lettura. La UI può mostrare conflitti con i due titoli e l'intervallo condiviso, finestre libere con fuso orario e
durata, riepiloghi con busy/available time, scadenze e prossimo evento. Deve mantenere distinta la provenienza
(`Manual`, `ICS`, `Moodle`, `Planner`) e la verifica (`Official`, `Indicative`, `Unverified`).

Non sono stati modificati XAML, code-behind, temi o layout: `AGENTS.md` assegna questi file al workstream UI dedicato.
Il contratto è pronto per essere collegato alla schermata calendario senza introdurre una seconda sorgente dati.

## Test

`CalendarInsightsServiceTests` copre sovrapposizioni, intervalli adiacenti, eventi annullati, all-day, offset differenti,
merge e clipping degli slot liberi, durata occupata senza doppio conteggio, DST Europe/Rome, scadenze, prossimo evento,
analisi aggregata, limiti di input e limite massimo delle coppie. `CompositionRootTests` verifica la registrazione
singleton nel contenitore applicativo.

La build e la suite completa devono essere confermate dal workflow Windows della PR; non viene dichiarata una verifica
locale finché non è stata effettivamente eseguita.
