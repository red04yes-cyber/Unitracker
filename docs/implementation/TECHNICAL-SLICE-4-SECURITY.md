# Technical slice 4 — Sicurezza e privacy

## Stato

Branch: `work/freebuff-issue-39-security`, derivato da
`origin/work/freebuff-issue-39-sprint-a` (commit `b11f6af`). PR separata con base
`work/freebuff-issue-39-sprint-a`. Nessun merge e nessuna riscrittura della
cronologia.

Nessuna UI è stata modificata: niente XAML, code-behind, temi, layout o
`Presentation/Views/**`. Le modifiche a `MainViewModel` si limitano a messaggi
sanificati e all'azzeramento del token dopo la connessione.

L'audit completo, l'esito della scansione dei segreti e i rischi residui sono in
`docs/security/SECURITY-AUDIT.md`; l'inventario dei dati è in
`docs/security/PRIVACY.md`.

## 1. Segreti e repository

- Nessuna credenziale reale trovata in sorgenti, configurazioni, fixture,
  workflow o cronologia Git disponibile (102 commit, `git log --all -p`).
- `.gitignore` esclude database, token, segreti e backup cifrati.
- `.github/workflows/security-scan.yml`: gitleaks 8.30.1 con checksum SHA-256
  verificato, `--redact`, `permissions: contents: read`, checkout con
  `persist-credentials: false`, scansione della cronologia (`--log-opts="--all"`)
  e del working tree.
- `.gitleaks.toml` estende le regole di default con token Moodle e blob DPAPI;
  l'allowlist copre solo fixture e documentazione, con il trade-off documentato.

## 2. Credenziali

- `ISecretProtector` / `DpapiSecretProtector` e `ProtectedSecretStore`
  (envelope `UTSK`, DPAPI con entropia, scrittura atomica, `Delete`,
  `DeleteAll`, nessun fallback su blob corrotto).
- `MoodleTokenVault` e `WindowsTokenVault`: migrazione di `token.bin`, buffer
  azzerati, token normalizzato e validato, `Delete`.
- `AiCredentialStore`: chiavi per nome, salvate come `ai-key.<nome>.secret`;
  il database conserva solo il nome della chiave e la variabile d'ambiente.
- `AiSettings` non contiene più la chiave: `ResolveApiKey` la legge dal vault al
  momento della chiamata.

## 3. Database e file locali

- `LocalFileSecurity`: DACL limitata all'utente, modalità `0700`/`0600`,
  scrittura atomica file+rename, scrub e cancellazione, pulizia dei temporanei,
  controllo `IsInside`.
- `StudyHubDbContextFactory`: directory privata, cleanup dei temporanei,
  connection string costruita e `DescribeProtection`.
- **SQLite non è cifrato**: `DatabaseProtectionStatus` lo dichiara e raccomanda
  BitLocker/EFS. Nessuna dipendenza aggiunta e nessuna affermazione falsa.
- `CalendarEventStore`, `LocalDataStore` e `LegacyJsonMigrator` usano ACL,
  scrittura atomica e limiti di dimensione.

## 4. Backup e ripristino

- Formato v2 `UTBK2`: AES-256-GCM, chiave casuale per backup, avvolta con DPAPI
  o derivata con PBKDF2-SHA256 (600 000 iterazioni, minimo 100 000 in lettura).
- Manifest con versione, timestamp, hash e dimensione; verifica completa prima
  del ripristino; safety backup prima della sostituzione.
- Un backup corrotto o alterato non sovrascrive un database valido: il ripristino
  avviene su file temporaneo e solo dopo la verifica.
- I backup v1 `.db` in chiaro restano verificabili e ripristinabili ma non
  vengono più creati.
- `PruneAsync` applica la retention (10 backup, 90 giorni).

## 5. Log e diagnostica

- `AppDiagnostics`: log locale redatto, rotazione a 512 KB con 3 file, nessun
  sink remoto, nessuna telemetria.
- `SecurityRedaction`: redazione di token, password, header `Authorization`,
  URL con token, email, percorsi utente e chiavi private; messaggi utente
  generici con dettaglio utile solo per gli errori di validazione.
- `ExceptionForLog` non registra mai il valore di un segreto.

## 6. Moodle

- `MoodleApplicationService.DisconnectAsync` cancella il token e
  `ClearLocalMoodleReferences` rimuove URL e riferimenti ai corsi senza toccare
  carriera e voti.
- Ogni chiamata di rete richiede un token presente: dopo la disconnessione non
  viene riutilizzata alcuna credenziale.
- `MoodleClient`: endpoint policy, POST con token nel corpo, nessun redirect,
  timeout, limiti di risposta, errori sanificati.
- `DocumentSyncService`: estensioni eseguibili bloccate, nomi ridotti a foglia,
  contenimento nella cartella del corso, `.part` atomico.

## 7. AI e PDF

- `OutboundEndpointPolicy`: HTTPS, blocco di loopback/link-local/privati/CGNAT/
  multicast, `localhost`/`.local`/`.internal`, IP decimali ed esadecimali;
  loopback solo con opt-in esplicito.
- `AiRequestLimiter`, limiti di prompt (120 000 caratteri) e risposta (4 MB),
  clamp di token e temperatura, redirect disabilitati, gate di consenso.
- `PdfServices`: lettura singola bounded (25 MB) con hash sugli stessi byte,
  limiti 5000 pagine / 200 000 caratteri per pagina / 4 000 000 totali,
  `SafeFileName`, errori generici. L'importer applica il limite pagine anche con
  un estrattore diverso da PdfPig.
- Il testo estratto resta contenuto non attendibile e delimitato nel prompt.

## 8. Importazione ed esportazione

- ICS: limite 10 MB, campi sanitizzati e troncati, avvisi sugli eventi
  incompleti.
- JSON legacy: limite 64 MB, backup prima della migrazione, path redatti.
- Export JSON: escaping HTML (`\u003C`); CSV: guardia formula su note e tag;
  XLSX: inline string senza elementi `<f>`.
- Le note private sono escluse dagli export di default e incluse solo
  nell'export completo.

## 9. Privacy dell'app

- `PrivacyService`: inventario, export completo, cancellazione di dati,
  credenziali, backup, file legacy e log, retention.
- `PrivacyDefaults.TelemetryEnabled = false`; nessun analytics o crash report.
- Consenso separato per Moodle e AI; revoca tramite disconnessione o rimozione
  della chiave.

## Test

Nuovi file:

- `tests/UniToStudyHub.Tests/Security/SecurityTestDoubles.cs`
- `tests/UniToStudyHub.Tests/Security/SecretAndFileSecurityTests.cs`
- `tests/UniToStudyHub.Tests/Security/BackupSecurityTests.cs`
- `tests/UniToStudyHub.Tests/Security/RedactionAndEndpointTests.cs`
- `tests/UniToStudyHub.Tests/Security/PrivacyAndMoodleSecurityTests.cs`
- `tests/UniToStudyHub.Tests/Services/PdfImportSecurityTests.cs`
- `tests/UniToStudyHub.Tests/Services/ExportAndImportSafetyTests.cs`

Coprono: scrittura atomica e ACL, envelope e corruzione dei segreti, backup e
restore con password errata e file alterato, versione non supportata e backup
legacy, redazione e policy endpoint, inventario/export/cancellazione/retention,
disconnessione Moodle, URL non sicuri, path traversal nei nomi scaricati, limiti
PDF, formula injection, escaping JSON/XLSX e limiti ICS.

## Verifica eseguita

```
dotnet test tests/UniToStudyHub.Tests/UniToStudyHub.Tests.csproj
→ Passed: 227, Failed: 0, Skipped: 0

dotnet build src/UniToStudyHub/UniToStudyHub.csproj -p:EnableWindowsTargeting=true
→ Build succeeded
```

La suite gira su Linux con doppi di test per DPAPI e ACL; la build WPF è
compilata con targeting Windows ma non eseguita in questo ambiente. Il workflow
Windows resta il gate per il runtime.

## Limitazioni

- SQLite non cifrato a riposo.
- DPAPI legato all'utente Windows.
- Scrubbing delle stringhe best-effort.
- DNS rebinding fuori dal modello di minaccia.
- Allowlist gitleaks su `tests/` e `docs/`.
- Dependency audit report-only.

## Prossima slice sicura

1. Valutare SQLCipher o cifratura a livello di volume con una decisione
   documentata su licenza, manutenzione e migrazione.
2. Aggiungere un test Windows end-to-end per ACL, DPAPI e ripristino da backup.
3. Rendere il dependency audit bloccante dopo la pulizia degli avvisi correnti.
4. Collegare in UI le azioni privacy già esposte dal servizio, quando il
   proprietario UI lo pianifica.
