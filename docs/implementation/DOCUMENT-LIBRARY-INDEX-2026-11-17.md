# Libreria documenti locale — 17 novembre 2026

## Ambito

Questa slice aggiunge un indice locale, derivato e di sola lettura per i file già scaricati da Moodle in `Documenti/UniToStudyHub/Moodle`. Non crea un secondo archivio persistente e non richiede una connessione Moodle.

## Comportamento implementato

- `MoodleApplicationService.GetDownloadedDocumentsAsync` espone la scansione locale anche quando non è configurato un token.
- `DocumentLibraryService.ScanAsync` attraversa l'archivio in modo asincrono rispetto al thread chiamante e restituisce nome corso (prima cartella), categoria, nome file, estensione, dimensione, ultima modifica UTC, percorso relativo, SHA-256 e stato.
- `Filter` combina testo, corso, categoria, estensione, stato e intervallo temporale; l'input è limitato.
- `RefreshAvailability` aggiorna lo stato di una voce già visualizzata dopo spostamenti o cancellazioni.
- `TryGetSafePath` restituisce un percorso solo dopo una nuova verifica di contenimento, reparse point ed esistenza. Il chiamante deve comunque chiedere conferma prima di aprire un file con un'applicazione esterna.
- I file temporanei `.tmp` e `.part` non vengono mostrati.

## Limiti intenzionali

| Limite | Valore |
|---|---:|
| File enumerati per scansione | 5.000 |
| Cartelle visitate o accodate | 5.000 |
| Profondità | 16 livelli |
| Dimensione massima per checksum | 250 MiB |
| Byte massimi letti per checksum in una scansione | 512 MiB |
| Elementi esaminati da un filtro | 10.000 |

I file più grandi del limite per checksum restano elencabili ma hanno checksum non calcolato. Se il budget complessivo è esaurito, la scansione continua a raccogliere metadati senza leggere altri contenuti. La scansione può essere annullata.

Il checksum SHA-256 identifica i byte osservati e aiuta a rilevare cambiamenti accidentali; non prova autenticità, provenienza Moodle o affidabilità del documento.

## Contratto per il binding WPF

La UI può usare il servizio già esposto da `MoodleApplicationService`:

- caricamento/refresh: `await Moodle.GetDownloadedDocumentsAsync(cancellationToken: token)`;
- lista: `DocumentLibraryScanResult.Entries`;
- filtri: `DocumentLibraryService.Filter(entries, filter)`;
- aggiornamento stato di una selezione: `DocumentLibraryService.RefreshAvailability(root, entry)`;
- apertura: chiamare `TryGetSafePath` immediatamente prima dell'azione e usare solo il percorso restituito.

Gli stati `Missing`, `Inaccessible` e `UnsafePath` devono essere mostrati come tali, senza trasformarli in un successo. I messaggi di scansione sono generici e non riportano percorsi locali.

## Persistenza e UI

L'indice è ricostruito on-demand dal filesystem: nessuna tabella, migration, file JSON laterale o modifica ai file scaricati. Questa scelta evita duplicare la fonte di verità e permette di usare la libreria offline.

Non sono stati modificati XAML, code-behind o `StudyWorkspaceViewModel`: `AGENTS.md` assegna la UI a un workstream separato e PR aperte stanno modificando i viewmodel. Di conseguenza questa PR consegna il servizio e il contratto tecnico, ma **non dichiara completato il collegamento visibile della libreria nella schermata WPF**.

## Verifiche automatiche aggiunte

I test coprono metadati e checksum, non-mutazione del file, filtri combinati e limite di enumerazione, esclusione dei file temporanei, file mancanti, rifiuto di path traversal, accesso offline senza token, archivio assente, cancellazione e limite di profondità.
