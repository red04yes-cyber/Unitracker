# Persistenza dello stato letto delle notifiche

## Ambito

Questa slice rende persistente l'azione già esposta nell'inbox WPF **Segna tutti come letti**. Non introduce un nuovo pannello, una sincronizzazione automatica o una cronologia Moodle: questi restano fuori da questa consegna.

## Comportamento

- Gli avvisi derivati da eventi calendario e task continuano a essere calcolati dai dati locali esistenti.
- Quando l'utente segna gli avvisi visibili come letti, l'app salva le occorrenze dismissate nello stesso snapshot SQLite usato dal resto dei dati.
- Dopo il riavvio, la stessa occorrenza non viene mostrata di nuovo.
- Se cambia l'istante pianificato dell'evento/task, la nuova occorrenza ha una chiave diversa e torna visibile.
- Se il salvataggio fallisce, lo stato in memoria viene ripristinato e gli avvisi restano visibili.

## Dati e privacy

La tabella `NotificationDismissals` conserva solo:
- un digest SHA-256 (64 caratteri esadecimali) dell'identità sorgente e dell'istante UTC dell'occorrenza;
- l'istante in cui l'utente l'ha segnata come letta.

Titolo, dettaglio, note, URL, contenuti Moodle e token non vengono salvati in chiaro nella tabella. Il digest è deterministico e pseudonimo, non una forma di anonimizzazione: la protezione a riposo continua a dipendere dalla cifratura SQLCipher/DPAPI già adottata dal database. Il nuovo schema è additivo; le righe esistenti non vengono riscritte dalla migrazione. I record seguono il normale ciclo di backup/ripristino e cancellazione del database.

## Architettura

- `NotificationCenterService`: calcola la chiave occurrence-aware, filtra gli avvisi letti e registra una dismissal in modo idempotente.
- `StudyData.NotificationDismissals`: DTO parte dello snapshot applicativo condiviso.
- `NotificationDismissalEntity` e migrazione `20261114000100_AddNotificationDismissals`: persistenza SQLite.
- `StudyWorkspaceViewModel.ClearNotificationsAsync`: salva prima di svuotare l'inbox; in caso di errore rimuove le nuove dismissal in memoria.
- `SqliteStudyDataStore`: include la tabella nella sostituzione transazionale completa del dataset.

## Limiti noti

- La cronologia delle sincronizzazioni Moodle (nuovi/aggiornati/invariati/falliti e retry per corso) non è implementata da questa slice.
- Le notifiche Pomodoro generate in memoria non costituiscono ancora una cronologia persistente; questa modifica persiste il loro stato letto solo quando l'occorrenza è presente nell'inbox.
- Non è introdotta una retention automatica delle dismissal: evitare cancellazioni automatiche non richieste. Una futura UI potrà esporre una pulizia esplicita.

## Verifiche previste

I test coprono chiave stabile e priva di testo, idempotenza, nuova occorrenza dopo una modifica dell'orario, round-trip SQLite, validazione di chiavi malformate/duplicate, rollback transazionale e upgrade di un database esistente. La build WPF e l'esecuzione completa della suite restano delegate al workflow Windows della PR.
