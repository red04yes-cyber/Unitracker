# Security review — Weekly Review workspace binding (2026-11-28)

## Scope and method

Review statica del delta rispetto a `main` (`98e6ca1ec5ee84759497763a19a823f8f4957560`).
File esaminati: `StudyWorkspaceViewModel.cs`, `WeeklyReviewServiceTests.cs`,
`CompositionRootTests.cs` e `FREEBUFF-UI-CONTRACTS.md`. La review riguarda
l'integrazione read-only del servizio settimanale già esistente; non sostituisce
un audit runtime dell'intera applicazione.

Esiti ammessi: **superato**, **non applicabile**, **non verificabile**,
**problema rilevato**.

| # | Area / rischio | Metodo ed evidenza | Esito | Correzione / test |
|---:|---|---|---|---|
| 1 | Scrittura accidentale su `main` | Compare `main...feat/weekly-review-workspace-binding-20261128`: il branch dedicato è ahead e non modifica il ref base | Superato | Tutte le scritture eseguite sul branch dedicato |
| 2 | Modifiche estranee o scope creep | Compare GitHub: cinque file applicativi/test/documentazione previsti; nessun altro path | Superato | Diff limitato a binding, test e contratto |
| 3 | Segreti hardcoded nel delta | Ispezione del diff e dei nuovi fixture; nessuna credenziale o valore segreto | Superato | Nessun segreto aggiunto |
| 4 | Modifica involontaria delle funzioni AI | Path del diff: nessun provider, prompt, credenziale AI o servizio AI | Superato | Nessun file AI modificato |
| 5 | Chiamate HTTP/HTTPS non autorizzate | Il delta non contiene client HTTP, socket o chiamate remote | Non applicabile | Nessun codice di rete aggiunto |
| 6 | Redirect o SSRF | Nessun URL o endpoint viene costruito o seguito dal delta | Non applicabile | Nessuna superficie URL introdotta |
| 7 | Token e credenziali | Il binding non legge né modifica vault, token Moodle o chiavi provider | Non applicabile | Nessuna gestione di segreti aggiunta |
| 8 | Leakage nei log/eccezioni | Nessuna nuova chiamata di logging o serializzazione di errori | Superato | Il riepilogo è esposto solo come stato del ViewModel |
| 9 | Scrittura filesystem | Nessuna API `File`, `Directory` o stream introdotta | Non applicabile | Nessuna scrittura locale aggiunta |
| 10 | Path traversal | Nessun path o nome file è accettato dal nuovo flusso | Non applicabile | Nessun percorso filesystem introdotto |
| 11 | SQL injection / query dinamiche | Nessuna query SQL o concatenazione di comandi DB nel delta | Non applicabile | Nessuna query aggiunta |
| 12 | Schema e migrazioni | Nessuna entity, tabella o migration modificata | Non applicabile | Nessuna migrazione necessaria |
| 13 | Deserializzazione non sicura | Nessun parser JSON/XML/BinaryFormatter o deserializzatore introdotto | Non applicabile | Nessuna deserializzazione aggiunta |
| 14 | Import e archivi | Nessun import, ZIP o estrazione file nel delta | Non applicabile | Nessun import aggiunto |
| 15 | Export e formula injection | Il riepilogo non scrive CSV/XLSX/JSON e non modifica l'export esistente | Non applicabile | Nessun export aggiunto |
| 16 | Backup e restore | Nessun flusso di backup o ripristino modificato | Non applicabile | Nessuna operazione sui backup |
| 17 | Crittografia e primitive crittografiche | Nessuna chiave, algoritmo o buffer crittografico usato | Non applicabile | Nessuna crittografia aggiunta |
| 18 | DPAPI e key material | Nessuna chiamata a DPAPI o accesso al vault | Non applicabile | Nessun materiale chiave accessibile |
| 19 | ACL e permessi locali | Nessuna modifica a directory, ACL o file privati | Non applicabile | Nessuna variazione dei permessi |
| 20 | Processi e shell | Nessun `Process.Start`, shell o comando esterno | Non applicabile | Nessuna esecuzione di processi |
| 21 | Reflection / codice dinamico | Nessuna reflection, assembly load o compilazione dinamica | Non applicabile | Nessun caricamento dinamico |
| 22 | Injection nel markup WPF | Nessun XAML/code-behind/tema modificato; il delta espone normali proprietà .NET | Superato | Binding testato a livello ViewModel; nessun parser markup introdotto |
| 23 | Azioni automatiche da suggerimenti | `SuggestedActions` resta testo descrittivo; nessun comando o handler viene creato | Superato | Nessuna azione automatica; le proposte non mutano dati |
| 24 | Mutazione involontaria dei dati | Test `CreateDoesNotMutateTheSourceStudyData` verifica conteggi, riferimenti e timestamp | Superato | Test read-only aggiunto |
| 25 | Esposizione di note private | Il riepilogo non legge note/session text; le evidenze degli error pattern sono domande già presenti e restano nel binding locale | Superato | Nessun log/export; UI deve trattare `ErrorPatterns.Evidence` come dato personale |
| 26 | Interpretazione di testo non attendibile | Le stringhe sono dati di presentazione, non HTML, XAML dinamico o istruzioni eseguite | Superato | Nessun renderer o evaluator aggiunto |
| 27 | Confini temporali e timezone | I test costruiscono timestamp nel fuso locale del runner; il calcolo usa il percorso esistente di `ProgressService` | Superato | Test di intervalli e sovrapposizioni aggiunti |
| 28 | Overflow di date | Test `CreateRejectsAWeekStartThatCannotContainSixFollowingDays` verifica il limite di `DateOnly` | Superato | Il fallimento avviene prima di qualsiasi mutazione |
| 29 | Ampiezza della query | Il servizio usa una sola settimana (sette giorni), senza intervallo arbitrario fornito dalla UI | Superato | Range fisso; nessun input numerico aggiunto |
| 30 | Dati reali nei fixture | I nuovi test usano ID e contenuti sintetici, senza account, voti o documenti reali | Superato | Fixture sintetiche |

## Risultato

- **Superati:** 14
- **Non applicabili:** 16
- **Problemi rilevati:** 0
- **Non verificabili in questa fase:** scansione Gitleaks e NuGet audit sul nuovo head; saranno verificati dal workflow Security scan della PR.
- **Test/build locali:** non eseguiti; l'ambiente non dispone del .NET SDK.
- **Rischio residuo privacy:** `WeeklyReview.ErrorPatterns.Evidence` può contenere testo di domande inserite dallo studente. È esposto solo al binding locale già previsto dal servizio; la UI non deve inserirlo in log o export senza scelta esplicita.
- **Ambito escluso:** nessuna modifica AI, nessuna dipendenza, rete, persistenza, migrazione o UI visuale.
