# Controlli mirati Pomodoro — 18 novembre 2026

## Metodo e perimetro

Revisione source-level di `PomodoroService.cs`, `PomodoroModels.cs`, `StudyDataEntityMapper.cs`, `ActivityAndMissionServices.cs`, `CompositionRoot.cs`, test SQLite e workflow Actions. Non è un penetration test né un audit dell'intera applicazione.

| # | Controllo / rischio | Metodo ed evidenza | Esito |
|---:|---|---|---|
| 1 | Durata Focus fuori intervallo | `PomodoroService.UpdateSettings`: 1–1440 minuti | Superato |
| 2 | Pausa breve fuori intervallo | `UpdateSettings`: 1–240 minuti | Superato |
| 3 | Pausa lunga fuori intervallo | `UpdateSettings`: 1–240 minuti | Superato |
| 4 | Soglia cicli non valida | `UpdateSettings`: 1–12 | Superato |
| 5 | Configurazione persistita alterata | `StudyDataEntityMapper.ValidatePomodoro` controlla gli stessi limiti | Superato |
| 6 | Enum di fase/stato corrotti | Validazione `Enum.IsDefined` nel mapper | Superato |
| 7 | ID timer duplicato o inatteso | Il mapper richiede l'ID stabile `current` | Superato |
| 8 | Timer orfano | Il mapper richiede una sessione esistente | Superato |
| 9 | Timestamp mancanti | Il mapper valida inizio fase, inizio finestra e aggiornamento | Superato |
| 10 | Secondi negativi | Il mapper rifiuta secondi attivi/residui negativi | Superato |
| 11 | Contatori ciclo fuori dominio | Il mapper valida numero ciclo e focus completati | Superato |
| 12 | Timer attivo senza residuo | Il mapper richiede residuo positivo per Running/Paused | Superato |
| 13 | Timer Running senza deadline | Il mapper richiede `PhaseEndsAt` | Superato |
| 14 | Timer Paused con deadline | Il mapper richiede `PhaseEndsAt == null` | Superato |
| 15 | Timer Completed senza deadline | Il mapper richiede la deadline di completamento | Superato |
| 16 | Stato sessione e timer discordanti | Il mapper verifica la combinazione Running/Paused | Superato |
| 17 | Ciclo collegato a sessione inesistente | Il mapper verifica il riferimento del ciclo | Superato |
| 18 | Tipo di pausa non valido | Il mapper rifiuta Focus come tipo di pausa | Superato |
| 19 | Secondi Focus superiori all'intervallo | Il mapper confronta secondi e intervallo registrato | Superato |
| 20 | Secondi pausa superiori all'intervallo | Il mapper confronta secondi e intervallo registrato | Superato |
| 21 | Task collegato inesistente | `PomodoroService.Start` e `ActivityLedgerService.SyncFocusSession` verificano il task | Superato |
| 22 | Task non più aperto | Start rifiuta task diversi da Todo/InProgress | Superato |
| 23 | Task e sessione di corsi diversi | Start/ledger verificano la coerenza del corso | Superato |
| 24 | Duplicazione del tempo Focus | Il ledger aggiorna la stessa attività tramite SessionId | Superato |
| 25 | Accredito automatico durante chiusura app | Il timer usa timestamp persistiti e non avvia automaticamente un nuovo Focus dopo la pausa | Superato |
| 26 | Rifiuto di Pause con timer già mutato | Revisione del percorso e test di regressione con sessione non Running | Problema rilevato, corretto |
| 27 | Rifiuto di Resume con timer già mutato | Revisione del percorso e test di regressione con sessione non Paused | Problema rilevato, corretto |
| 28 | Clock rollback che estende il residuo | Confronto Snapshot prima/dopo correzione | Problema rilevato, corretto |
| 29 | Overflow nella conversione dei secondi | Analisi della conversione intervallo→int e test con `DateTimeOffset.MaxValue` | Problema rilevato, corretto |
| 30 | Rete, file o segreti nel servizio Pomodoro | Ispezione del servizio: nessuna chiamata HTTP, accesso a file o gestione credenziali | Non applicabile |

## Esito

- Controlli eseguiti: 30.
- Superati: 25.
- Problemi rilevati e corretti in questa slice: 4.
- Non applicabili: 1.
- Problemi aperti: 0 nel perimetro esaminato.
- Non verificabili: esecuzione dei test e CI sul nuovo branch, in attesa dei workflow.

La verifica dei workflow esistenti conferma `permissions: contents: read`, checkout senza credenziali persistite e riferimenti Actions pin immutabili. Gitleaks e NuGet audit sono previsti dal workflow security; il loro esito sul nuovo commit resta da attendere. Nessuna modifica a workflow, AI, provider, database o schema.
