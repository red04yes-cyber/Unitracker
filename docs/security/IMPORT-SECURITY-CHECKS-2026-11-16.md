# Audit mirato import dati — 2026-11-16

## Perimetro

Verificati `StudyDataImportService`, `StudyDataEntityMapper.NormalizeAndValidate`,
i modelli annidati di carriera/Studio Lab, i test di import e il contratto
documentato in `DATA-IMPORT-EXPORT.md`.

Sono stati eseguiti 30 controlli mirati. "OK" indica che il controllo era già
presente e verificabile; "corretto" indica una modifica effettiva in questa
branch. Nessuna vulnerabilità è stata aggiunta per raggiungere il numero.

## 30 controlli

| # | Controllo | Esito |
|---:|---|---|
| 1 | Rifiuto di payload vuoti | OK |
| 2 | Limite dimensionale di 64 MiB prima del parsing | OK |
| 3 | Profondità JSON massima pari a 64 | OK |
| 4 | JSON malformato convertito in errore di import controllato | OK |
| 5 | Envelope radice obbligatoriamente oggetto | OK |
| 6 | Presenza e tipo stringa di `schemaVersion` | OK |
| 7 | Rifiuto di versioni schema non supportate | OK |
| 8 | `data` obbligatoriamente oggetto | OK |
| 9 | Limite globale sugli elementi degli array, inclusi quelli annidati, prima di DOM e deserializzazione | **Corretto** |
| 10 | Cancellazione richiesta prima della scansione e durante scansioni lunghe | **Corretto** |
| 11 | Conteggio record dopo normalizzazione come difesa aggiuntiva | OK |
| 12 | Rifiuto di elementi nulli nelle collezioni principali | OK |
| 13 | Identificativi univoci per le principali entità importate | OK |
| 14 | Un solo esame di carriera per corso | OK |
| 15 | Identificativi dei tentativi d'esame univoci globalmente | OK |
| 16 | Riferimenti dei voti a corsi esistenti e voti nell'intervallo ammesso | OK |
| 17 | Riferimenti degli esami pianificati e CFU/ore non negativi | OK |
| 18 | Entrambi gli estremi di ogni propedeuticità devono esistere | OK |
| 19 | Rifiuto di archi di propedeuticità duplicati | OK |
| 20 | Rilevamento dei cicli nel grafo delle propedeuticità | OK |
| 21 | Identificativi dei segmenti di sessione univoci globalmente | OK |
| 22 | Timestamp dei segmenti validi e sequenza senza sovrapposizioni | OK |
| 23 | Coerenza tra stato Running/Paused e segmento aperto | OK |
| 24 | Massimo 200 passi per missione | OK |
| 25 | Identificativi e titoli dei passi, timestamp dei passi completati | OK |
| 26 | Validazione dei collegamenti delle attività e unicità del collegamento sessione-attività | OK |
| 27 | Intervalli, durata accreditata e fuso orario delle attività | OK |
| 28 | Limiti di note, risultato, tag e valutazioni delle attività | OK |
| 29 | Metadati PDF, hash duplicati e corrispondenza tra numero e numerazione delle pagine | OK |
| 30 | Replace subordinato a BackupService e backup prima della sostituzione | OK |

## Problema concreto e correzione

Prima della modifica, il limite di 100.000 record era verificato soltanto dopo
`JsonDocument.Parse` e la deserializzazione in `StudyData`. Il conteggio
considerava inoltre solo le collezioni di primo livello. Un payload entro i
64 MiB poteva quindi concentrare un numero molto elevato di oggetti in una
singola collezione annidata (per esempio i segmenti di una sessione), causando
un'amplificazione della memoria durante la creazione del DOM e del grafo di
oggetti.

La branch aggiunge una scansione preliminare con `Utf8JsonReader` che conta
tutti gli elementi di tutti gli array, anche annidati, e interrompe l'import
oltre 100.000 prima della creazione del DOM. La scansione osserva il token di
cancellazione prima di iniziare e ogni 1.024 token. Il limite dimensionale di
64 MiB e quello di profondità 64 restano attivi.

## Altri controlli di comportamento

- L'anteprima carica i dati locali ma non invoca `SaveAsync`.
- Merge conserva i campi dei record padre locali in conflitto.
- Le impostazioni locali, l'endpoint Moodle e il timer non vengono sostituiti da Merge.
- Token Moodle e chiavi AI sono custoditi fuori da `StudyData` e non entrano nel formato importabile.
- Il checksum SHA-256 è calcolato sui byte esaminati e conservato nell'oggetto preview.
- Replace richiede un backup verificato prima della scrittura; il salvataggio SQLite usa una transazione.

## Verifiche aggiunte in questa branch

- Payload con oltre 100.000 elementi in una collezione annidata rifiutato prima
  di chiamare `IStudyDataStore.LoadAsync`.
- Token di cancellazione già annullato rispettato prima della scansione e del
  caricamento del database.
- Merge additivo dei nuovi tentativi, segmenti e passi per record padre già
  esistenti; i campi locali del padre restano invariati.
- Segmenti riordinati cronologicamente dopo l'unione.
- Seconda applicazione dello stesso preview idempotente: nessun record figlio
  aggiunto una seconda volta.

La compilazione e l'esecuzione effettiva dei test sono demandate al workflow
Windows/.NET 10 della Pull Request; questo audit non sostituisce la CI.
