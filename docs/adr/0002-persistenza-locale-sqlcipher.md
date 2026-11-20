# ADR-0002 — Persistenza locale SQLCipher

- **Data:** 2026-11-20
- **Stato:** Accepted — documenta la baseline già implementata su `main`
- **Ambito:** archivio applicativo, schema e protezione della chiave

## Contesto

Corsi, voti, esami, calendario e dati Study Lab devono condividere una fonte di verità locale. Un secondo archivio JSON attivo creerebbe divergenze e renderebbe non atomiche le operazioni tra moduli. Il database contiene dati personali dello studente e deve essere protetto a riposo.

## Decisione

- `IStudyDataStore` è il contratto di accesso all'aggregato `StudyData`; l'implementazione corrente è `SqliteStudyDataStore`.
- EF Core e `StudyHubDbContext` persistono le entità nel database SQLite cifrato con il codec SQLCipher compatibile con il formato v4.
- `DatabaseKeyVault` genera una chiave casuale da 256 bit e conserva soltanto l'envelope protetto da DPAPI per l'account Windows corrente.
- Un vault esistente non leggibile è un errore bloccante: non si sostituisce automaticamente la chiave.
- Le modifiche allo schema usano migrazioni EF Core incrementali.
- Il precedente `data.json` è gestito da `LegacyJsonMigrator` come sorgente di import una tantum, con copia di recupero e marker; non è una seconda fonte di verità.
- `UNITRACKER_DATA_DIR` consente di isolare directory dati nei test e negli smoke test.

## Alternative considerate

- **JSON come archivio attivo:** rifiutato perché non offre lo stesso modello relazionale e transazionale tra i moduli.
- **SQLite in chiaro come formato corrente:** rifiutato per la protezione dei dati locali.
- **Generare una nuova chiave quando DPAPI non riesce a leggere quella esistente:** rifiutato perché renderebbe il database preesistente illeggibile.
- **Database remoto obbligatorio:** rifiutato perché l'app deve restare utilizzabile localmente senza sincronizzazione cloud.

## Conseguenze

- La chiave DPAPI è legata all'account Windows che l'ha protetta; il trasferimento del solo file DB non basta a trasferire l'accesso.
- Backup e procedure di ripristino restano necessari per migrazione e recupero; non vanno confusi con una sincronizzazione multi-dispositivo.
- Ogni nuova relazione persistente richiede entità, mapping, migrazione e test su database nuovo ed esistente.
- `SaveAsync` riceve uno snapshot completo: valida prima della scrittura e sostituisce il contenuto in una transazione. Non è un'API di patch parziale.

## Evidenze

- `src/UniToStudyHub/Services/SqliteStudyDataStore.cs`
- `src/UniToStudyHub/Infrastructure/Data/StudyHubDbContext.cs`
- `src/UniToStudyHub/Infrastructure/Data/StudyDataEntityMapper.cs`
- `src/UniToStudyHub/Infrastructure/Data/StudyHubDbContextFactory.cs`
- `src/UniToStudyHub/Infrastructure/Data/DatabaseKeyVault.cs`
- `src/UniToStudyHub/Services/LegacyJsonMigrator.cs`
- `src/UniToStudyHub/Infrastructure/Data/Migrations/`
