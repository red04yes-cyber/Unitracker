# Controlli mirati — libreria documenti locale — 17 novembre 2026

Perimetro: nuovo `DocumentLibraryService`, integrazione in `MoodleApplicationService`, test dedicati e workflow già presenti. Sono controlli source-level; non equivalgono a una scansione runtime completa del filesystem di un utente.

| # | Area / rischio | Metodo ed evidenza | Esito |
|---:|---|---|---|
| 1 | Root predefinita / path relativo inatteso | `GetDefaultRootDirectory` normalizza con `Path.GetFullPath` | Superato |
| 2 | Documenti reindirizzata / root errata | Risoluzione del target finale prima di comporre la cartella app | Superato |
| 3 | Root Moodle collegata | `ScanAsync` rifiuta una root reparse/non verificabile | Superato |
| 4 | Junction/symlink in sottocartelle | `IsSafeDirectory` e `LocalFileSecurity.IsInside` | Superato |
| 5 | Symlink file, incluso dangling | `IsReparsePoint` controlla `LinkTarget` e attributi | Superato |
| 6 | File fuori dall'archivio | Contenimento verificato prima della lettura | Superato |
| 7 | Path rooted importato da una voce vecchia | `IsSafeRelativePath` rifiuta percorsi assoluti | Superato |
| 8 | Path traversal | Segmenti `.` e `..` rifiutati; test `TryGetSafePathRejectsTraversalAndReturnsContainedPath` | Superato |
| 9 | Drive/ADS e NUL nel percorso relativo | Rifiuto di `:` e NUL nei segmenti | Superato |
| 10 | Path cambiato dopo la scansione | `TryGetSafePath` ripete risoluzione e verifica; non si fida del percorso memorizzato | Superato |
| 11 | File spostato o cancellato | `RefreshAvailability` produce `Missing`; test dedicato | Superato |
| 12 | Esaurimento risorse per numero di file | Limite di 5.000 file | Superato |
| 13 | Esaurimento risorse per ampiezza dell'albero | Limite di 5.000 cartelle anche sulla coda, non solo sulle visite | Superato |
| 14 | Albero ricorsivo patologico | Profondità massima 16; test dedicato | Superato |
| 15 | Input filtro arbitrariamente grande | Enumerazione limitata a 10.000 elementi; test dedicato | Superato |
| 16 | Hash di singolo file eccessivo | Nessun checksum oltre 250 MiB | Superato |
| 17 | Hash complessivo eccessivo | Budget di lettura di 512 MiB per scansione | Superato |
| 18 | Allocazione dell'intero file in memoria | SHA-256 incrementale su stream da 64 KiB | Superato |
| 19 | Superamento del budget durante una crescita del file | Stream limitato e controllo della lunghezza prima/dopo la lettura; nessun byte di probe oltre il budget | Superato |
| 20 | Download interrotto indicizzato come documento | Estensioni temporanee `.tmp` e `.part` escluse; test dedicato | Superato |
| 21 | Operazione non interrompibile | Token di cancellazione controllato durante scansione e hash; test pre-cancellazione | Superato |
| 22 | Alterazione accidentale dei materiali | Apertura in sola lettura; test verifica che i byte restino invariati | Superato |
| 23 | Dipendenza da token Moodle | Metodo applicativo locale non invoca `RequireToken`; test con token store vuoto | Superato |
| 24 | Esfiltrazione/rete | Il nuovo indice non crea richieste HTTP e non invia dati | Superato |
| 25 | Esecuzione di file non attendibili | Nessun avvio di processi o shell; il servizio restituisce solo metadati/percorso verificato | Superato |
| 26 | Deserializzazione o decompression bomb | Il servizio non deserializza né estrae archivi | Non applicabile |
| 27 | Segreti nei risultati o warning | DTO contiene solo metadati locali e hash; warning generici, senza token o contenuto | Superato |
| 28 | Persistenza parallela e migrazioni | Nessuna scrittura DB/JSON, entity o migration introdotta | Non applicabile |
| 29 | TLS/redirect Moodle | La scansione è locale e non effettua chiamate Moodle | Non applicabile |
| 30 | Dipendenze e supply chain | Nessun nuovo package; workflow esistente mantiene checkout senza credenziali persistenti, pin immutabili, Gitleaks redatto e audit NuGet bloccante | Superato |

## Problemi e correzioni

- Problemi concreti rilevati durante la revisione: nessuna vulnerabilità confermata.
- Hardening incluso: limite anche sulle cartelle accodate (non soltanto su quelle già visitate) e contabilizzazione stretta dei byte effettivamente letti per checksum.
- Correzioni di vulnerabilità pregresse: nessuna.
- Nuove dipendenze, endpoint, permessi o schema dati: nessuno.

## Rischi residui / non verificabili

- La scansione ricontrolla reparse point e contenimento prima di leggere e prima di restituire un percorso, ma le API filesystem usate non offrono qui un'apertura atomica garantita “no-follow” contro un processo locale ostile che sostituisca un path esattamente tra controllo e apertura. Non viene dichiarata protezione assoluta da questa race locale.
- ACL effettive, reparse point e disponibilità dei file sulla macchina dell'utente non sono verificabili dal controllo source-level.
- I test sono aggiunti ma, prima della CI Windows, non sono dichiarati eseguiti.
