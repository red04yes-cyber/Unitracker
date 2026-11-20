# Aggiornamenti automatici — UniToStudyHub

## Comportamento client

All'avvio e poi ogni 24 ore, mentre l'applicazione resta aperta, UniToStudyHub interroga esclusivamente l'endpoint pubblico GitHub Releases del repository:

`https://api.github.com/repos/red04yes-cyber/Unitracker/releases/latest`

Il controllo:
- non invia dati di studio, identificativi, credenziali o telemetria;
- usa la versione dell'assembly installato e confronta tag stabili `vMAJOR.MINOR.PATCH`;
- ignora release draft, prerelease, tag non validi e risposte oltre 256 KiB;
- usa timeout breve e fallisce in modo silenzioso se la rete o GitHub non sono disponibili;
- mostra una notifica solo quando esiste una versione più recente;
- apre esclusivamente la pagina HTTPS della release ufficiale GitHub dopo conferma dell'utente.

Non installa né sovrascrive file automaticamente. Il pacchetto attuale è portabile: l'utente scarica la nuova ZIP, la estrae in una nuova cartella e avvia il nuovo eseguibile. I dati utente restano in `%LOCALAPPDATA%\UniToStudyHub`.

## Pubblicazione

Il workflow `.github/workflows/publish-release.yml` parte solo quando viene pushato un tag conforme a `vMAJOR.MINOR.PATCH` (eventuale suffisso prerelease). Prima di pubblicare:
1. verifica che il commit del tag sia già contenuto in `main`;
2. esegue la suite automatica;
3. pubblica l'app WPF self-contained per Windows x64;
4. crea ZIP portabile e checksum SHA-256 con lo script esistente;
5. crea una GitHub Release con entrambi gli asset.

Per esempio, dopo aver integrato in `main` una versione che deve essere distribuita come 0.3.0, pubblicare il tag `v0.3.0`. Il workflow non pubblica release da push ordinari o da PR.

## Limiti e comportamento utente

- Una modifica non pubblicata in una GitHub Release non genera notifiche.
- Gli utenti ricevono il controllo al successivo avvio o entro 24 ore se l'app resta aperta e connessa.
- Il controllo automatico richiede accesso HTTPS a GitHub. Nessun dato dell'utente viene inviato.
- L'aggiornamento è assistito: l'utente sceglie quando installare; dopo il clic l'installazione e il riavvio sono automatici.
- Il pacchetto portabile non è firmato digitalmente; il checksum rileva corruzione ma non autentica l'autore. La firma del codice richiede un certificato di code signing gestito dal proprietario del progetto.

## Test

I test del servizio usano un `HttpMessageHandler` fittizio e non richiedono accesso a Internet. Coprono versione nuova, versione uguale/precedente, draft/prerelease, tag malformati, endpoint senza release, risposta troppo grande e cancellazione.
