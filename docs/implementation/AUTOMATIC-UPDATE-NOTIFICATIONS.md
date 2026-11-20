# Aggiornamenti automatici — UniToStudyHub

## Comportamento client

All'avvio e poi ogni 24 ore, mentre l'applicazione resta aperta, UniToStudyHub interroga esclusivamente l'endpoint pubblico GitHub Releases del repository:

`https://api.github.com/repos/red04yes-cyber/Unitracker/releases/latest`

Il controllo:
- non invia dati di studio, identificativi, credenziali o telemetria;
- usa la versione dell'assembly installato e confronta tag stabili `vMAJOR.MINOR.PATCH`;
- ignora release draft, prerelease, tag non validi e risposte oltre 256 KiB;
- usa timeout breve e fallisce in modo silenzioso se la rete o GitHub non sono disponibili;
- mostra una finestra con i pulsanti **Installa ora** e **Più tardi**;
- con **Installa ora**, scarica la ZIP ufficiale e il checksum SHA-256, verifica il digest e controlla i percorsi contenuti nell'archivio;
- avvia un processo separato, attende la chiusura dell'app, sostituisce i file nella cartella corrente e riavvia l'eseguibile;
- conserva una copia temporanea della versione precedente e tenta il ripristino se la sostituzione fallisce;
- costruisce gli URL degli asset usando il tag validato e l'origine HTTPS GitHub fissata nel codice.

L'aggiornamento sostituisce soltanto i binari nella cartella dell'applicazione. I dati utente restano in `%LOCALAPPDATA%\\UniToStudyHub` e non fanno parte del pacchetto. Windows può mostrare una richiesta UAC per autorizzare la sostituzione. Se il download, la verifica o l'installazione falliscono, l'app mostra un errore e il processo di aggiornamento tenta il rollback.

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
