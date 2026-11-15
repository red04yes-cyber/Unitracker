# Exam readiness basata su evidenze di studio

Data: 15 novembre 2026  
Branch: `work/evidence-based-exam-readiness-20261115`

## Comportamento

Il punteggio è un indicatore organizzativo locale, non una previsione del voto né un dato ufficiale UniTo. Usa tre componenti:

| Componente | Peso | Campione richiesto | Regola |
|---|---:|---:|---|
| Copertura syllabus | 50% | almeno 1 argomento | argomenti completati / argomenti registrati |
| Richiamo flashcard | 35% | almeno 3 revisioni | media delle autovalutazioni negli ultimi 30 giorni: Again 0, Hard 50, Good 80, Easy 100 |
| Revisione errori | 15% | almeno 3 errori | media dello stato degli errori aggiornati negli ultimi 30 giorni: Open 0, Reviewing 50, Resolved 100 |

Le revisioni flashcard contano solo se riferite a flashcard attive della materia selezionata. Le voci del diario errori sono limitate alla stessa materia e alla data di ultimo aggiornamento/revisione; un errore più vecchio rivisto di recente conta come evidenza recente. Le date sono valutate come giorni di calendario registrati nell'offset del dato; la finestra comprende oggi e i 29 giorni precedenti, non date future.

I componenti sotto la numerosità minima sono segnalati e **non** inseriti come zero: i pesi dei soli componenti disponibili vengono rinormalizzati. `IsComplete` è vero solo quando tutti e tre i componenti hanno dati sufficienti. Il voto ufficiale più recente, se presente, viene mostrato come contesto e non modifica il punteggio.

## Contratto UI esistente

`StudyWorkspaceViewModel.ReadinessStatus` espone:
- punteggio con etichetta `parziale` quando mancano campioni;
- riepilogo del numero di argomenti, revisioni flashcard ed errori nella finestra;
- formula, pesi e soglie minime;
- componenti mancanti e, separatamente, l'eventuale ultimo esito ufficiale come solo contesto.

Il riepilogo viene ricalcolato quando le collezioni del workspace vengono aggiornate, quindi dopo inserimento/revisione di errori e flashcard e dopo le modifiche al syllabus. Non sono stati modificati XAML, temi o layout.

## Controlli di sicurezza e privacy

Esito dei controlli source-level sul perimetro della modifica. “Superato” indica una proprietà verificata nel codice; non equivale a una correzione di vulnerabilità. I test automatizzati aggiunti sono in attesa della CI Windows/Linux del branch.

| # | Area / rischio | Metodo ed evidenza | Esito |
|---:|---|---|---|
| 1 | Confine AI: modifica accidentale a Study Director | Diff limitato a readiness, ViewModel, test e documentazione; nessun file AI modificato | Superato |
| 2 | Chiamate a modelli non autorizzate | Revisione di `ExamReadinessService`: solo aggregazioni in memoria | Superato |
| 3 | Modifica dei provider AI | Nessun provider, factory o impostazione AI nel diff | Superato |
| 4 | Credenziali o token nel nuovo codice | Ricerca manuale nei file aggiunti/modificati; nessun segreto o token | Superato |
| 5 | Supply chain da nuove dipendenze | Confronto del diff: nessun PackageReference aggiunto | Superato |
| 6 | Traffico di rete non consensuale | Il servizio non usa client HTTP, socket o endpoint | Superato |
| 7 | SSRF / URL arbitrari | Nessun URL viene accettato o richiesto dal servizio | Non applicabile |
| 8 | SQL injection | Nessuna query SQL o input SQL nel perimetro | Non applicabile |
| 9 | SQL dinamico | Nessun comando SQL dinamico introdotto | Non applicabile |
| 10 | Path traversal / accesso file | Il servizio non costruisce né apre percorsi | Non applicabile |
| 11 | Reparse point e symlink | Nessuna operazione filesystem introdotta | Non applicabile |
| 12 | Estrazione ZIP / archive bombs | Nessuna gestione di archivi introdotta | Non applicabile |
| 13 | Download e redirect | Nessun download o redirect introdotto | Non applicabile |
| 14 | Deserializzazione di dati non attendibili | Il servizio riceve il modello `StudyData` già caricato; non deserializza payload | Non applicabile |
| 15 | Esecuzione di codice / processi | Nessun `Process.Start`, reflection o caricamento dinamico | Superato |
| 16 | Log di note o risposte personali | Nessuna chiamata di logging; i testi delle domande/risposte non sono copiati nel riepilogo | Superato |
| 17 | Eccezioni con contenuti sensibili | Gli errori di input riportano solo l'identificativo materia, non note o risposte | Superato |
| 18 | Identificativo materia vuoto | Guard clause e test dedicato | Superato |
| 19 | Isolamento syllabus tra materie | `SyllabusTrackerService.GetCoverage(data, courseId)` e test esistenti | Superato |
| 20 | Isolamento flashcard tra materie | Set di ID costruito solo dalle flashcard della materia; test dedicato | Superato |
| 21 | Flashcard sospese conteggiate | Esclusione esplicita di `IsSuspended`; test dedicato | Superato |
| 22 | Revisioni orfane conteggiate | Una revisione vale solo se il suo FlashcardId è nel set attivo della materia | Superato |
| 23 | Revisione fuori finestra | Filtro sui 30 giorni di calendario; test dedicato sul limite inferiore | Superato |
| 24 | Dati futuri inclusi nel punteggio | Il filtro ha anche un limite superiore pari al giorno di riferimento; test dedicati | Superato |
| 25 | Enum corrotti interpretati come evidenza | `Enum.IsDefined` filtra rating e stati sconosciuti; test dedicato | Superato |
| 26 | Campione statisticamente insufficiente | Soglie esplicite 3 revisioni e 3 errori; test dedicati | Superato |
| 27 | Dati mancanti trasformati in zero | I componenti insufficienti hanno `HasData=false` e vengono esclusi; test dedicato | Superato |
| 28 | Pesi non normalizzati | Divisione per la somma dei soli pesi disponibili; test combinato e parziale | Superato |
| 29 | Voto ufficiale confuso con readiness | Il voto è solo `OfficialResultContext`; test con voti differenti | Superato |
| 30 | Mutazione involontaria dello storico | Il calcolo legge collezioni senza scrivere; test di non-mutazione aggiunto | Superato a livello sorgente; esecuzione test pendente |

### Superficie non modificata / residui

- Nessuna migration, tabella, export, backup, reset privacy o formato persistito modificato.
- Nessun controllo di autenticazione, DPAPI o ACL era pertinente: il servizio non accede a credenziali o file.
- Il richiamo è un'autovalutazione e il diario errori dipende dalla registrazione volontaria: i campioni non sono statisticamente rappresentativi e non devono essere interpretati come probabilità di superare l'esame.
- La CI deve ancora confermare build e test sul commit finale; il container di sviluppo non è stato usato come sostituto della build Windows.

## Test aggiunti

I casi dedicati coprono assenza dati, punteggio parziale, mappatura delle quattro valutazioni, isolamento per materia, flashcard sospese, finestra di 30 giorni, soglie minime, stati del diario errori, normalizzazione dei pesi, separazione dal voto ufficiale, enum corrotti, non-mutazione e input materia non valido.
