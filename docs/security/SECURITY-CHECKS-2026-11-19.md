# Verifica mirata sicurezza e privacy — 19 novembre 2026

## Perimetro e metodo

Revisione statica del commit `d77fd554f0b5d8617881046e112780123e1f11fc` di `main`, con focus sul ciclo di vita dei log diagnostici e verifiche trasversali delle superfici già implementate. Sono stati letti sorgenti, test, workflow e documentazione; non sono stati eseguiti localmente .NET, WPF, DPAPI o test Windows tramite il connettore GitHub.

La modifica di questa slice riguarda soltanto il trattamento dei log locali. Nessun file, servizio, provider o flusso AI è stato modificato.

## Controlli

| # | Area / controllo | Evidenza | Esito |
|---:|---|---|---|
| 1 | Percorso del log corrente: rifiuto di file reparse/link | `Security/AppDiagnostics.cs`, `LocalFileSecurity.IsInside` | Superato con la modifica |
| 2 | Directory dei log: rifiuto di una root reparse/link prima di crearla o restringerne i permessi | `Security/AppDiagnostics.cs` | Superato con la modifica |
| 3 | Percorso esposto da `CurrentLogPath` verificato prima di restituirlo | `Security/AppDiagnostics.cs` | Superato con la modifica |
| 4 | Inventario dei log limitato ai tre nomi gestiti, senza wildcard ampia | `Security/AppDiagnostics.cs` | Superato con la modifica |
| 5 | Cancellazione usa lo stesso inventario consentito del writer | `Security/AppDiagnostics.cs` | Superato con la modifica |
| 6 | Cancellazione include log corrente e due rotazioni | `Security/AppDiagnostics.cs` | Superato con la modifica |
| 7 | Diagnostic bundle include le rotazioni gestite | `Services/PrivacyService.cs` | Superato con la modifica |
| 8 | Contenimento del percorso ricontrollato immediatamente prima dello scrub | `Security/AppDiagnostics.cs` | Superato con la modifica |
| 9 | Sorgente della rotazione verificata prima dello spostamento | `Security/AppDiagnostics.cs` | Superato con la modifica |
| 10 | Destinazione della rotazione verificata prima della sostituzione | `Security/AppDiagnostics.cs` | Superato con la modifica |
| 11 | Messaggio log redatto prima della scrittura | `Security/AppDiagnostics.cs`, `Security/SecurityRedaction.cs` | Superato |
| 12 | Eccezione redatta prima della scrittura | `Security/AppDiagnostics.cs`, `Security/SecurityRedaction.cs` | Superato |
| 13 | Caratteri di controllo rimossi per impedire log multilinea | `Security/SecurityRedaction.cs` | Superato |
| 14 | Lunghezza di ogni messaggio log limitata a 4.000 caratteri | `Security/SecurityRedaction.cs` | Superato |
| 15 | Rotazione attivata al limite di 512 KiB | `Security/AppDiagnostics.cs` | Superato |
| 16 | Numero di rotazioni conservate limitato a due | `Security/AppDiagnostics.cs` | Superato |
| 17 | Creazione del diagnostic bundle subordinata al consenso esplicito | `Services/PrivacyService.cs` | Superato |
| 18 | Database, documenti e credenziali esclusi dal bundle | `Services/PrivacyService.cs` | Superato |
| 19 | Log redatti nuovamente durante l'esportazione | `Services/PrivacyService.cs` | Superato |
| 20 | Limite di 512 KiB per file e 2 MiB complessivi nel bundle | `Services/PrivacyService.cs` | Superato |
| 21 | Protezione DPAPI legata all'utente Windows corrente | `Security/SecretProtection.cs` | Superato in revisione sorgente; runtime Windows non eseguito |
| 22 | Entropia contestuale passata a DPAPI per separare gli ambiti dei segreti | `Security/SecretProtection.cs` | Superato in revisione sorgente |
| 23 | Buffer unmanaged di input, output ed entropia azzerati nel `finally` | `Security/SecretProtection.cs` | Superato in revisione sorgente |
| 24 | Dimensione massima dell'envelope dei segreti definita e controllata | `Security/SecretProtection.cs` | Superato in revisione sorgente |
| 25 | Scrittura dei segreti tramite sostituzione atomica e directory privata | `Security/SecretProtection.cs`, `Security/LocalFileSecurity.cs` | Superato in revisione sorgente |
| 26 | Chiave, salt e nonce del backup generati casualmente | `Security/BackupCrypto.cs` | Superato in revisione sorgente |
| 27 | Cifratura backup con AES-GCM e tag di autenticazione | `Security/BackupCrypto.cs` | Superato in revisione sorgente |
| 28 | Header del container autenticato come associated data | `Security/BackupCrypto.cs` | Superato in revisione sorgente |
| 29 | Iterazioni PBKDF2 limitate tra 100.000 e 4.000.000 | `Security/BackupCrypto.cs` | Superato in revisione sorgente |
| 30 | Payload backup limitato a 8 GiB | `Security/BackupCrypto.cs` | Superato in revisione sorgente |
| 31 | Redirect HTTP automatici disabilitati nel client Moodle predefinito | `Services/MoodleClient.cs` | Superato in revisione sorgente |
| 32 | Timeout delle richieste Moodle limitato a 45 secondi | `Services/MoodleClient.cs` | Superato in revisione sorgente |
| 33 | Limite risposta API Moodle applicato prima del buffering completo | `Services/MoodleClient.cs` | Problema rilevato su `main`: `PostAsync` usa il completamento predefinito prima della lettura limitata; PR #88 aperta contiene la correzione, non duplicata qui |
| 34 | Download Moodle usa `ResponseHeadersRead`, Content-Length e limite effettivo sui byte letti | `Services/MoodleClient.cs` | Superato in revisione sorgente |
| 35 | Destinazione download verificata rispetto alla root consentita | `Services/MoodleClient.cs`, `Services/DocumentSyncService.cs` | Superato in revisione sorgente |
| 36 | Import PDF limitato a 25 MiB e verificato sulla firma `%PDF-` | `Services/PdfServices.cs` | Superato in revisione sorgente |
| 37 | Import PDF limitato per numero pagine e caratteri estratti | `Services/PdfServices.cs` | Superato in revisione sorgente |
| 38 | Action GitHub di terze parti fissate a SHA completo | `.github/workflows/*.yml`, `scripts/verify-github-actions-pinning.sh` | Superato in revisione sorgente |
| 39 | Workflow build e security scan con permessi repository `contents: read` | `.github/workflows/*.yml` | Superato in revisione sorgente |
| 40 | Checkout senza persistenza delle credenziali Git | `.github/workflows/*.yml` | Superato in revisione sorgente |
| 41 | Download Gitleaks verificato con SHA-256 e output redatto | `.github/workflows/security-scan.yml` | Superato in revisione sorgente |
| 42 | Secret scan sulla cronologia Git e sul working tree | `.github/workflows/security-scan.yml` | Superato in revisione sorgente |
| 43 | NuGet audit blocca gli advisory NU1901–NU1904 per app e test | `.github/workflows/security-scan.yml` | Superato in revisione sorgente |

## Problemi e rischi residui

1. **Log diagnostici — corretto in questa slice.** Il vecchio flusso di cancellazione non verificava i reparse point prima dello scrub e il pattern `unitracker*.log` non includeva i nomi prodotti dalla rotazione (`.log.1`, `.log.2`). Il diagnostic bundle aveva la stessa esclusione delle rotazioni. Il nuovo inventario usa nomi esatti, verifica il contenimento e viene condiviso da scrittura, cancellazione ed esportazione.
2. **Moodle API response buffering — non corretto qui.** Su `main`, il limite di 8 MiB viene applicato durante la lettura del body ma `PostAsync` usa il completamento predefinito, che può bufferizzare la risposta prima del limite. La PR #88 aperta propone `ResponseHeadersRead` e decodifica UTF-8 strict; non è stata duplicata.
3. **Protezione branch predefinito.** L'API repository riportava `main` con `protected=false`. Non è stata modificata la configurazione amministrativa: va abilitata la protezione del branch con review e status check obbligatori da parte di un amministratore.
4. **Verifica Windows.** DPAPI, ACL Windows, build WPF e smoke test non sono eseguibili dal connettore di sola gestione GitHub. Devono essere confermati dalla CI Windows della PR e, per i test con link, dai test Linux.

## Modifiche di questa slice

- `AppDiagnostics`: inventario esatto dei log, rifiuto di file/directory linkati, scrittura e rotazione con controlli di contenimento, cancellazione delle tre generazioni.
- `PrivacyService`: diagnostic bundle allineato allo stesso inventario, includendo le rotazioni.
- Test: copertura di cancellazione rotazioni, inclusione nel bundle, link a file esterno e link della directory log.
- Nessuna migrazione database, dipendenza aggiunta, modifica UI o modifica AI.
