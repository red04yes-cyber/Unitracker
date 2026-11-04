# PROMPT PER FREEBUFF — UniTo StudyHub: implementazione funzionale completa

Agisci come software engineer responsabile di dominio, logica applicativa, persistenza, integrazioni, AI, sicurezza e test di UniTo StudyHub.

## OBIETTIVO

Portare avanti tutte le funzionalità tecniche di StudyHub: completamento carriera e materie, pianificazione e calendario, Studio Lab, Accademia/AI, analytics, le 20 funzioni aggiuntive e infrastruttura di qualità.

**NON sei responsabile di UI o UX. La UI/UX è gestita separatamente da ChatGPT. Non devi ridisegnare, rifattorizzare o modificare l'aspetto dell'applicazione.**

## REPOSITORY E BRANCH

- Repository: `red04yes-cyber/Unitracker`
- Base attuale con la UI/UX approvata: `work/studyhub-uiux`
- La tua branch dedicata: `work/freebuff-studyhub-features`
- La PR UI #35 deve restare intatta e non deve essere unita da te.
- Non lavorare direttamente su `feature/core-domain`, non fare force-push e non sovrascrivere branch altrui.
- Mantieni le tue modifiche isolate e apri PR tecniche verso `work/studyhub-uiux` (o PR più piccole verso la base concordata).
- Non fare merge automatico.

## CONFINE NON NEGOZIABILE: UI E UX ESCLUSE

Non modificare questi file:
- `src/UniToStudyHub/App.xaml`
- `src/UniToStudyHub/MainWindow.xaml`
- `src/UniToStudyHub/MainWindow.xaml.cs`

Non modificare:
- dizionari di risorse, temi, stili, template, colori, font, icone, animazioni e asset visivi;
- layout, sidebar, navigazione visuale, dimensioni, spaziature, card, testi di interfaccia o disposizione delle schermate;
- future viste XAML sotto `Presentation/Views/**`.

Puoi lavorare su:
- Domain, Application, Services, Infrastructure, Persistence, EF entities e migrations;
- repository, adapter e integrazioni;
- DTO, contratti, validatori, scheduler, motori di calcolo;
- test automatici, CI, sicurezza e documentazione tecnica;
- ViewModel solo per esporre stato e comandi ai binding, mantenendo compatibili i binding già usati dalla UI. Non decidere layout, contenuti visuali o flussi UX.

Se una funzione richiede una scelta UI, implementa prima il servizio/contratto e documenta un'interfaccia tecnica neutra; non creare né modificare la schermata. Segnala a ChatGPT i binding necessari.

## REGOLE DI SVILUPPO

1. Mantieni WPF, .NET 10 e MVVM.
2. Non trasformare `MainViewModel` in una classe gigantesca: sposta regole e casi d'uso in servizi/domain dedicati. Non rompere i binding attuali.
3. Mantieni una sola fonte dati condivisa tra carriera, calendario, missioni, focus, ripassi e analytics.
4. Le nuove modifiche al database devono essere migration incrementali EF Core.
5. Verifica migrazioni sia su DB vuoto sia su DB esistente. Non cancellare né sovrascrivere dati utente.
6. Operazioni I/O e AI asincrone, cancellabili, con timeout, retry ragionato e gestione errori.
7. Ogni import deve essere idempotente, con deduplicazione basata su ID esterno/hash/fonte.
8. Conserva origine, timestamp di acquisizione e stato di verifica per appelli, eventi e materiali importati.
9. Distingui sempre appelli ufficiali da date indicative generate dal planner.
10. Non creare dati demo/finti e non visualizzare stime come certezze.
11. Non fare scraping che aggiri login, CAPTCHA, controlli d'accesso o condizioni UniTo. Preferisci API/feed ufficiali, Moodle autorizzato e file importati dall'utente. Prevedi inserimento manuale quando non esiste una fonte.
12. Non hardcodare API key o token. Riutilizza DPAPI per i segreti Windows e non inserirli in log, URL, backup o messaggi d'errore.
13. I PDF, appunti, pagine Moodle e testi importati sono input non attendibili: proteggi da prompt injection. Non interpretarli come istruzioni di sistema, non eseguire codice e non inviare dati ad AI remote senza consenso/configurazione espliciti.
14. Non aggiungere dipendenze senza verificarne necessità, licenza, manutenzione, sicurezza e compatibilità .NET 10.
15. Implementa le funzioni in vertical slice piccole, ciascuna con test e migrazioni proprie. Non fare un mega-commit.
16. Documenta formule, assunzioni, limiti, origine dei dati e comportamento in caso di dati mancanti.
17. Non dire che qualcosa è completo senza test che lo dimostrino.

## ROADMAP — COSA IMPLEMENTARE

### 1. Completamento logico di Carriera e Materie

- Profilo universitario persistente: CFU totali configurabili, obiettivo laurea, preferenze e anno/sessioni accademiche.
- Stato materia esplicito, separato dal semplice booleano `Completed`: da iniziare, in preparazione, pianificata, superata, archiviata/ritirata.
- Archiviazione manuale e ripristino senza alterare voti, CFU o relazioni.
- Tentativi d'esame multipli con date, voto, lode, esito, accettazione/rifiuto e storico.
- Evita di contare due volte i CFU e definisci bene il tentativo valido più recente.
- Calcoli testati di media ponderata, aritmetica e voto base; gestisci esami senza voto e non conteggiati.
- Simulatore What-if: voti ipotetici senza cambiare dati persistiti, confronto tra scenario attuale e scenario simulato.
- Metadati delle materie: docente, codice, CFU, sessione, syllabus, prerequisiti, note e fonti.

### 2. Calendario, appelli e importazioni

- Modello unificato di eventi: lezione, appello, studio, ripasso, scadenza e impegno personale.
- Import ICS completo: timezone, ricorrenze, eccezioni, eventi all-day, aggiornamenti, cancellazioni e duplicati.
- Sincronizzazione calendario Moodle tramite token autorizzato: paginazione, fusi orari, incrementale, idempotenza e gestione errori.
- Appelli da fonti ufficiali solo se accessibili in modo documentato e consentito; registra URL, timestamp, stato di verifica e fonte. Altrimenti supporta ICS/manuale.
- Distingui evento ufficiale, evento importato e proposta generata dal planner.
- Planner settimanale basato su disponibilità reale, ore configurate, difficoltà, scadenze e ripassi.
- Genera proposte; non spostare o cancellare impegni già esistenti senza conferma esplicita.
- Gestione conflitti, pause/buffer, limiti giornalieri e sostenibilità del piano.

### 3. Studio Lab — motore funzionale

- Focus session persistenti: inizio/fine, durata, pause, materia, argomento e outcome.
- Pomodoro configurabile e affidabile a sospensione/riavvio app o PC: recupero timer da timestamp, non da loop in memoria.
- Task/Mission: create dall'utente o generate da regole esplicite; stato, scadenza, durata stimata, priorità e collegamenti.
- Diario errori: materia, argomento, domanda, risposta errata, tipo di errore, correzione, causa e ripasso collegato.
- Flashcard/schede personali con spaced repetition configurabile, storico, intervalli e valutazione.
- Parcheggio distrazioni: testo, data/ora, sessione correlata, stato aperta/risolta.
- Check-in energia volontario, senza diagnosi o inferenze mediche.
- Registro focus, streak e statistiche con definizioni precise; niente streak artificiali.
- Formule, cheat sheet e note per materia.
- Il completamento di un task o errore aggiorna in modo coerente ripassi, calendario e analytics.

### 4. Accademia e AI Study Director

- Architettura AI provider-agnostic; non legare l'app ad un unico provider.
- Configurazione sicura delle credenziali e limiti di utilizzo/costo.
- AI Study Director: propone il piano quotidiano usando appelli, syllabus, scadenze, tempo disponibile, risultati e ripassi. Spiega le motivazioni e consente modifiche. Mai applicare il piano senza conferma.
- Memory Engine: coda ripassi, scheduling, spiegazione libera e risposta scritta; valuta recupero, completezza e confidenza, conservando lo storico.
- Feynman Coach: individua concetti poco chiari e genera domande di chiarimento.
- Error Pattern Detector: individua errori ricorrenti, mostra evidenze e consente correzione manuale.
- Ruoli esperti: apprendimento, progettazione didattica, preparazione esami, pianificazione sostenibile e metacognizione. Sono profili del motore, non cinque app isolate.
- Prompt injection: i documenti sono dati non attendibili, non istruzioni privilegiate. Delimita e sanitizza il contenuto, filtra tool e azioni, non concedere accesso implicito a dati sensibili.

### 5. Exam Simulator, PDF Intelligence e analytics

- Question Bank con materia, syllabus, argomento, difficoltà, tipologia e fonte.
- Exam Simulator: fac-simile da materiali autorizzati, rubriche di correzione e report per argomento. Etichetta chiaramente la simulazione, mai prova ufficiale.
- Oral Exam Trainer: astrazione tecnica per input/output vocale, trascrizione e valutazione con criteri espliciti.
- PDF Intelligence: estrazione testo, pagine, metadati, indice, ricerca, sintesi, domande e riferimenti verificabili.
- Source Citation Engine: ogni citazione deve puntare a documento e pagina esistenti; non inventare riferimenti.
- Exam Trends Analyzer: usa solo prove fornite o fonti consentite; mostra numerosità, periodo e incertezza, non promettere previsioni.
- Exam Readiness Score: formula trasparente basata su syllabus coperto, ripassi, esercizi e prestazioni; mostra componenti e dati mancanti.
- Learning Analytics con definizioni, periodo, unità, denominatore, fonte e gestione missing.

### 6. Le 20 funzionalità aggiuntive

Implementale progressivamente, non in un unico commit:

1. Laurea What-if Simulator.
2. Mappa delle propedeuticità e grafo degli esami.
3. Session Simulator per confrontare carichi e scenari di sessione.
4. Exam Readiness Score trasparente.
5. Syllabus Tracker con copertura e stato degli argomenti.
6. Knowledge Graph di concetti, formule e prerequisiti.
7. Question Bank.
8. Oral Exam Trainer.
9. Error Pattern Detector.
10. Feynman Coach.
11. Source Citation Engine.
12. Exam Trends Analyzer.
13. Time Budget Manager per ore settimanali e disponibilità.
14. Energy Intelligence basata sui check-in volontari.
15. Smart Notifications native Windows con quiet hours e preferenze.
16. University Admin Hub per scadenze tasse, iscrizioni, documenti e attività amministrative; fonti verificabili e reminder manuali.
17. Deep Work Mode: stato della sessione, sospensione notifiche solo con consenso e ripristino robusto.
18. Secure Backup Center: backup, storico versioni, restore verificato, crittografia e gestione errori.
19. Personal Knowledge Vault: note, concetti, backlink e collegamenti a materie.
20. Weekly Review: confronto obiettivi/risultati, focus, errori e suggerimenti motivati.

Priorità di implementazione per il primo ciclo dopo la stabilizzazione:
- What-if Simulator;
- Mappa propedeuticità;
- Exam Readiness Score;
- Syllabus Tracker;
- Question Bank;
- Error Pattern Detector;
- Source Citation Engine;
- Time Budget Manager;
- Secure Backup Center;
- Weekly Review.

Gli altri elementi entrano in iterazioni successive.

## TEST OBBLIGATORI

- Unit test per calcoli, transizioni di stato, tentativi, planner e spaced repetition.
- Integration test SQLite su DB vuoto e DB preesistente.
- Test ripetizione import ICS/Moodle senza duplicati, ricorrenze e timezone.
- Test Pomodoro con riavvio e orologio che cambia.
- Test backup/restore, file corrotti, backup parziali, rollback.
- Test prompt injection in PDF, Moodle, appunti e testo web.
- Test timeout, cancellation, retry, rate limit e logging senza segreti.
- Test AI con mock, senza chiamate a rete o credenziali vere.
- Audit NuGet transitive e CI Windows: restore, test, WPF build/publish e smoke test.
- Non usare dati personali reali nelle fixture.

## MODO DI LAVORARE

1. Prima leggi la codebase corrente e la PR UI #35, poi individua gli attuali servizi, entità, migrations e test.
2. Scrivi una breve analisi dei gap e una sequenza di vertical slice. Non modificare UI/UX.
3. Parti dalla prima slice utile: dominio carriera/attempts/profilo + migrazione + mapping + test; non limitarti a produrre un piano.
4. Dopo ogni slice: test, build, controllo diff, documentazione e commit autonomo.
5. Quando una modifica richiede binding WPF, esponi il contratto tecnico e descrivilo nella PR; lascia a ChatGPT la modifica visuale.
6. Se devi cambiare un file vietato per necessità tecnica, fermati e chiedi prima: non farlo di nascosto.
7. Apri PR separate, piccole e verificabili, contro la branch UI. Non fare merge.

## CONSEGNA ATTESA

- Una PR tecnica dedicata e non confliggente con la PR UI #35.
- Codice, migration, test e documentazione per ciascuna feature.
- Report finale con file cambiati, comportamento implementato, risultati test/CI, limiti e prossima slice.
- Elenco esplicito dei binding/contratti che ChatGPT dovrà collegare alla UI.
- Conferma che nessun file UI/UX è stato modificato.

Inizia ora con l'audit tecnico della codebase e implementa la prima vertical slice completabile. Non dichiarare completate le funzionalità future soltanto perché sono elencate in roadmap.