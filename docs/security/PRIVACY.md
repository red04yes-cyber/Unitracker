# Privacy di UniTracker

Questa nota descrive il comportamento realmente implementato. Non è una
informativa legale e non sostituisce quella dell'Università o dell'istanza
Moodle.

## Dati trattati

| Dato | Dove | Finalità | Conservazione |
| --- | --- | --- | --- |
| Corsi, CFU, esami, voti, piano di studi | SQLite `unitracker.db` in `%LOCALAPPDATA%\UniToStudyHub` | funzionamento dell'app | fino a cancellazione |
| Note di corso e note di sessione | SQLite | promemoria personale | fino a cancellazione |
| Attività, sessioni, Pomodoro, missioni | SQLite | registro studio e statistiche | fino a cancellazione |
| PDF importati e testo estratto | cartella documenti dell'app | consultazione e citazioni | fino a cancellazione |
| Token Moodle | `secrets/moodle-token.secret`, cifrato con DPAPI | autenticazione all'istanza scelta | fino a disconnessione |
| Chiavi AI | `secrets/ai-key.<nome>.secret`, cifrate con DPAPI | chiamate al provider scelto | fino a cancellazione |
| Impostazioni AI | SQLite | nome della chiave, endpoint, modello, consenso | fino a cancellazione |
| Backup | `backups/*.utbk` cifrati | ripristino | massimo 10 backup / 90 giorni |
| Log diagnostici | `logs/unitracker*.log` redatti | diagnosi locale | 512 KB per file, 3 file |

Le credenziali non compaiono mai in export, backup, log o URL.

## Flussi di rete

- **Moodle**: solo dopo configurazione esplicita dell'URL e del token, e solo su
  azione dell'utente (connessione, aggiornamento corsi, download). HTTPS
  obbligatorio; il token viaggia nel corpo POST; redirect non seguiti; timeout e
  limiti di dimensione. Nessuna chiamata all'avvio se l'integrazione non è
  configurata.
- **AI**: solo se l'utente ha configurato una chiave e ha dato consenso esplicito
  all'invio a un servizio esterno. Endpoint HTTPS pubblico, con blocco di
  loopback, link-local e indirizzi privati; prompt delimitato e limitato; limite
  giornaliero di richieste. Nessun invio automatico di note, PDF o cronologia.
- **Nessun altro servizio**: niente telemetria, analytics, crash reporting,
  controllo aggiornamenti o sink remoto.

## Consenso e revoca

Il consenso è granulare e separato per Moodle e AI. Si revoca disconnettendo
Moodle (che cancella il token) o eliminando la chiave AI; da quel momento le
chiamate vengono rifiutate prima di raggiungere la rete.

## Export e cancellazione

- **Export standard**: corsi, voti, piano, attività e calendario, senza note
  private e senza credenziali.
- **Export completo**: richiesto esplicitamente dall'utente, include anche le
  note private, l'inventario e la versione dello schema. Non include token,
  chiavi, backup o log.
- **Cancellazione**: rimuove dati, credenziali, backup, file legacy e log;
  sovrascrive il contenuto dei file prima di eliminarli (best-effort) e azzera
  il database. L'operazione è irreversibile e va confermata.

## Note private e monitoraggio

Le note marcate private restano fuori dagli export di default; compaiono solo
nell'export completo, che è una scelta esplicita.

L'app non monitora il PC: non registra tasti, non enumera processi o finestre,
non legge file fuori dalle cartelle che l'utente sceglie e non invia dati a
servizi esterni. Il tempo di studio è quello registrato dall'utente tramite le
funzioni dell'app (timer, quick-log, Pomodoro), non una sorveglianza in
background.

## Limiti

- SQLite non è cifrato: si raccomanda BitLocker o EFS sul dispositivo.
- DPAPI protegge i segreti per l'account Windows corrente; un altro utente non
  può leggerli.
- Un backup protetto da password dipende dalla robustezza della password scelta
  (minimo 12 caratteri); la password non è recuperabile.
- La cancellazione su SSD/unità con wear leveling è best-effort: il sistema
  operativo può conservare copie residue.


## Database encryption and Moodle files

The live SQLite database uses SQLCipher v4 (AES-256-CBC + HMAC-SHA512). A random 256-bit key is stored only as a DPAPI-protected secret for the current Windows account. On first startup, an existing plaintext SQLite database is copied into an encrypted temporary database, checked for integrity, then atomically replaced. The plaintext database is not retained.

Backups are portable AES-256-GCM containers. Their short-lived SQLite snapshot is scrubbed after encryption. Password-protected backups can be restored on another Windows account or PC; the restore process re-encrypts the database with the destination account's DPAPI key. DPAPI-protected backups remain tied to the original account.

Moodle files are downloaded under the user's Documents folder in `UniToStudyHub`, with private folder permissions, path traversal protection, per-file and per-course limits, and executable/macro extension blocking. These files are not encrypted by SQLCipher.
