# Revisione di sicurezza e privacy — UniTracker

## Stato

- Data: 5 novembre 2026.
- Branch: `work/freebuff-issue-39-security`, derivato da
  `origin/work/freebuff-issue-39-sprint-a` (commit `b11f6af`).
- Base della PR: `work/freebuff-issue-39-sprint-a`. Nessun merge, nessuna
  riscrittura della cronologia Git.
- Perimetro: codice applicativo WPF/.NET 10, servizi, storage locale SQLite e
  file, integrazione Moodle, integrazione AI, import/export, GitHub Actions.
- Nessun valore di segreto è riportato in questo documento o nei log.

## Metodo

1. Revisione manuale di sorgenti, configurazioni, file JSON/XML, fixture,
   workflow e script.
2. Ricerca nella cronologia Git (`git log --all -p`, 102 commit sui ref
   disponibili nel clone) di token Moodle, API key, password, chiavi private e
   connection string.
3. Verifica dei flussi di rete (Moodle, AI), dei punti di log e dei messaggi
   mostrati all'utente.
4. Build e test dopo ogni correzione:
   `dotnet test tests/UniToStudyHub.Tests/UniToStudyHub.Tests.csproj` (227 test)
   e `dotnet build src/UniToStudyHub/UniToStudyHub.csproj -p:EnableWindowsTargeting=true`.

## Esito dell'audit dei segreti

Nessuna credenziale reale è stata trovata nei sorgenti, nelle configurazioni,
nelle fixture, nei workflow o nella cronologia Git disponibile.

Le uniche stringhe assimilabili a segreti sono deliberate e non funzionanti:

| Posizione | Contenuto | Giudizio |
| --- | --- | --- |
| test di redazione | `api_key=segreta` | fixture fittizia, usata per verificare la redazione |
| test di backup | `correct horse battery staple` | passphrase di test, nessun servizio la accetta |
| test di endpoint | URL `*.example.test` | dominii riservati, non instradabili |

- Nessun percorso utente assoluto è versionato: le directory runtime sono
  risolte da `%LOCALAPPDATA%` e non compaiono nei file del repository.
- Nessuna credenziale è stata revocata o ruotata: non ce n'era bisogno.
- La cronologia Git non è stata modificata.

Se in futuro venisse trovata una credenziale reale:

1. revocare il token Moodle da *Preferenze → Chiavi di sicurezza* dell'istanza
   (o chiedere al gestore dell'istanza) e generarne uno nuovo;
2. ruotare la chiave AI dal pannello del provider;
3. rimuovere il valore dal file corrente e considerare la revoca l'unica
   mitigazione efficace: riscrivere la cronologia non annulla una credenziale
   già esposta e richiede autorizzazione esplicita.

## Correzioni

| ID | Priorità | Problema | Correzione | File principali |
| --- | --- | --- | --- | --- |
| F-01 | P0 | I backup erano copie SQLite in chiaro con un sidecar SHA-256, quindi leggibili da chiunque avesse accesso alla cartella | Container `UTBK2` con AES-256-GCM; chiave casuale per backup avvolta con DPAPI (default) o derivata con PBKDF2-SHA256 (600 000 iterazioni, minimo 100 000 in lettura); verifica completa prima del ripristino | `Security/BackupCrypto.cs`, `Security/SecretProtection.cs`, `Services/BackupService.cs` |
| F-02 | P0 | Database, backup, JSON legacy e token ereditavano le ACL del profilo; scritture non atomiche e file temporanei non ripuliti | DACL limitata all'utente corrente (Windows) e modalità `0700`/`0600` (Unix); scrittura atomica file+rename; scrub e cancellazione; pulizia dei `.tmp` orfani | `Security/LocalFileSecurity.cs`, `Infrastructure/Data/StudyHubDbContextFactory.cs`, `Services/CalendarEventStore.cs`, `Services/LocalDataStore.cs` |
| F-03 | P0 | Il vault del token non offriva cancellazione, non usava entropia, non azzerava i buffer e scriveva in modo non atomico | `ProtectedSecretStore` con envelope `UTSK`, DPAPI con entropia, scrittura atomica, `Delete`/`DeleteAll`, nessun fallback su blob corrotto; migrazione del vecchio `token.bin` con scrub | `Security/SecretProtection.cs`, `Services/WindowsTokenVault.cs` |
| F-04 | P0 | Nessun secret scanning in CI | Workflow dedicato con gitleaks 8.30.1 scaricato e verificato con SHA-256, `--redact`, `permissions: contents: read`, checkout senza credenziali persistite; scansione di cronologia e working tree | `.github/workflows/security-scan.yml`, `.gitleaks.toml` |
| F-05 | P1 | L'endpoint AI accettava qualunque host HTTPS, inclusi loopback, link-local e indirizzi privati, e seguiva i redirect | `OutboundEndpointPolicy`: HTTPS obbligatorio, blocco di IP privati/loopback/link-local/CGNAT/multicast, `localhost`/`.local`/`.internal`, IP decimali/esadecimali; loopback solo con opt-in esplicito; redirect disabilitati | `Security/OutboundEndpointPolicy.cs`, `Services/AiServices.cs` |
| F-06 | P1 | Il client Moodle accettava risposte di dimensione illimitata, seguiva redirect, salvava HTML come materiale e scriveva i download direttamente sul file finale | Risposte limitate a 8 MB, download a 250 MB con file `.part` e rename atomico, contenimento nella cartella del corso, rifiuto di HTML, timeout, errori sanificati | `Services/MoodleClient.cs`, `Services/DocumentSyncService.cs` |
| F-07 | P1 | Import PDF con finestra TOCTOU tra hash e parsing, nessun limite di pagine o caratteri, errori del parser mostrati all'utente | Lettura singola bounded (25 MB) e hash sugli stessi byte; limiti 5000 pagine, 200 000 caratteri per pagina, 4 000 000 totali; nome file ridotto a foglia senza caratteri di controllo; errori generici | `Services/PdfServices.cs` |
| F-08 | P1 | Le eccezioni grezze potevano finire in UI e log con percorsi, URL o token | `SecurityRedaction` per log, eccezioni e messaggi utente; log locale ruotato e redatto; niente telemetria | `Security/SecurityRedaction.cs`, `Security/AppDiagnostics.cs`, `Presentation/ViewModels/MainViewModel.cs` |
| F-09 | P1 | Non esisteva una disconnessione Moodle: il token restava utilizzabile dopo la rimozione dell'integrazione | `DisconnectAsync` cancella il segreto, `IsConnected` guida lo stato, ogni chiamata di rete richiede un token presente; `ClearLocalMoodleReferences` rimuove URL e riferimenti ai corsi senza toccare la carriera | `Services/MoodleApplicationService.cs` |
| F-10 | P1 | JSON legacy e calendario importato senza ACL, scrittura atomica o limiti effettivi di lettura; campi non sanificati | Limite effettivo di 64 MB per JSON legacy e migrazione generale, 16 MB per il calendario JSON legacy e 10 MB per ICS; lettura bounded sullo stream aperto, backup prima della migrazione, sanitizzazione dei campi, scrittura atomica e ACL | `Security/BoundedFileReader.cs`, `Services/CalendarEventStore.cs`, `Services/LocalDataStore.cs`, `Services/LegacyJsonMigrator.cs`, `Services/IcsCalendarParser.cs` |
| F-11 | P2 | SQLite non è cifrato a riposo | Nessuna dipendenza aggiunta: `DatabaseProtectionStatus` dichiara esplicitamente che il database non è cifrato e raccomanda BitLocker/EFS. Nessuna affermazione di cifratura nel README | `Infrastructure/Data/StudyHubDbContextFactory.cs` |
| F-12 | P2 | Mancavano export completo, cancellazione dati e retention | `PrivacyService`: inventario, export JSON completo, cancellazione di dati/credenziali/backup/file legacy/log, retention (10 backup, 90 giorni) | `Services/PrivacyService.cs` |
| F-13 | P2 | Import senza limiti espliciti e campi importati non sanificati | Limiti di dimensione, sanitizzazione e messaggi di avviso; le note private restano escluse dagli export standard | `Services/IcsCalendarParser.cs`, `Services/StudyExports.cs` |

## Verificato senza modifiche

- Il token Moodle viaggia nel corpo POST, mai in URL o query string.
- Nessun contenuto scaricato viene eseguito: nel codice applicativo non ci sono
  `Process.Start`, `Assembly.Load`, hook di tastiera o enumerazione di finestre.
- Gli export JSON/CSV/XLSX già neutralizzavano HTML e formule; ora è coperto da
  test dedicati.
- Le note private sono escluse dagli export standard e incluse solo
  nell'esportazione completa richiesta esplicitamente.
- Il testo dei PDF è delimitato come contenuto non attendibile nel prompt AI;
  nessuna chiamata AI parte senza consenso e chiave configurata.
- Non esiste telemetria: nessun analytics, crash reporting o controllo aggiornamenti.

## Rischi residui e limitazioni

- **SQLite non cifrato**: chi ha accesso al profilo Windows può leggere il file.
  La raccomandazione è BitLocker/EFS; non è stata introdotta una dipendenza di
  cifratura SQLite in questa slice.
- **DPAPI legato all'utente Windows**: il token e le chiavi AI non sono
  trasferibili ad altro account. I backup protetti da password sono trasferibili
  per scelta esplicita dell'utente.
- **Scrubbing best-effort**: in .NET le stringhe sono immutabili; l'azzeramento
  riguarda i buffer `byte[]`. Non è una garanzia contro la memoria swapped.
- **DNS rebinding**: la policy valida URL e indirizzi, ma non pinna la risoluzione
  DNS. Un resolver compromesso resta fuori dal modello di minaccia.
- **ACL e DPAPI non eseguibili su Linux**: i test girano su Linux con doppi di
  test; la build WPF è verificata con targeting Windows e la validazione runtime
  resta al workflow Windows.
- **Allowlist gitleaks**: `tests/` e `docs/` sono esclusi perché contengono
  fixture deliberatamente fittizie. Una credenziale reale committata in quei
  percorsi non verrebbe segnalata; la revisione manuale resta necessaria.
- **Dependency audit report-only**: `dotnet list package --vulnerable` elenca le
  vulnerabilità ma non fallisce la build; l'aggiornamento è una decisione dei
  maintainer.

## Segnalazione

Eventuali sospetti segreti vanno segnalati in privato ai maintainer, senza
aprire una issue pubblica con il valore. Questo documento non contiene segreti.
