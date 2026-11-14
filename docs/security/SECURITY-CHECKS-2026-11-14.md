# Controlli mirati di sicurezza — 14 novembre 2026

## Perimetro e metodo

Revisione del codice sul branch predefinito `main` al commit `d77fd554f0b5d8617881046e112780123e1f11fc`, prima di iniziare il lavoro sul registro attività. Sono stati letti i punti di ingresso e le implementazioni indicate sotto; questo è un controllo statico mirato, non un penetration test né una verifica runtime Windows.

**Esito:** 30 controlli mirati eseguiti. Non è emersa una nuova vulnerabilità che giustificasse una modifica alle parti di sicurezza già presidiate da PR aperte. È stata corretta una dichiarazione UI sulla privacy: il flag rende privata la nota, non elimina titolo e metadati dagli export standard.

## Registro dei 30 controlli

| # | Area e controllo | Esito | Evidenza / limite |
| ---: | --- | --- | --- |
| 1 | Protezione del token Moodle con DPAPI per l'utente Windows corrente | OK | `Services/WindowsTokenVault.cs`, `Security/SecretProtection.cs` |
| 2 | Limite dei byte per segreti ed envelope prima della lettura | OK | `ProtectedSecretStore.MaximumSecretBytes` e `MaximumEnvelopeBytes` |
| 3 | Validazione del token: lunghezza 8–512 e caratteri stampabili | OK | `MoodleTokenVault.Normalize` |
| 4 | Migrazione del vecchio token con limite di dimensione e protezione prima della cancellazione | OK | `MoodleTokenVault.MigrateLegacyToken` |
| 5 | Disconnessione: cancellazione e scrub best-effort del token corrente e del file legacy | OK | `MoodleTokenVault.Delete`, `LocalFileSecurity.ScrubAndDelete` |
| 6 | URL esterni: HTTPS obbligatorio, senza user-info, query o fragment | OK | `Security/OutboundEndpointPolicy.cs` |
| 7 | Blocco di loopback, reti private, link-local, CGNAT, multicast e host locali; eccezione loopback solo con opt-in | OK | `OutboundEndpointPolicy.IsBlockedHost` e `IsPrivateOrLoopbackAddress` |
| 8 | Redirect HTTP disabilitati nel client Moodle | OK | `MoodleClient` crea `HttpClientHandler { AllowAutoRedirect = false }` |
| 9 | Timeout esplicito delle richieste Moodle | OK | `MoodleClient.RequestTimeout = 45 s` |
| 10 | Risposte API Moodle limitate anche quando manca Content-Length | OK con follow-up | Lettura streaming con cap 8 MiB in `ReadBoundedStringAsync`; la validazione UTF-8 stretta è in revisione nella PR #88, non ancora in `main` |
| 11 | Download Moodle limitati a 250 MiB e copiati a blocchi | OK | Controllo sia su Content-Length sia sul totale letto |
| 12 | Download Moodle same-origin, directory consentita e file temporaneo prima della sostituzione | OK con limitazione di protocollo | Il token è aggiunto alla query solo per il download Moodle; le chiamate API usano POST body. Il download non segue redirect e l'URL tokenizzato non viene registrato |
| 13 | Percorsi Moodle contenuti, estensioni bloccate e limiti per corso/file | OK | `DocumentSyncService`: contenimento, estensioni bloccate, massimo 1 GiB e 1.000 file per corso |
| 14 | PDF: limite 25 MiB e lettura bounded anche da stream non seekable | OK | `PdfTextExtractor` e `PdfImportService` |
| 15 | PDF: limite massimo di 5.000 pagine | OK | `PdfImportService.EnsurePageLimit` |
| 16 | PDF: 200.000 caratteri per pagina e 4.000.000 complessivi | OK | Estrazione troncata con warning esplicito |
| 17 | PDF: magic bytes `%PDF-` e hash calcolato sugli stessi byte passati al parser | OK | `PdfImportService.ImportAsync` legge e valida un unico buffer bounded |
| 18 | ICS: limite di 10 MiB su file e testo importato | OK | `IcsImportService.MaximumFileBytes` |
| 19 | ICS: limite di occorrenze e guard contro espansioni di ricorrenza non terminate | OK con follow-up | Default 1.000 eventi e guard 20.000 iterazioni; la correzione DST/TZID è in revisione nella PR #85 |
| 20 | ICS: sanitizzazione e lunghezze massime per titolo, descrizione, luogo e corso | OK | `IcsCalendarParser.Sanitize` e limiti dei campi |
| 21 | Backup: cifratura autenticata AES-256-GCM | OK | `Security/BackupCrypto.cs` |
| 22 | Backup: chiave DPAPI oppure PBKDF2-SHA256 con limiti di iterazioni e password | OK | Minimo password 12 caratteri; iterazioni lette limitate a 100.000–4.000.000 |
| 23 | Backup: limite payload e pulizia dei buffer plaintext | OK | Limite del servizio 512 MiB; buffer azzerati nel `finally` |
| 24 | Restore: checksum, autenticazione/decrittazione, integrità SQLite e safety backup prima della sostituzione | OK con hardening in revisione | Verifica nel codice corrente; PR #84 aggiunge ulteriore hardening sui file locali e sul restore |
| 25 | Database: configurazione SQLCipher v4, AES-256-CBC/HMAC-SHA512 e chiave casuale protetta DPAPI | OK a livello sorgente | `StudyHubDbContextFactory`; la verifica runtime del database cifrato richiede Windows e non è stata eseguita in questo ambiente |
| 26 | ACL e permessi locali: DACL per utente Windows e modalità Unix 0700/0600 | OK con limite operativo | `LocalFileSecurity`; il filesystem può rifiutare ACL e il chiamante deve mostrare la limitazione |
| 27 | Scritture atomiche, flush, file temporanei e pulizia degli orfani | OK | `WriteAllBytesAtomicallyAsync`, `WriteAllTextAtomicallyAsync`, cleanup temporanei |
| 28 | Contenimento dei percorsi e rifiuto dei componenti reparse point | OK con follow-up | `LocalFileSecurity.IsInside` fallisce chiuso sui componenti esistenti; PR #82 contiene ulteriore hardening del perimetro filesystem |
| 29 | Import JSON: 64 MiB, profondità 64 e massimo 100.000 record | OK | `StudyDataImportService.PreviewAsync` |
| 30 | Export: formula injection CSV e note private escluse dagli export standard | OK | `NeedsFormulaGuard` e `NoteForExport`; titolo e metadati dell'attività restano esportabili |

## Correzione privacy applicata in questa slice

Il codice di export include titolo, tipo, materia e orari dell'attività, mentre omette il testo della nota quando `IsPrivate` è true e `IncludePrivateNotes` è false. La nuova UI inizialmente descriveva in modo troppo ampio la voce come esclusa dagli export.

Correzione applicata:
- nota privata attiva per default nel quick-log;
- campo titolo indicato come breve ed esportabile;
- checkbox e messaggi precisano che è la **nota** a essere esclusa dagli export standard;
- la voce rimane consultabile nel diario locale.

Non è stata modificata la semantica esistente degli export.

## Controlli CI aggiuntivi

Oltre ai 30 controlli sopra:
- workflow con `permissions: contents: read`;
- GitHub Actions referenziate tramite SHA immutabili;
- checkout con `persist-credentials: false`;
- Gitleaks su cronologia completa e working tree, con output redatto e checksum dell'archivio verificato;
- audit NuGet bloccante per NU1901–NU1904 su progetto app e test.

## Rischi residui e PR già aperte

Non sono state duplicate modifiche già in corso:
- **PR #82** — hardening aggiuntivo dei percorsi locali e dei reparse point;
- **PR #84** — letture locali bounded e hardening del ripristino backup;
- **PR #85** — preservazione TZID e ricorrenze attraverso DST;
- **PR #88** — risposta Moodle con UTF-8 stretto e limiti di streaming.

Restano inoltre limiti espliciti: la policy endpoint non pinna la risoluzione DNS (DNS rebinding fuori dal perimetro corrente); lo scrub su SSD, journal e snapshot è best-effort; il token nei download Moodle resta nella query per compatibilità di protocollo, ma non viene loggato; la verifica runtime SQLCipher/DPAPI richiede Windows.

## Modifiche di sicurezza preesistenti non alterate

Nessun codice AI è stato modificato. Nessuna dipendenza, chiamata di rete, telemetria o servizio esterno è stato aggiunto in questa slice.
