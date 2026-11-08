# Privacy e protezione dei dati di UniTo StudyHub

Questa nota descrive i comportamenti implementati nell'app. Non è un'informativa legale e non sostituisce quella dell'Università o dell'istanza Moodle.

## Dati e archiviazione

| Dato | Posizione | Protezione |
|---|---|---|
| Corsi, CFU, voti, carriera, calendario, sessioni, note, attività e impostazioni | `%LOCALAPPDATA%\UniToStudyHub\unitracker.db` | SQLCipher v4, AES-256-CBC + HMAC-SHA512 |
| Chiave del database | `secrets/database-key.secret` | Chiave casuale da 256 bit protetta da Windows DPAPI per l'account corrente |
| Token Moodle e chiavi AI | `secrets/*.secret` | Windows DPAPI |
| File scaricati da Moodle | `Documenti\UniToStudyHub\Moodle\` | ACL/cartelle private, validazione percorso, limiti dimensione, blocco eseguibili e macro |
| Backup | `backups/*.utbk` | AES-256-GCM, chiave protetta da DPAPI oppure derivata da password |

La cifratura SQLCipher copre i contenuti del database e i file journal/WAL creati dal motore cifrato. La chiave del database non viene salvata in chiaro, inclusa nei backup o scritta nei log.

## Migrazione del database esistente

Al primo avvio con questa versione, se viene rilevato il vecchio header SQLite in chiaro:
1. l'app apre il database precedente in sola lettura;
2. copia i dati in un database temporaneo SQLCipher con la nuova chiave;
3. verifica `PRAGMA integrity_check`;
4. chiude le connessioni, elimina in modo best-effort gli eventuali sidecar WAL/SHM in chiaro e sostituisce atomicamente il file precedente.

Se la conversione o la verifica fallisce, il database originale non viene sostituito e l'app interrompe l'apertura: non rigenera una chiave e non azzera i dati. La chiave DPAPI è vincolata all'account Windows che ha creato il database; per migrare su un altro account/PC usa un backup protetto da password.

## Backup e ripristino

I backup sono contenitori AES-256-GCM autenticati. Il contenuto SQLite temporaneo viene scritto nella cartella staging privata, cifrato nel contenitore e sovrascritto/eliminato al termine (best effort).
- **Backup con password:** protegge il contenitore esterno con AES-256-GCM, ma il database interno resta cifrato con la chiave SQLCipher protetta da DPAPI. Il ripristino richiede lo stesso account Windows.
- **Backup DPAPI:** può essere aperto solo dall'account Windows che lo ha creato.
- La password non è recuperabile. Usa una password robusta e conservala in un gestore password.

## Moodle

Moodle è facoltativo e viene contattato solo dopo che l'utente ha configurato URL/token e avviato una sincronizzazione. L'app:
- legge corsi, sezioni e moduli/attività esposti da `core_course_get_contents`;
- usa solo le funzioni Web Services elencate da Moodle per il token corrente;
- recupera dettagli specifici per le funzioni attività abilitate, tentativi/consegne personali per alcuni moduli (ad es. compiti, quiz, lezioni, workshop, SCORM e wiki) e stato di completamento quando consentito;
- sincronizza eventi di calendario e discussioni forum;
- scarica gli allegati esposti nei contenuti dei moduli e nelle risposte delle API attività abilitate.

Il token non viene inserito negli URL delle chiamate REST; le richieste API usano POST, i redirect non vengono seguiti e i download sono limitati alla stessa origine Moodle. I file vengono organizzati per corso/sezione/modulo in `Documenti\UniToStudyHub\Moodle\`. Sono bloccate estensioni eseguibili e macro; limiti attuali: 250 MB per file, 1 GB per corso e 1.000 file per corso. I file già scaricati vengono aggiornati quando Moodle espone una data di modifica più recente.

**Limite importante:** Moodle non offre un'unica API universale per ogni plugin o attività. I dettagli e i dati personali sincronizzabili dipendono dalle funzioni abilitate dall'amministratore e dai permessi del token. Le attività sono comunque indicizzate quando compaiono nei moduli del corso; tentativi, voti o allegati non esposti dalle API autorizzate non possono essere estratti dall'app. Non vengono aggirati SSO, ruoli o controlli di accesso.

I file Moodle non sono contenuti nel database SQLCipher: sono file separati nella cartella Documenti con permessi limitati e controlli sul percorso. Se vuoi proteggerli anche a riposo, abilita BitLocker o EFS sul volume.

## Privacy, export e cancellazione

- Nessuna telemetria, analytics o crash reporting remoto.
- Il token Moodle viene eliminato alla disconnessione; corsi, voti e storico accademico locale restano disponibili.
- L'export completo include le note private ma non token o chiavi.
- La funzione di cancellazione completa elimina record locali, credenziali, backup selezionati, file legacy, log e file Moodle scaricati. La chiave DPAPI del database viene conservata per mantenere apribile il database vuoto e poterlo riutilizzare.
- La sovrascrittura prima della cancellazione è best-effort: SSD, journaling, snapshot e wear levelling possono conservare copie residue.

## Limiti

- La cifratura del database è vincolata a Windows DPAPI e all'account Windows corrente.
- La protezione dei file Moodle scaricati dipende anche dai permessi del volume; SQLCipher non cifra quei file.
- Le funzioni Moodle non abilitate sul server non possono essere sincronizzate.
- L'app non sostituisce Esse3 né garantisce la correttezza o completezza delle informazioni pubblicate dall'istanza Moodle.
