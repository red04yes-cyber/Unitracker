# Revisione mirata Moodle — limiti delle risposte e download

**Data:** 14 novembre 2026  
**Base analizzata:** `main` — `d77fd554f0b5d8617881046e112780123e1f11fc`  
**Branch di correzione:** `work/moodle-response-hardening-20261114`

## Perimetro e metodo

Revisione statica del client Moodle, della sincronizzazione documenti, della policy endpoint e dei test già presenti. Nessuna richiesta è stata inviata a UniTo o a un'istanza Moodle reale; tutti i test usano handler e payload sintetici.

Il limite di 8 MiB era già controllato da `ReadBoundedStringAsync`, ma `CallAsync` usava `HttpClient.PostAsync` con il comportamento predefinito `ResponseContentRead`: il corpo poteva quindi essere bufferizzato integralmente **prima** che il limite applicativo venisse applicato. È un problema concreto di consumo memoria/DoS da risposta sovradimensionata. È stato corretto passando a `SendAsync(..., ResponseHeadersRead, ...)` e mantenendo il controllo sia su Content-Length sia sui byte letti.

È stata inoltre rilevata una perdita d'integrità: `Encoding.UTF8.GetString` sostituiva byte UTF-8 malformati con il carattere di rimpiazzo. La risposta Moodle poteva quindi essere accettata con testo corrotto. Ora la decodifica è strict e l'input non valido viene rifiutato come `InvalidDataException`.

## Controlli mirati

| # | Area / rischio | Metodo ed evidenza | Esito |
|---:|---|---|---|
| 1 | URL base assoluto e valido | `OutboundEndpointPolicy.ValidateBaseUrl`; test `RedactionAndEndpointTests` | Superato |
| 2 | Trasporto HTTPS predefinito | Policy endpoint e test su HTTP non-loopback | Superato |
| 3 | Userinfo, query e fragment nell'URL base | Test `SsrfAndInsecureEndpointsAreRejected` | Superato |
| 4 | Reti private, loopback, link-local e metadata endpoint | Test SSRF con indirizzi IPv4/IPv6 e host riservati | Superato |
| 5 | Loopback solo con opt-in esplicito | Test `MoodleClientRequiresExplicitLoopbackOptIn` | Superato |
| 6 | Normalizzazione del token | `MoodleTokenVault.Normalize` e test `InvalidTokensAreRejected` | Superato |
| 7 | Metodo HTTP delle API Moodle | `CallAsync` usa POST | Superato |
| 8 | Token API assente da URI/query | Nuovo test `ApiTokenIsSentInThePostBodyAndNeverInTheRequestUri` | CI superata sul commit `33758ef`; incluso nei 357 test |
| 9 | Eccezione protocollo: token nel download file | `WithToken` usato solo dopo il controllo same-origin; protocollo file Moodle | Superato, rischio limitato al server autorizzato |
| 10 | Parametri token duplicati nel download | Nuovo test `DownloadReplacesDuplicateTokenQueryParametersWithOneCurrentToken` | CI superata sul commit `33758ef`; incluso nei 357 test |
| 11 | Redirect automatici | Handler di produzione creato con `AllowAutoRedirect = false`; test 302 con handler controllato | Superato in revisione codice; non è un test end-to-end di rete |
| 12 | Status HTTP non-success | `CallAsync` e `DownloadFileAsync` rifiutano status non-success | Superato |
| 13 | Content-Type API | Le risposte dichiarate non JSON vengono rifiutate | Superato |
| 14 | Content-Length API | Controllo preventivo rispetto a `MaximumApiResponseBytes` | Superato |
| 15 | Limite effettivo del body API | Lettura incrementale con limite di 8 MiB in `ReadBoundedStringAsync`; nuovo test su stream senza Content-Length | Codice e test superati in CI (`33758ef`) |
| 16 | Buffering prima del limite API | `PostAsync` bufferizzava prima del controllo; sostituito con `SendAsync(ResponseHeadersRead)`; nuovo test misura i byte effettivamente letti | Problema corretto; test superato in CI (`33758ef`) |
| 17 | UTF-8 malformato nelle risposte | Decodifica strict e nuovo test con sequenza non valida | Problema corretto; test superato in CI (`33758ef`) |
| 18 | JSON malformato | `JsonDocument.Parse` e conversione di `JsonException` in errore dati generico | Superato |
| 19 | Messaggio di eccezione Moodle | Nuovo test verifica che il token contenuto nel messaggio server venga redatto | Test superato in CI (`33758ef`) |
| 20 | Timeout delle richieste | `MoodleClient.RequestTimeout` = 45 secondi | Superato in revisione codice |
| 21 | Cancellazione API | Nuovo test cancella durante la lettura del body e attende `OperationCanceledException` | Test superato in CI (`33758ef`) |
| 22 | Capability Moodle | Le API attività sono chiamate solo se presenti nell'elenco funzioni concesso al token; test esistente | Superato |
| 23 | Numero di funzioni candidate | `GetCourseActivityDetailsAsync` limita a 100 candidate | Superato in revisione codice |
| 24 | Origine download: schema | `SameOrigin` confronta lo schema | Superato |
| 25 | Origine download: host | `SameOrigin` confronta l'host senza distinzione maiuscole/minuscole; test host esterno esistente | Superato |
| 26 | Origine download: porta | Nuovo test rifiuta stesso host su porta diversa prima della richiesta | Test superato in CI (`33758ef`) |
| 27 | Content-Length download | Nuovo test rifiuta un file dichiarato oltre 250 MiB senza sostituire il file esistente | Test superato in CI (`33758ef`) |
| 28 | Byte effettivi download | Il ciclo di copia interrompe oltre 250 MiB anche senza Content-Length | Superato in revisione codice; non viene trasferito un payload da 250 MiB nel test |
| 29 | Pagina HTML/login al posto del file | Nuovo test verifica il rifiuto HTML e la conservazione del file locale esistente | Test superato in CI (`33758ef`) |
| 30 | File temporaneo e sostituzione atomica | Scrittura su `.part`, rename solo dopo successo e cleanup in `finally`; nuovi test per redirect, HTML, oversize e cancellazione | Codice e test superati in CI (`33758ef`) |

### Controlli aggiuntivi verificati fuori dal conteggio

- `DocumentSyncService` passa la radice Moodle consentita al download e ripete i controlli di contenimento prima della creazione delle directory e del download.
- `LocalFileSecurity.IsInside` controlla i reparse point; i test dedicati coprono link e percorsi non contenuti.
- `DocumentSyncService.SafeName` riduce i nomi remoti a una foglia e gestisce traversal, caratteri di controllo e nomi dispositivo Windows.
- Le estensioni eseguibili/script e i documenti Office con macro sono bloccati dalla sincronizzazione.
- La sincronizzazione limita i file per corso a 1.000 e il volume complessivo a 1 GiB.
- La redazione di token/password/Bearer e URL con token è coperta da `RedactionAndEndpointTests`.
- La disconnessione elimina il token e l'export completo esclude token e chiavi: `PrivacyAndMoodleSecurityTests`.

## Modifiche effettive

- `src/UniToStudyHub/Services/MoodleClient.cs`: POST API con `ResponseHeadersRead`; decodifica UTF-8 strict; error handling esplicito per byte malformati.
- `tests/UniToStudyHub.Tests/Security/MoodleClientBoundaryTests.cs`: 12 test di regressione su token, limiti streaming, UTF-8, cancellazione, redazione, same-origin, redirect, HTML, file preesistenti, query token, deduplica allegati e cleanup.
- Nessuna modifica a XAML, temi, navigazione, schema dati, dipendenze, workflow o codice AI.

## Verifiche non eseguite e rischi residui

- Il container di questa esecuzione non dispone del .NET SDK: i test non sono stati eseguiti localmente. Sul commit `33758ef` il workflow Windows ha superato **357 test (0 falliti, 0 saltati)**, build Release WPF (**0 warning, 0 errori**), publish self-contained `win-x64`, smoke test di avvio, ZIP e checksum SHA-256. La security scan ha superato Gitleaks su history/working tree, pinning Actions e audit NuGet bloccante.
- Nessuna verifica runtime con credenziali o server Moodle reali; non è necessaria per i test automatizzati e non è stata tentata.
- Il limite di 250 MiB sul download è verificato nel codice sullo stream effettivo; il test automatico evita di trasferire centinaia di MiB e verifica il rifiuto anticipato tramite Content-Length.
- La protezione da DNS rebinding non è introdotta da questa slice; la policy attuale valida l'endpoint ma non pinna la risoluzione DNS.
- I test con handler sintetico non sostituiscono una prova di rete Windows sul comportamento del handler HTTP di produzione.

Il primo run build sul commit `295565d` ha rilevato un errore di tipo nel fixture helper `JsonBytes`; il helper è stato corretto nel commit `33758ef` e il run finale sullo stesso head ha superato tutti gli step. Nessun dato utente, token reale o contenuto universitario è stato usato. Nessuna modifica AI è stata effettuata.
