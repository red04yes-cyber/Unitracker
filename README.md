# UniTo StudyHub

Applicazione desktop Windows per organizzare corsi, materiali Moodle, avvisi, esami e carriera universitaria.

## Stato
Starter MVP WPF su .NET 10. Il progetto prevede dashboard, media ponderata, simulazione what-if, pianificazione indicativa delle sessioni, persistenza locale e client Moodle REST.

## Funzionalità previste
- Dashboard con media ponderata, proiezione indicativa del voto di partenza e CFU completati.
- Anagrafica corsi, registrazione voti e simulazione di voti ipotetici.
- Pianificazione degli esami nelle sessioni invernale, estiva e autunnale con bilanciamento dei CFU.
- Client Moodle REST per corsi, contenuti, calendario e forum, nei limiti delle funzioni autorizzate.
- Download e classificazione dei materiali didattici esposti dall'istanza Moodle.
- Protezione locale del token con Windows DPAPI e backup cifrati AES-256-GCM.

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
L'app usa esclusivamente le API Web Services autorizzate. Non aggira SSO, non estrae cookie e non tenta di bypassare controlli di accesso. La disponibilità delle funzioni dipende dalla configurazione dell'istanza e dai permessi del token.

## Privacy e limiti
I dati di carriera restano in locale, in una cartella con ACL limitata all'utente Windows corrente. Il token Moodle e le chiavi AI sono protetti con DPAPI; i backup sono cifrati con AES-256-GCM e protetti da DPAPI o da una password scelta dall'utente. Non esiste telemetria: nessun dato viene inviato a servizi esterni senza configurazione e consenso espliciti. L'app offre esportazione completa e cancellazione dei dati; le note private restano fuori dagli export di default. Il database SQLite **non** è cifrato: si raccomanda BitLocker o EFS. Il pianificatore produce date indicative, non appelli ufficiali. Non è incluso un connettore Esse3; le date ufficiali vanno verificate sui sistemi UniTo. Dettagli in `docs/security/PRIVACY.md`.

## Obiettivo laurea
Ottobre 2027 (configurabile).

## CI
Il workflow GitHub Actions in `.github/workflows/build-windows.yml` compila e pubblica l'artefatto Windows x64 a ogni push su `main`.

Il workflow `.github/workflows/core-domain-validation.yml` valida le PR e i push sui branch core domain: esegue tutti i test, compila l'app WPF, pubblica l'eseguibile Windows x64 e verifica l'avvio della finestra con un profilo temporaneo.

Il workflow `.github/workflows/security-scan.yml` esegue il secret scanning con gitleaks (versione pinnata e checksum verificato) sulla cronologia e sul working tree, con permessi di sola lettura e output redatto, e un audit non bloccante dei pacchetti vulnerabili.

## Documentazione
- Moodle Web Services: https://docs.moodle.org/503/en/Web_services
- .NET 10: https://dotnet.microsoft.com/download/dotnet/10.0
