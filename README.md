# UniTo StudyHub

Applicazione desktop Windows per organizzare corsi, materiali Moodle, avvisi, esami e carriera universitaria.

## Stato
Applicazione desktop WPF per Windows su .NET 10. Include dashboard, carriera e tentativi, planner collegato al calendario, Study Lab, Accademia, progressi, backup/ripristino, privacy locale e integrazione Moodle facoltativa. Include AI Study Director con provider OpenAI, Google Gemini e Ollama locale, configurazione e consenso esplicito, pianificazione assistita e simulazione orale.

## Funzionalità previste
- Dashboard con media ponderata, proiezione indicativa del voto di partenza e CFU completati.
- Anagrafica corsi, registrazione voti e simulazione di voti ipotetici.
- Pianificazione degli esami nelle sessioni invernale, estiva e autunnale con bilanciamento dei CFU.
- Client Moodle REST per corsi, contenuti, calendario e forum, nei limiti delle funzioni autorizzate.
- Sincronizzazione di tutti i moduli/attività restituiti dal corso Moodle, metadati specifici delle attività per le funzioni Web Services autorizzate, stato di completamento personale quando disponibile, forum e calendario.
- Download automatico degli allegati esposti da `core_course_get_contents` e dalle API attività abilitate; i file sono organizzati per corso/sezione e filtrati per sicurezza (limite 250 MB per file, 1 GB per corso, estensioni eseguibili e macro bloccate).
- Provider AI OpenAI, Gemini e Ollama con chiavi API protette tramite DPAPI, consenso esplicito e Ollama limitato a loopback.\n- Esportazione completa JSON, anteprima di importazione, merge non distruttivo e replace con backup cifrato preventivo.\n- Reset privacy selettivo e pacchetto diagnostico locale con consenso, redazione log e senza upload automatico.\n- Cifratura del database SQLite con SQLCipher v4 (AES-256-CBC + HMAC-SHA512) e chiave casuale protetta da DPAPI; backup AES-256-GCM ulteriormente protetti da password o DPAPI (la chiave SQLCipher resta legata all'account Windows).

## Requisiti
- Windows 10/11 x64
- .NET 10 SDK
- URL dell'istanza Moodle utilizzata
- Web Services REST abilitati e token autorizzato dall'istanza

## Build
Da PowerShell:
```powershell
dotnet restore .\src\UniToStudyHub\UniToStudyHub.csproj
dotnet publish .\src\UniToStudyHub\UniToStudyHub.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

## Integrazione Moodle
L'app usa esclusivamente le API Web Services autorizzate. Non aggira SSO, non estrae cookie e non tenta di bypassare controlli di accesso. La disponibilità dei dettagli specifici, dei file e degli stati di completamento dipende dalle funzioni Web Services abilitate per il token. Le attività non esposte dal server vengono comunque indicizzate dai moduli del corso, ma non è possibile sincronizzare tentativi o dati privati che Moodle non rende disponibili a quel token.

## Privacy e limiti
I dati di carriera restano in locale, in una cartella con ACL limitata all'utente Windows corrente. Il token Moodle e le chiavi AI sono protetti con DPAPI; i backup sono cifrati con AES-256-GCM e protetti da DPAPI o da una password scelta dall'utente. Non esiste telemetria: nessun dato viene inviato a servizi esterni senza configurazione e consenso espliciti. L'app offre esportazione completa e cancellazione selettiva o totale dei dati; l'esportazione completa include anche le note private, ma esclude token e chiavi API. Il pacchetto diagnostico è opt-in e può includere solo log redatti. Il database SQLite è cifrato con SQLCipher v4; la chiave è vincolata all'account Windows tramite DPAPI. I backup sono cifrati due volte (SQLCipher per il database e AES-256-GCM per il contenitore); la chiave SQLCipher è protetta da DPAPI e quindi il ripristino richiede lo stesso account Windows. La password protegge il contenitore, ma non rende la chiave DPAPI trasferibile. Il pianificatore produce date indicative, non appelli ufficiali. Non è incluso un connettore Esse3; le date ufficiali vanno verificate sui sistemi UniTo. Dettagli in `docs/security/PRIVACY.md`.

## Obiettivo laurea
Ottobre 2027 (configurabile).

## CI
Il workflow GitHub Actions in `.github/workflows/build-windows.yml` esegue test, build WPF, publish self-contained Windows x64, smoke test di avvio, crea un pacchetto portabile ZIP con checksum SHA-256 e carica gli artefatti su push/PR per `main`, `develop` e branch `work/**`.

Il workflow `.github/workflows/core-domain-validation.yml` valida le PR e i push sui branch core domain: esegue tutti i test, compila l'app WPF, pubblica l'eseguibile Windows x64 e verifica l'avvio della finestra con un profilo temporaneo.

Il workflow `.github/workflows/security-scan.yml` esegue secret scanning con Gitleaks sulla cronologia e sul working tree, con permessi di sola lettura e output redatto, e un audit NuGet bloccante per le dipendenze dell'app e dei test (NU1901–NU1904 trattati come errori).

## Limiti noti
- L'integrazione Moodle dipende dai servizi REST e dai permessi concessi dall'istanza; non sostituisce Esse3 e non garantisce la disponibilità di ogni endpoint.
- Il pianificatore propone date indicative e non consulta gli appelli ufficiali.
- Prima del rilascio su `main`/`develop`, un amministratore del repository deve rendere obbligatori i check CI nelle regole di protezione dei branch.

## Documentazione
- Moodle Web Services: https://docs.moodle.org/503/en/Web_services
- .NET 10: https://dotnet.microsoft.com/download/dotnet/10.0
