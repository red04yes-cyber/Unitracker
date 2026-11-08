# Freebuff technical slice 1 — Carriera e fondamenta di studio

## Stato

Questa è la **prima PR tecnica** sul branch `work/freebuff-studyhub-features`, basata su `work/studyhub-uiux`.
Non è la dichiarazione che l’intero ecosistema è completo: le aree elencate in “Lavoro non incluso” non sono state
implementate in questo intervento e non devono essere presentate dalla UI come funzionanti.

La PR non modifica `App.xaml`, `MainWindow.xaml`, `MainWindow.xaml.cs`, temi, risorse o view. I binding esistenti
restano compatibili.

## Funzionalità implementate

### 1. Profilo universitario

Campi persistenti in `ProfileSettings`:

- nome e codice del corso di laurea;
- anno di iscrizione e anno accademico corrente;
- CFU totali previsti;
- data obiettivo di laurea (campo già esistente, ora affiancato agli altri dati di profilo);
- preferenze di studio;
- ore settimanali disponibili e limite giornaliero.

`AcademicProfileService.Update` valida intervalli, coerenza tra anno di iscrizione e anno corrente, capienza
giornaliera/settimanale e CFU delle materie rispetto al totale configurato.

### 2. Materie e stati

`CourseStatus` distingue: da iniziare, in corso, preparata, appello pianificato, superata, non superata, archiviata.
`Archived` è separato da `Completed`; archiviazione e ripristino non eliminano storico, voti o relazioni. I dati legacy
con `Completed = true` vengono normalizzati a `Passed`. Sono persistiti docente, anno accademico, sessione, ore stimate,
data appello, programma, URL, note, fonte e timestamp di aggiornamento.

L’eliminazione di una materia con storico carriera richiede una conferma esplicita (`removeCareerHistory`); non viene
mai eliminato silenziosamente uno storico.

### 3. Tentativi e carriera

`CareerApplicationService`:

- usa gli esami di carriera come unica sorgente per medie e CFU;
- preserva tutti i tentativi, inclusi rifiuti e fallimenti;
- usa il tentativo ufficiale più recente per media e CFU;
- registra un rifiuto senza `RecordedAt`, quindi fuori da media e CFU;
- accetta lode, idoneità e materie che non concorrono alla media;
- usa il totale CFU del profilo quando configurato;
- calcola la media aritmetica sui più recenti risultati ufficiali numerici, una volta per materia;
- supporta What-if senza modificare la carriera reale.

I vecchi record `Grades` vengono proiettati una sola volta in `CareerExams` con ID deterministici
(`legacy-grade:<id>` e `legacy-career:<courseId>`). La proiezione è idempotente. Se esiste già un esame di carriera per
la materia, i record legacy non vengono duplicati.

Formula media ponderata: `Σ(voto × CFU) / Σ(CFU)` sui risultati ufficiali che concorrono alla media.
Formula voto base: `media ponderata × 110 / 30`, arrotondata secondo `CareerCalculationOptions`.
Formula aritmetica: media semplice degli ultimi risultati ufficiali numerici per esame, esclusi idoneità e rifiuti.

### 4. What-if

`CareerApplicationService.Simulate` accetta materie esistenti o nuove e restituisce la differenza tra scenario reale e
simulato. Non salva e non altera gli attempt reali. L’interfaccia utente dovrà mostrare il risultato come simulazione.

### 5. Prerequisiti

`PrerequisiteGraphService` valida archi duplicati, auto-dipendenze e cicli; espone prerequisiti diretti, chiusura
transitiva e ordinamento topologico. La UI potrà usare il grafo per mostrare dipendenze e impedire piani impossibili.

### 6. Syllabus

`SyllabusTrackerService` gestisce argomenti per materia, stato, minuti stimati, fonte e ultimo studio. La copertura è
calcolata su un denominatore esplicito (`completati / totale argomenti`) e il residuo in minuti è la somma degli
argomenti non completati. Non viene presentata come previsione ufficiale.

### 7. Exam Readiness Score

`ExamReadinessService` produce componenti espliciti:

- copertura syllabus, peso 60%;
- ultimo risultato ufficiale, peso 40%, normalizzato sulla scala 18–30 (idoneità = 100, senza inventare un voto).

Se un componente manca, il punteggio è **parziale**, `IsComplete` è `false` e `MissingData` elenca cosa manca. Il
punteggio non è una previsione ufficiale né un voto.

### 8. Question Bank

`QuestionBankService` supporta domande aperte e chiuse, difficoltà, fonte, materia, argomento, opzioni, risposta corretta
e spiegazione. Le domande chiuse sono valutate in modo deterministico. Le domande aperte restituiscono
`RequiresManualReview = true`: nessun finto giudice AI viene usato nel motore locale.

### 9. Time Budget Manager

`TimeBudgetService` calcola capacità settimanale, minuti disponibili fino alle scadenze, fabbisogno stimato e
allocazione. Segnala capacità insufficiente o stime mancanti. Non crea, sposta o cancella eventi del calendario.

### 10. Secure Backup Center

`BackupService`:

- crea snapshot SQLite consistenti con `VACUUM INTO`;
- verifica con `PRAGMA integrity_check`;
- calcola SHA-256;
- crea un backup di sicurezza prima del ripristino;
- verifica la copia temporanea prima di sostituire il database;
- invalida il pool SQLite per non servire connessioni al vecchio file.

Il token Moodle resta nel vault DPAPI separato e non viene copiato nel backup. Il ripristino richiede che non ci siano
altri processi con il database aperto.

## Persistenza

Migrazione incrementale: `20261104192749_AddAcademicKnowledgeModel`.

Nuove tabelle:

- `CoursePrerequisites`
- `SyllabusTopics`
- `QuestionBankItems`

Nuove colonne in `ProfileSettings` e `Courses`. I test verificano:

- database vuoto e database già migrato;
- upgrade da schema legacy senza perdita di dati;
- round-trip di profilo, metadati, prerequisiti, syllabus, domande e tentativi;
- migrazione idempotente dei vecchi voti;
- rollback atomico del salvataggio.

## Sicurezza e limiti

- Nessun contenuto PDF/Moodle viene trattato come istruzione privilegiata in questo slice.
- Nessuna chiamata AI reale, nessuna API key e nessun token vengono registrati.
- Il backup non include il token DPAPI.
- Le stime (readiness, time budget, date del planner) sono etichettabili come stime, non come dati ufficiali.
- La UI non è stata modificata e non è stata eseguita una prova visuale WPF su Linux.

## Lavoro non incluso in questa PR

Non sono ancora implementati, e non devono essere dichiarati completi:

- import ICS, ricorrenze/timezone e sincronizzazione calendario Moodle;
- Studio Lab completo (Pomodoro persistente, missioni, errori, flashcard, ripassi, distrazioni, energia, streak);
- provider AI, Memory Engine, Feynman Coach e protezioni runtime per prompt injection;
- PDF Intelligence, Exam Simulator completo, Oral Trainer e Citation Engine;
- Learning Analytics, notifiche Windows, University Admin Hub, Knowledge Graph, Weekly Review;
- backup con crittografia aggiuntiva o versioning remoto.

Queste aree richiedono slice dedicate, migration e test propri. La UI di ChatGPT non deve mostrare comandi inerti:
i contratti disponibili sono in `docs/implementation/FREEBUFF-UI-CONTRACTS.md`.
