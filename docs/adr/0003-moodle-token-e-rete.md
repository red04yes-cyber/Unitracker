# ADR-0003 — Token Moodle e confine di rete

- **Data:** 2026-11-20
- **Stato:** Accepted — documenta la baseline già implementata su `main`
- **Ambito:** credenziali Moodle e richieste HTTP in uscita

## Contesto

Moodle è un'integrazione facoltativa verso un'istanza scelta dall'utente. Il token è una credenziale personale e gli URL e i file restituiti dal server devono essere trattati come input non attendibili.

## Decisione

- `MoodleTokenVault` conserva il token nel vault locale protetto da DPAPI per l'account Windows corrente, separato da `StudyData`.
- `MoodleApplicationService` richiede un token presente prima di avviare un'operazione di rete.
- `MoodleClient` valida la base URL con `OutboundEndpointPolicy`: HTTPS è richiesto per endpoint remoti; HTTP è ammesso soltanto per loopback quando il chiamante abilita esplicitamente lo scenario locale.
- Il client non segue redirect e applica un timeout alle richieste.
- Le chiamate Web Services REST usano POST e trasportano `wstoken` nel corpo form, non nella query.
- Il download dei file Moodle è l'eccezione richiesta dal protocollo: il singolo GET aggiunge `token=` alla query. Prima della richiesta il client verifica schema e stessa origine; non segue redirect, non registra l'URL tokenizzato, applica un limite dimensionale e pubblica il file tramite un temporaneo dopo il trasferimento.
- La disconnessione elimina il token dal vault senza cancellare i dati accademici locali.

## Alternative considerate

- **Token in chiaro in impostazioni o in `StudyData`:** rifiutato perché mescola credenziali e dati applicativi.
- **Token nella query di tutte le chiamate REST:** rifiutato; il POST con token nel corpo è la modalità ordinaria.
- **Seguire redirect o scaricare da host arbitrari:** rifiutato perché può trasferire credenziali o contenuti fuori dall'istanza autorizzata.
- **Scraping o aggiramento di login e permessi:** fuori dal modello; il server Moodle resta l'autorità sui permessi concessi al token.

## Conseguenze

- Il token DPAPI è legato all'account Windows corrente; una copia del database non lo trasferisce.
- La query del download resta una superficie sensibile imposta dal protocollo: l'eccezione non va estesa ad altre API e gli URL completi non devono finire in log o messaggi.
- Un errore di permessi o una capability Moodle non disponibile deve essere presentato come tale; non si deve simulare una sincronizzazione riuscita.
- Le modifiche al client devono mantenere test su HTTPS, redirect, stessa origine, limiti di risposta/download e redazione.

## Evidenze

- `src/UniToStudyHub/Services/WindowsTokenVault.cs`
- `src/UniToStudyHub/Services/MoodleApplicationService.cs`
- `src/UniToStudyHub/Services/MoodleClient.cs`
- `src/UniToStudyHub/Security/OutboundEndpointPolicy.cs`
- `tests/UniToStudyHub.Tests/Security/PrivacyAndMoodleSecurityTests.cs`
- `tests/UniToStudyHub.Tests/Services/MoodleExtendedSyncTests.cs`
