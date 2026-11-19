# Course removal safety — 19 novembre 2026

## Problema verificato su `main`

`AcademicProfileService.RemoveCourse` non chiudeva l'intero insieme di dipendenze:

- eliminava le domande del corso senza una conferma specifica per i dati Study Lab;
- lasciava sessioni, task, missioni, attività, flashcard, revisioni, errori, distrazioni, cicli Pomodoro e metadati PDF che continuavano a riferirsi al corso o ai suoi argomenti;
- poteva lasciare attività che referenziavano task, missioni, esami o sessioni rimossi;
- non offriva un inventario preventivo delle conseguenze della cancellazione.

Il mapper SQLite valida queste relazioni prima del salvataggio. La cancellazione del solo corso poteva quindi lasciare uno snapshot non persistibile e costringere il chiamante a ricostruire i dati precedenti.

## Implementazione

### Anteprima non mutante

`AcademicProfileService.GetCourseRemovalImpact(data, courseId)` restituisce conteggi separati per:

- carriera, esami, syllabus e collegamenti di propedeuticità;
- domande e dati Study Lab, inclusi tentativi d'esame, pagine PDF, segmenti delle sessioni e passi delle missioni;
- revisioni attività, review flashcard, distrazioni e cicli Pomodoro;
- documenti PDF indicizzati;
- eventi calendario da scollegare;
- sessioni Focus o timer Pomodoro ancora attivi.

Ripetere l'anteprima sullo stesso snapshot restituisce lo stesso risultato e non modifica le collezioni.

### Tre conferme indipendenti

`RemoveCourse` mantiene il parametro storico `removeCareerHistory` e aggiunge:

- `removeStudyHistory`: autorizza la cancellazione dei dati Study Lab e dei relativi record figli;
- `detachCalendarEvents`: autorizza a scollegare gli eventi dal corso.

Tutti i controlli vengono eseguiti prima della prima mutazione. Se manca una conferma, lo snapshot rimane invariato.

### Preservazione e blocchi

- Gli eventi calendario non vengono eliminati: il loro `CourseId` viene azzerato e il nome del corso viene conservato come etichetta storica quando manca.
- Un focus in stato Running/Paused blocca la cancellazione.
- Un Pomodoro Running/Paused collegato a una sessione del corso blocca la cancellazione.
- Un timer completato collegato a una sessione rimossa viene eliminato insieme alla cronologia esplicitamente confermata.
- Le dipendenze di propedeuticità entranti e uscenti vengono rimosse solo dopo la conferma della cancellazione accademica.

## Verifiche di sicurezza mirate

Sono stati eseguiti 30 controlli source-level sulla superficie di cancellazione e sulle relazioni validate dal mapper. Non rappresentano 30 vulnerabilità né un penetration test.

1. Null data rejected before planning.
2. Empty course identifier rejected.
3. Unknown course rejected by preview and removal.
4. Preview does not mutate collections.
5. Repeated preview is deterministic for an unchanged snapshot.
6. Grade records are included in the impact.
7. Planned exams are included.
8. Career exam records and nested attempts are counted.
9. Syllabus topics are included.
10. Incoming prerequisite edges are included.
11. Outgoing prerequisite edges are included.
12. Questions linked by course are included.
13. Questions linked through a syllabus topic are included.
14. Focus sessions linked by course are included.
15. Nested focus-session segments are counted.
16. Study tasks linked by course are included.
17. Missions linked by course are included.
18. Missions linked through a syllabus topic are included.
19. Nested mission steps are counted.
20. Activities linked directly by course are included.
21. Activities linked through topic/task/mission/exam/session are included.
22. Activity revisions are included through their parent activity.
23. Flashcards linked by course or topic are included.
24. Flashcard reviews are included through their parent card.
25. Error-journal entries linked by course or topic are included.
26. Distractions are included through their focus session.
27. Pomodoro cycles are included through their focus session.
28. PDF metadata and indexed pages are counted by course.
29. Running/paused focus sessions and Pomodoro timers block deletion.
30. Academic, Study Lab and calendar confirmations are checked before mutation; calendar events are detached rather than deleted.

### Correzioni effettive

- Chiusura delle dipendenze che prima rimanevano orfane.
- Conferma separata per la cancellazione dei dati Study Lab.
- Conferma separata per lo scollegamento degli eventi calendario, che vengono preservati.
- Blocco delle cancellazioni mentre una sessione Focus o un timer Pomodoro collegato è attivo.

Gli altri controlli confermano i guard-rail introdotti o già presenti nel mapper e nei modelli. Non sono state aggiunte modifiche cosmetiche per raggiungere una quota.

## Test aggiunti

`CourseRemovalSafetyTests.cs` contiene 13 test:

- conteggio completo dell'impatto, inclusi record annidati;
- conferma accademica e conferma Study Lab indipendenti;
- conferma di scollegamento calendario;
- cancellazione completa seguita da `StudyDataEntityMapper.NormalizeAndValidate`;
- blocco di sessioni Running e Paused;
- blocco di Pomodoro attivo;
- isolamento dei dati appartenenti ad altri corsi;
- anteprima stabile e non mutante;
- rifiuto del corso inesistente;
- verifica che nessuna collezione venga modificata quando manca una conferma.

I test sono stati aggiunti ma devono ancora essere eseguiti dalla CI Windows .NET 10.

## Contratto per la UI

Prima di mostrare la conferma finale, la UI può chiamare `GetCourseRemovalImpact` e visualizzare i conteggi. I tre consensi devono rimanere distinti:

1. eliminazione di carriera/syllabus/propedeuticità;
2. eliminazione della cronologia Study Lab;
3. scollegamento degli eventi calendario (gli eventi restano).

Non sono stati modificati XAML, temi, layout o ViewModel. Nessuna migrazione DB, dipendenza o funzionalità AI è stata aggiunta.

## Limiti

- Il chiamante deve ancora collegare l'anteprima e le tre conferme alla UI.
- La rimozione dei metadati PDF non cancella file originali esterni al database.
- Non viene effettuata alcuna operazione filesystem o rete.
