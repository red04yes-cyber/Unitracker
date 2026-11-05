# Freebuff technical slice 2 — Calendario, Studio Lab, PDF/AI, progressi e backup

## Stato

Questa è la seconda slice tecnica sul branch `work/freebuff-studyhub-logic`, basato su
`work/studyhub-uiux` + slice 1 + CI. Non è un merge e non sostituisce la PR UI #35 né la PR calendario #36.

La UI/UX resta di ChatGPT. In questa slice **non sono stati modificati** `App.xaml`, `MainWindow.xaml`,
`MainWindow.xaml.cs`, temi, risorse, layout, navigazione o file XAML sotto `Presentation/Views/**`.
Sono stati modificati solo modelli, persistenza, servizi, composizione e il ViewModel esistente per esporre
stato/comandi reali.

## 1. Calendario funzionale

### Modello e regole

`CalendarEvent` conserva ora, oltre ai campi visuali già presenti:

- `Source` (`manual`, `ics`, `planner`, `moodle`), `Verification` (`unverified`, `indicative`, `official`);
- `TimeZoneId`, `ExternalId`, `ExternalSequence`, `IsCancelled`, `RecurrenceRule`;
- `CreatedAt` / `UpdatedAt`.

`CalendarService` valida titolo, categoria, intervallo positivo, lunghezza dei campi, fuso orario e durata
dei tutto-il-giorno. Un evento che attraversa la mezzanotte è valido finché la fine è successiva all’inizio;
la vista deve mostrarlo su entrambi i giorni tramite `GetEventsInRange`, non duplicarlo nei dati.

`CalendarApplicationService` implementa CRUD, filtro per categoria/sorgente e collegamento idempotente agli
esami pianificati. `EnsureExamEvent` produce un evento `planner` con `Verification.Indicative` e note esplicite:
non è un appello ufficiale. La chiave esterna stabile `exam:<id>` evita duplicati su importazioni ripetute.

### Import ICS

`IcsCalendarParser` e `IcsImportService` supportano:

- `VEVENT`, `DTSTART`, `DTEND`, `DURATION` di base, `UID`, `SEQUENCE`, `STATUS:CANCELLED`;
- date tutto-il-giorno (`VALUE=DATE`), `TZID`, date UTC con `Z`;
- `RRULE` `DAILY`, `WEEKLY`, `MONTHLY`, `YEARLY` con `INTERVAL`, `COUNT`, `UNTIL`, `BYDAY` (solo weekly),
  ed `EXDATE`;
- finestra di materializzazione esplicita, limite di 1000 occorrenze e limite file di 10 MB;
- deduplicazione per `ExternalId` e `SEQUENCE`; un import completo rimuove le occorrenze non più presenti
  nella finestra, mentre un evento cancellato viene marcato `IsCancelled`.

**Limiti dichiarati:** `VTIMEZONE`, `BYSETPOS`, `RDATE`, regole di fuso orario non IANA e alcuni casi di
ora legale “flottante” non vengono indovinati. Gli eventi non validi producono warning e non bloccano gli altri.
L’import Moodle live non è implementato: non esiste una sincronizzazione finta né un dato ufficiale inventato.

### Persistenza e UI

Gli eventi sono nella stessa sorgente `StudyData`/SQLite, non più in un JSON parallelo. `MainViewModel`
carica gli eventi da SQLite e importa una sola volta il vecchio `calendar-events.json`, parcheggiandolo come
`.migrated`. Il salvataggio del ViewModel usa `IStudyDataStore.SaveAsync`.

Il branch non contiene le viste mensile/settimanale/giornaliera: sono UI. La PR #36 non è stata usata come base
perché il suo `MainWindow.xaml` non compila (manca la chiusura di `ScrollViewer` alla riga 604); correggerlo
sarebbe una modifica visuale fuori dal perimetro Freebuff. I binding tecnici disponibili sono descritti in
`FREEBUFF-UI-CONTRACTS.md`.

## 2. Studio Lab

`StudyLabServices.cs` contiene servizi deterministici e persistenti:

- `FocusSessionService`: start, pausa, ripresa, finish, cancel/reset. Lo stato è ricostruito da timestamp e
  secondi accumulati; chiudere l’app non perde la sessione. Non è possibile avviare due sessioni attive.
- `StudyTaskService`: creazione, completamento, annullamento, rimozione, priorità e scadenza.
- `ErrorJournalService`: registro errori con risposta sbagliata/corretta, causa, stato di revisione e prossima
  revisione.
- `FlashcardService`: creazione e revisione con rating `Again/Hard/Good/Easy`, intervalli, ease factor,
  lapse e storico `FlashcardReviewData`.
- `DistractionService` ed `EnergyService`: annotazioni volontarie collegate alla sessione o al momento.
- `ProgressService`, `ErrorPatternService`, `WeeklyReviewService`: aggregazioni descritte sotto.

Non esiste un timer di sistema o una notifica Windows in questa slice. La UI deve usare
`Snapshot`/`GetActive` e un proprio timer di visualizzazione, senza creare un secondo timer persistente.

## 3. PDF Intelligence e AI

### PDF

`PdfPigTextExtractor` estrae il testo pagina per pagina con PdfPig. `PdfImportService`:

- accetta solo file `.pdf`, con firma `%PDF-`, massimo 25 MB;
- calcola SHA-256 e rifiuta i duplicati;
- conserva `PdfDocumentData` e `PdfPageData` nel database;
- segnala `RequiresOcr` quando non c’è testo estraibile, senza inventare OCR o contenuti.

`PdfCitationService` crea una citazione solo se pagina e frase esistono davvero nel documento;
`PdfIntelligenceService.Search` cerca nelle pagine e restituisce snippet. Non esiste ricerca semantica,
embedding o indice vettoriale in questa slice.

**Limiti:** i test del workflow usano un estrattore fake deterministico; l’estrazione PdfPig è compilata ma
non è stata provata su una libreria reale di PDF. OCR, PDF protetti e file danneggiati restano casi da
verificare con fixture dedicate; gli errori di estrazione non vengono presentati come testo valido.

### Provider AI

`IAiProvider` separa l’interfaccia dal trasporto. Il provider registrato di default è `UnavailableAiProvider`:
restituisce `AiStatus.NotConfigured`, non effettua rete e non produce testo simulato. `MockAiProvider` esiste
solo per i test. `OpenAiCompatibleProvider` è un adapter opzionale per endpoint OpenAI-compatibile, non
registrato automaticamente.

`AiSettingsService` richiede consenso esplicito, provider, modello, URL HTTPS (HTTP solo loopback) e il
**nome** di una variabile d’ambiente che contiene la chiave; la chiave non viene salvata in `StudyData`,
log, prompt o backup. `PromptSafety` delimita i documenti come dati non attendibili e vieta al modello di
seguire istruzioni contenute nelle fonti. Le citazioni restituite dal modello non sono automaticamente vere:
la UI deve validarle con `PdfCitationService`.

**Limiti:** nessuna chiamata AI reale è stata eseguita; manca un selettore visuale per configurare il provider;
non sono inclusi Memory Engine, Feynman Coach, embeddings, OCR o un rate limiter oltre al campo
`MaxRequestsPerDay` (che è una configurazione, non ancora un contatore persistente).

## 4. Progressi

`ProgressService.Calculate(data, from, to, now)` produce solo valori derivati da dati registrati:

- minuti di focus = somma dei secondi accumulati delle sessioni completate nel periodo;
- sessioni completate = sessioni con stato `Completed`;
- task completati e minuti stimati/completati;
- flashcard dovute e ripassi registrati;
- minuti per materia e distribuzione giornaliera;
- goal settimanale calcolato dalla disponibilità del profilo (default 20 ore) e dal periodo;
- streak di giorni con almeno una sessione completata, senza giorni inventati;
- confronto con il periodo precedente, marcato come confronto e non come previsione.

`ErrorPatternService` conta gli errori per materia/argomento/tipo e mostra il campione; non deduce cause
psicologiche. `WeeklyReviewService` produce osservazioni e azioni solo quando ci sono dati sufficienti,
altrimenti restituisce un avviso esplicito.

## 5. Sistema e backup

`BackupService` continua a creare snapshot SQLite con `VACUUM INTO`, verifica `PRAGMA integrity_check`,
calcola SHA-256 e crea un backup di sicurezza prima del ripristino. In questa slice aggiunge un sidecar
`<backup>.manifest.json` con `FormatVersion`, data, dimensione e checksum. Il ripristino rifiuta un manifest
di formato futuro o un checksum non corrispondente; un backup vecchio senza manifest resta ripristinabile
solo dopo la verifica SQLite.

Il token Moodle è nel vault DPAPI separato e non viene incluso nel backup. **Non è stata aggiunta
crittografia del file di backup**: il manifest è integrità/versioning, non riservatezza. Un backup contiene
dati di studio e PDF testuali; va protetto con permessi del filesystem e una strategia di conservazione.

## 6. Migrazione e compatibilità

Nuova migrazione incrementale:
`src/UniToStudyHub/Infrastructure/Data/Migrations/20261105000258_AddStudyLabAndCalendarPersistence.cs`.

Aggiunge/estende le tabelle per calendario, sessioni, task, errori, flashcard e review, distrazioni, energia
e PDF. `StudyDataEntityMapper` valida i riferimenti e fa round-trip; `SqliteStudyDataStore` elimina in modo
atomico tutte le nuove collezioni quando viene sostituito lo snapshot. I test verificano anche un salvataggio
invalido: il database precedente non viene cancellato prima della validazione.

## 7. Verifica eseguita

Comandi eseguiti sul workspace Linux:

```bash
dotnet test tests/UniToStudyHub.Tests/UniToStudyHub.Tests.csproj -c Release
dotnet build src/UniToStudyHub/UniToStudyHub.csproj -c Release -p:EnableWindowsTargeting=true
```

Risultato: suite completa verde e build WPF Release con 0 warning/0 errori. La suite include i test
preesistenti più test per: timer attraverso pausa/riavvio, doppia sessione, CRUD calendario e mezzanotte,
ICS con timezone/tutto-il-giorno/EXDATE/ricorrenza, import idempotente e cancellazione, collegamento esami,
flashcard, progressi, pattern errori, PDF/duplicati/OCR warning, citazioni, prompt injection, provider non
configurato, round-trip SQLite e manifest backup.

**Non verificato:** esecuzione runtime WPF su Windows, rendering delle schermate, notifiche, integrazione
Moodle live, chiamate AI reali, OCR e PDF reali. La build WPF su Linux è una verifica statica/di
compilazione, non un test visuale.

## 8. Istruzioni per ChatGPT (UI)

1. **Calendario:** collegare le viste esistenti a `CalendarEvents`/`VisibleCalendarEvents`, `CalendarDate`,
   `CalendarPeriodDisplay`, `SelectedCalendarEvent`, `AddCalendarEventCommand`, `DeleteCalendarEventCommand`,
   `PreviousCalendarPeriodCommand`, `NextCalendarPeriodCommand`, `TodayCalendarCommand`. Per mensile,
   settimanale e giornaliera usare gli stessi dati e `CalendarService.GetWeekRange`/`GetMonthRange`; non
   ricreare un secondo store JSON.
2. **Stato/errori:** mostrare `StatusMessage`, disabilitare i comandi durante il salvataggio e non presentare
   un evento `planner` come appello ufficiale. Distinguere `Source` e `Verification`.
3. **Studio Lab:** serve un ViewModel/schermata dedicata per timer, task, flashcard, errori, distrazioni ed
   energia. Il timer visuale deve solo interrogare lo stato persistito; start/pausa/ripresa/reset devono
   passare dai servizi e salvare con `IStudyDataStore`.
4. **PDF/AI:** mostrare `RequiresOcr`, numero di pagine, duplicati e stato `AiStatus.NotConfigured`. Il
   consenso e la configurazione del provider non devono essere impliciti. Ogni citazione va validata contro
   la pagina; non mostrare una citazione generata dal modello come verificata senza `PdfCitationService`.
5. **Progressi:** usare `ProgressService`/`WeeklyReviewService` e mostrare sempre periodo e definizione della
   metrica; non usare valori demo quando le collezioni sono vuote.
6. **Backup:** conferma esplicita prima del ripristino, visualizzazione di checksum/manifest e del backup di
   sicurezza. Non promettere crittografia o sincronizzazione remota.

## 9. Lavoro non incluso

- import/sincronizzazione Moodle via API e gestione autorizzazioni;
- notifiche Windows, avvio automatico del timer e integrazione con la tray;
- OCR per PDF scansionati e ricerca semantica/embedding;
- rate limiting persistente e contatore giornaliero AI;
- crittografia dei backup e versioning remoto;
- Knowledge Graph, University Admin Hub e altre aree del brief non coperte dai servizi elencati;
- correzione del XAML rotto della PR #36 (di competenza UI).

Queste aree non devono essere mostrate come complete nella UI.
