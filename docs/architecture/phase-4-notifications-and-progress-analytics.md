# Phase 4 — Centro notifiche, promemoria e statistiche di studio

**Stato:** proposta architetturale pronta per revisione. Non ancora implementata.

## Obiettivo

Aggiungere a Unitracker due capacità coordinate:

1. Un centro notifiche con promemoria automatici e personalizzabili, collegato a esami, consegne, sessioni di studio e obiettivi giornalieri, con consegna tramite notifiche locali di Windows.
2. Una sezione Progressi che trasformi le attività realmente registrate in statistiche leggibili: tempo per materia, costanza settimanale, calendario delle attività, andamento dei voti, obiettivi mensili e continuità dello studio.

Il principio guida è **misurare attività reali e aiutare a organizzarsi, non introdurre una competizione o una gamification**. Niente XP, classifiche, penalità o notifiche colpevolizzanti.

## 1. Fondazioni già presenti da riutilizzare

L'implementazione non deve introdurre un secondo calendario o un secondo registro delle ore.

| Esigenza | Fonte già presente | Uso previsto |
| --- | --- | --- |
| Esami | `CalendarEvents` con categoria `Exam`, `Exam` e `CareerExamData.Attempts` | Data, stato di verifica e collegamento alla materia |
| Consegne | `StudyTasks.DueAt` e `CalendarEvents` con categoria `Deadline` | Scadenza e stato del task/evento |
| Sessioni programmate | `CalendarEvents` con categoria `Study` o `Review` | Avviso prima dell'inizio |
| Tempo effettivo | `StudyActivities` e `ActivityLedgerService` | Ore registrate, per materia e per giorno |
| Timer | `StudySessions` sincronizzate nel registro attività | Conteggio del focus senza duplicare il tempo |
| Grafico/calendario attività | `ActivityLedgerService.Summarize`, `BuildHeatmap`, `BuildTimeline` | Aggregazioni già consapevoli di intervalli sovrapposti |
| Voti | `CareerDataProjection` e `CareerApplicationService` | Esiti ufficiali e voti numerici |
| Obiettivo settimanale | `WeeklyStudyHours` e progressi esistenti | Target e confronto settimanale |

Note importanti:

- `StudyMissionData` rappresenta una missione con checklist; non va riutilizzata come obiettivo mensile espresso in ore.
- `ActivityLedgerService` è la fonte canonica per il tempo studiato. Non sommare separatamente sessioni focus e attività collegate alla stessa sessione.
- `CareerDataProjection` è la proiezione canonica degli esami. I vecchi record `Grades` vengono migrati negli esami e nei tentativi: il grafico dei voti deve evitare di contare due volte lo stesso risultato.
- Le date generate dal planner possono essere indicative. Un avviso relativo a un evento indicativo deve dichiararlo esplicitamente e non presentarlo come un appello ufficiale.

## 2. Esperienza utente proposta

### Centro notifiche

Aggiungere un'icona campanella nella barra superiore della shell, con un contatore discreto degli elementi non letti. Il menu rapido mostra gli ultimi cinque avvisi e un collegamento **Apri centro notifiche**.

La pagina dedicata contiene:

- schede **Tutte**, **Da leggere** e **Promemoria**;
- raggruppamento per Oggi, Prossimi giorni e Precedenti;
- azioni **Segna come letto**, **Ignora** e, per gli avvisi temporali, **Rimanda**;
- collegamento alla risorsa originaria: esame, task, evento del calendario o Progressi;
- stato vuoto chiaro quando non ci sono notifiche.

Ogni elemento mostra categoria, titolo, data/ora e una descrizione concreta. Esempi:

- **Esame tra 7 giorni** — Analisi Matematica, 16 novembre, appello indicativo/confermato.
- **Consegna in scadenza** — Relazione di laboratorio, domani alle 18:00.
- **Sessione programmata** — Ripasso di Reti, inizio alle 15:00.
- **Obiettivo giornaliero non completato** — 35 di 60 minuti registrati oggi.

### Personalizzazione dei promemoria

Nella scheda Promemoria o in Impostazioni l'utente può:

- attivare/disattivare ciascuna categoria;
- scegliere l'anticipo per esami e consegne;
- scegliere quanti minuti prima avvisare per una sessione;
- impostare l'ora del controllo dell'obiettivo giornaliero;
- creare promemoria personali una tantum, giornalieri o settimanali;
- impostare una fascia **Non disturbare**;
- abilitare/disabilitare la consegna tramite notifiche Windows.

Valori iniziali suggeriti, tutti modificabili: esame 7 giorni prima, consegna 24 ore prima, sessione 15 minuti prima, controllo obiettivo alle 20:30. Il controllo dell'obiettivo viene creato solo quando l'utente ha configurato un target giornaliero.

### Progressi

La schermata Progressi viene organizzata in cinque blocchi:

1. **Riepilogo:** ore di focus nel periodo, media per giorno attivo, sessioni completate e serie corrente.
2. **Tempo per materia:** barre ordinate per minuti/ore, con filtro periodo.
3. **Costanza:** grafico giornaliero degli ultimi 7 o 28 giorni e calendario/heatmap delle attività completate.
4. **Andamento dei voti:** risultati numerici ufficiali in ordine cronologico, con filtro per materia. Gli esiti idoneità/pass-fail non entrano nella scala numerica.
5. **Obiettivi mensili:** target in ore, tempo effettivo registrato e percentuale raggiunta.

Stile: mantenere il tema charcoal già definito in `App.xaml`, card sobrie, grafici leggibili e accenti blu/ciano. La serie di giorni consecutivi è un dato informativo secondario, non una meccanica che punisce le pause.

## 3. Modello applicativo proposto

I nomi sono indicativi; vanno consolidati con i pattern delle entità EF già presenti prima di scrivere codice.

### `ReminderRuleData`

Definisce un promemoria creato dall'utente:

- `Id`, `Title`, `Message`;
- data/ora per il promemoria singolo oppure ora locale e ricorrenza;
- ricorrenza iniziale supportata: una tantum, giornaliera o settimanale;
- `TimeZoneId`;
- `IsEnabled`;
- riferimento facoltativo alla risorsa collegata;
- `CreatedAt`, `UpdatedAt`.

Per le ricorrenze si conserva l'ora locale e il fuso orario, non un offset fisso: in questo modo il promemoria resta alle 09:00 locali anche quando cambia l'ora legale.

### `NotificationSettingsData`

Preferenze globali e per categoria:

- attivazione esami, consegne, sessioni e obiettivo giornaliero;
- giorni/ore di anticipo configurabili;
- orario del controllo giornaliero;
- fascia di silenzio;
- abilitazione delle notifiche Windows;
- target giornaliero in minuti e soglia minima per conteggiare un giorno nella serie.

### `NotificationOccurrenceData`

Una voce concreta del centro notifiche, separata dalla regola che l'ha generata:

- identificativo e chiave di deduplicazione;
- categoria, titolo, messaggio e data/ora prevista;
- origine e identificativo della risorsa collegata;
- stato letto/ignorato;
- stato di consegna Windows e timestamp;
- eventuale identificativo della regola personale.

La chiave di deduplicazione deve essere stabile, ad esempio `exam:{examId}:7d:{localDate}`, e avere un vincolo univoco nel database. Riavvio, refresh o ricalcolo del calendario non devono creare notifiche duplicate.

### `MonthlyStudyGoalData`

Un target per uno specifico mese, con:

- mese di riferimento;
- minuti/ore target;
- eventuale materia, inizialmente facoltativa;
- timestamp di creazione/modifica.

Il target è salvato per mese, così la modifica dell'obiettivo del mese corrente non riscrive retroattivamente gli obiettivi dei mesi precedenti.

### Persistenza

Aggiungere i nuovi record a `StudyData` e al mapping EF/SQLite con una migration **additiva**. I database esistenti devono essere migrati senza perdita di dati e con notifiche disabilitate finché l'utente non conferma le preferenze iniziali.

Non modificare i formati di backup/cifratura o i token Moodle in questa fase; i nuovi dati dovranno però essere inclusi nei normali backup ed export secondo le regole di privacy già adottate.

## 4. Regole di generazione delle notifiche

Un `NotificationScheduler` calcola le occorrenze a partire dai dati già persistiti. Le regole automatiche proposte:

| Evento | Condizione | Comportamento |
| --- | --- | --- |
| Esame | Data futura e promemoria abilitato | Crea gli avvisi secondo gli anticipi configurati |
| Consegna | Task/evento non completato o annullato e scadenza futura | Avvisa secondo l'anticipo scelto |
| Sessione | Evento calendario Study/Review non annullato | Avvisa prima dell'orario di inizio |
| Obiettivo giornaliero | Ora di controllo raggiunta e minuti reali sotto il target | Una sola notifica per giorno locale |
| Promemoria personale | Regola attiva e occorrenza raggiunta | Crea una notifica collegata alla regola |

Vincoli:

- Ogni occorrenza ha una chiave univoca; il servizio è idempotente.
- Se un evento viene spostato, annullato o completato, le notifiche future corrispondenti vengono aggiornate/annullate. La cronologia già vista resta disponibile.
- Il controllo giornaliero usa i secondi effettivi dell'Activity Ledger nel giorno locale; non considera il tempo con l'app aperta.
- Se il target giornaliero non è stato configurato, non si genera alcun avviso di mancato obiettivo.
- Il motore rispetta fuso orario, passaggio ora legale/solare, orari silenziosi e preferenze di categoria.
- Un evento con verifica `Indicative` deve essere etichettato come indicativo. Non trasformare una data proposta dal planner in una scadenza ufficiale.

## 5. Notifiche native di Windows

Introdurre un'astrazione `IWindowsNotificationService` nel confine Presentation/Platform. Il motore applicativo non deve dipendere da API Windows.

Per WPF, la documentazione Microsoft attuale raccomanda `AppNotificationManager` del Windows App SDK (`Microsoft.Windows.AppNotifications`), anche per applicazioni desktop non pacchettizzate. La registrazione e l'handler di attivazione vanno gestiti nel bootstrap WPF; l'azione della notifica deve riaprire Unitracker sulla pagina e sull'entità corrette.

Per gli avvisi futuri si può usare la pianificazione locale di Windows. La documentazione indica una finestra di consegna di cinque minuti: se il PC rimane spento oltre tale finestra, la notifica programmata può essere scartata. La consegna garantita dopo lunghi periodi di spegnimento richiederebbe un meccanismo in background separato e non rientra nel primo rilascio.

Non usare push, cloud o servizi remoti: le notifiche sono locali e originate dai dati dell'utente.

Riferimenti tecnici Microsoft:
- [Windows notifications overview](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/)
- [App notifications in a .NET WPF/WinForms app](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet)
- [Schedule an app notification](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-scheduled)

## 6. Progettazione del servizio statistiche

Introdurre un `ProgressAnalyticsService` che restituisce read model immutabili. Deve riutilizzare `ActivityLedgerService`, `CareerDataProjection` e i servizi di calcolo esistenti, senza replicare regole di dominio nei ViewModel.

### Definizione delle metriche

- **Ore per materia:** minuti effettivi raggruppati per `CourseId`, nel periodo selezionato. Gli intervalli sovrapposti devono essere conteggiati una sola volta.
- **Costanza settimanale:** minuti effettivi per giorno locale, con selettore 7/28 giorni. I giorni senza dati restano a zero; non si inferiscono sessioni da eventi pianificati.
- **Calendario attività:** heatmap giornaliera basata su attività registrate/completate; il dettaglio di un giorno apre il diario attività.
- **Andamento voti:** tentativi completati con risultato ufficiale e voto numerico, ordinati per data; pass/fail esclusi dal grafico numerico. L'eventuale media cumulativa usa `CareerCalculatorService`.
- **Obiettivo mensile:** minuti dell'Activity Ledger nel mese confrontati con il target di quel mese. Se manca un target, mostrare uno stato configurabile, non una percentuale fittizia.
- **Serie di giorni:** giorno valido quando il tempo di focus registrato raggiunge la soglia configurata (predefinita suggerita: 20 minuti). Calcolo sul calendario locale; oggi non interrompe la serie finché la giornata non è terminata. Nessun avviso quando una serie si interrompe.

Ogni read model deve specificare periodo, fuso orario, unità e disponibilità dei dati. Un grafico senza campioni o senza voti ufficiali mostra uno stato vuoto esplicito, non valori inventati.

## 7. Architettura e dipendenze dalla Fase 3

Questa fase dipende dalla chiusura dei percorsi di scrittura della Fase 3.

Oggi `StudyDataSession` serializza le operazioni solo per i chiamanti che la utilizzano; i percorsi legacy di `MainViewModel` e `StudyWorkspaceViewModel` salvano ancora direttamente tramite `IStudyDataStore`. Se un nuovo ViewModel salvasse promemoria tramite la sessione mentre un ViewModel legacy mantenesse uno snapshot precedente, il salvataggio legacy potrebbe sovrascrivere i nuovi dati.

Quindi, prima di integrare la persistenza delle nuove funzioni nell'interfaccia:

1. completare la migrazione dei writer esistenti su `StudyDataSession`;
2. pubblicare gli aggiornamenti cross-page dopo ogni mutazione riuscita;
3. rimuovere il ponte `PageViewModelBase.State` e garantire che i nuovi ViewModel non dipendano dai monoliti legacy.

Dopo questa condizione, i servizi proposti diventano:

- `NotificationRulesService`: genera e aggiorna occorrenze senza dipendenze WPF;
- `NotificationApplicationService`: query, lettura, dismiss e gestione regole tramite sessione condivisa;
- `IWindowsNotificationService` / implementazione Windows: consegna e attivazione delle notifiche;
- `ProgressAnalyticsService`: query statistiche via `StudyDataSession.QueryAsync`;
- `NotificationsViewModel` e `ProgressViewModel`: soli read model, comandi e stato UI.

## 8. Sequenza di implementazione

1. **Prerequisito — chiusura Fase 3:** completare ownership condivisa dei dati e migrazione dei writer.
2. **4A — modelli e persistenza:** impostazioni, regole personali, occorrenze e obiettivi mensili; migration SQLite e test di compatibilità.
3. **4B — motore notifiche:** generazione idempotente, anticipi configurabili, fusi orari, annullamento/riprogrammazione.
4. **4C — centro notifiche WPF:** campanella shell, contatore non letti, pagina, azioni e gestione promemoria.
5. **4D — integrazione Windows:** notifiche locali/programmate, attivazione e deep link; impostazioni di consegna.
6. **4E — analytics:** read model, definizioni metriche, storico dei voti, obiettivi e streak.
7. **4F — schermata Progressi:** grafici, heatmap, filtri periodo e stati vuoti.

Le parti Windows vanno verificate su Windows dopo l'implementazione. Non è previsto eseguire ora test Windows.

## 9. Test e criteri di accettazione

### Notifiche

- Una regola genera una sola occorrenza anche dopo riavvio, refresh o doppia esecuzione.
- Spostamento, completamento o annullamento della risorsa aggiornano gli avvisi futuri senza cancellare la cronologia.
- Esame indicativo e appello ufficiale sono distinguibili nel testo della notifica.
- Test su confini di giornata, fusi orari, ora legale e ora solare.
- La notifica giornaliera non parte prima dell'orario scelto e non si ripete nello stesso giorno locale.
- Disabilitare una categoria impedisce nuove consegne per quella categoria.
- Click su notifica apre la pagina corretta e seleziona la risorsa collegata.

### Statistiche

- Un'attività associata a una sessione focus non duplica le ore.
- Intervalli sovrapposti, attività eliminate e attività annullate rispettano le regole dell'Activity Ledger.
- I totali per materia sono coerenti con il totale del periodo.
- Giorni senza registrazioni non vengono inventati a partire dal calendario.
- I voti legacy migrati e quelli ufficiali non compaiono due volte; pass/fail non entra nella media numerica.
- Il target mensile resta associato al mese corretto anche dopo la modifica del target successivo.
- La serie rispetta soglia e fuso orario ed è verificata per i casi di inizio/fine giornata.

### Accettazione prodotto

- Tutte le notifiche possono essere disabilitate o personalizzate.
- Nessuna notifica presenta una data indicativa come ufficiale.
- Le notifiche locali funzionano secondo i limiti documentati da Windows.
- Le statistiche spiegano periodo e provenienza dei dati.
- Nessuna gamification competitiva o messaggio colpevolizzante.
- Le nuove funzioni non introducono un secondo calendario, un secondo registro delle ore o una seconda fonte di verità.

## 10. Fuori ambito

- Notifiche push o sincronizzazione cloud.
- Classifiche, punti esperienza, badge o penalità per streak interrotti.
- Previsioni AI o suggerimenti generati da servizi remoti.
- Modifiche alle regole ufficiali di carriera, al calcolo dei voti o alle integrazioni Moodle.
- Modifiche ai formati di cifratura e alle chiavi dei backup.
