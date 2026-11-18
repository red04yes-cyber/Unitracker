# Affidabilità delle transizioni Pomodoro — 18 novembre 2026

## Perimetro

Hardening circoscritto del servizio Pomodoro già registrato nel `CompositionRoot` e utilizzato da `StudyWorkspaceViewModel`. Nessuna nuova schermata, dipendenza, migration, chiamata di rete o modifica AI.

## Problemi riprodotti

1. `Pause` e `Resume` modificavano prima `PomodoroTimerData` e solo dopo chiedevano a `FocusSessionService` di eseguire la transizione collegata. Se la sessione rifiutava la transizione, il timer restava parzialmente aggiornato e non era più coerente con la sessione.
2. `Snapshot` calcolava il tempo residuo direttamente da `PhaseEndsAt - now`. Un arretramento dell'orologio poteva mostrare più tempo del periodo configurato; la conversione diretta di intervalli molto grandi in `int` poteva inoltre andare in overflow.

## Correzioni

- `Pause`: verifica la monotonicità rispetto all'inizio della fase, calcola i valori in variabili locali e transiziona la sessione Focus prima di mutare il timer.
- `Resume`: verifica l'orologio, calcola la nuova deadline prima di mutare lo stato e riprende la sessione Focus prima di aggiornare il timer.
- `Snapshot`: per una fase in esecuzione deriva il residuo dalla durata configurata meno il tempo attivo già limitato; una correzione all'indietro dell'orologio non estende la fase oltre la durata originaria.
- `ElapsedActive`: usa aritmetica `long` e limita il risultato alla durata della fase prima della conversione a `int`.

## Verifica aggiunta

`PomodoroServiceTests` aggiunge 15 casi:
- creazione e collegamento al task;
- pausa, ripresa e ricostruzione del tempo;
- rollback dello stato in caso di transizione Focus rifiutata;
- clock rollback e salti temporali molto grandi;
- riconciliazione a deadline, completamento pausa e mancato avvio automatico;
- ripresa della stessa sessione, skip e soglia pausa lunga;
- reset, limiti delle impostazioni e round-trip SQLite seguito da resume.

## Limiti

I test sono stati aggiunti ma non eseguiti localmente: nell'ambiente di esecuzione non è installato il .NET SDK. La verifica effettiva è demandata ai workflow Windows/.NET 10 della PR. Il round-trip SQLite è coperto da un test d'integrazione, ma non è ancora stato eseguito in questa esecuzione.
