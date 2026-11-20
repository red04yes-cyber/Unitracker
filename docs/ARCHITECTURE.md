# Architettura tecnica

> Baseline documentata sul branch `main` al 20 novembre 2026. Questo documento descrive il codice presente: non implica che le PR aperte siano già integrate.

## Panoramica

UniTo StudyHub è un'applicazione desktop WPF per Windows, basata su .NET 10, organizzata in livelli pratici di presentazione, servizi applicativi, dominio e infrastruttura. I dati accademici e di studio sono locali; Moodle è un'integrazione facoltativa configurata dall'utente.

Le decisioni architetturali correlate sono raccolte in [docs/adr](adr/README.md).

## Avvio e composizione

Il punto d'ingresso è `App.OnStartup`:

1. inizializza il tema e la directory dati;
2. costruisce il contenitore DI registrando separatamente servizi core e ViewModel;
3. applica le migrazioni EF Core al database;
4. esegue l'import una tantum del precedente `data.json`, se presente e non ancora importato;
5. inizializza `MainViewModel` e mostra `MainWindow`.

I servizi core sono registrati da `Infrastructure/CompositionRoot.cs`; i ViewModel da `Presentation/Composition/PresentationServiceRegistration.cs`. La UI WPF usa `MainViewModel` e `StudyWorkspaceViewModel` come adapter di presentazione. I comandi asincroni sono esposti tramite `AsyncRelayCommand`.

## Responsabilità dei livelli

| Area | Responsabilità | Riferimenti principali |
|---|---|---|
| `Domain` | Entità, value object, enum e regole accademiche indipendenti dalla UI | `Domain/Entities`, `Domain/Services`, `Domain/ValueObjects` |
| `Services` | Casi d'uso e orchestrazione: carriera, calendario, planner, Study Lab, Moodle, backup, import/export | `Services/` |
| `Infrastructure` | Composizione DI, EF Core, SQLite, mapping e migrazioni | `Infrastructure/CompositionRoot.cs`, `Infrastructure/Data/` |
| `Presentation` | Binding, stato di schermata e comandi WPF; non è la sede delle regole di dominio | `Presentation/ViewModels/`, `Presentation/Commands/` |
| `Security` | Protezione segreti, policy endpoint, redazione diagnostica e hardening filesystem | `Security/` |

Non esiste un livello `Application` separato: i casi d'uso applicativi risiedono attualmente in `Services`. Evitare di introdurre un secondo contenitore di persistenza o di spostare regole di dominio nei ViewModel.

## Flusso dei dati locali

Il percorso principale è:

```text
WPF View
  -> MainViewModel / StudyWorkspaceViewModel
  -> service applicativo o di dominio
  -> IStudyDataStore
  -> SqliteStudyDataStore
  -> StudyHubDbContext + StudyDataEntityMapper
  -> database SQLite cifrato
```

`StudyData` è il DTO aggregato usato dai servizi e dalla presentazione. `IStudyDataStore` è l'astrazione di lettura/scrittura; l'implementazione corrente è `SqliteStudyDataStore`. Il mapper traduce l'aggregato nelle entità EF Core.

**Semantica di salvataggio importante:** `SqliteStudyDataStore.SaveAsync` normalizza e valida l'aggregato prima di toccare il database, poi sostituisce i record in una transazione. La sostituzione è atomica, ma non è un aggiornamento parziale: i chiamanti devono fornire uno snapshot completo e coerente. Non salvare una collezione parziale come se fosse l'intero archivio.

Le migrazioni dello schema sono incrementali in `Infrastructure/Data/Migrations`. Il vecchio JSON è una sorgente di migrazione, non un secondo database attivo: `LegacyJsonMigrator` crea una copia di recupero e registra un marker transazionale prima di considerare concluso l'import. Il file sorgente resta preservato.

## Persistenza, cifratura e directory

- La directory predefinita è `%LOCALAPPDATA%\\UniToStudyHub`; `UNITRACKER_DATA_DIR` consente di isolare i dati nei test e nello smoke test.
- Il database usa il codec SQLCipher compatibile con il formato v4 tramite il provider SQLite distribuito dall'app.
- `DatabaseKeyVault` genera una chiave casuale di 256 bit e conserva l'envelope protetto da DPAPI per l'account Windows corrente. Se l'envelope esistente non è leggibile, il servizio fallisce in modo chiuso: non genera una nuova chiave che renderebbe il database esistente illeggibile.
- La conversione del vecchio SQLite in chiaro verifica il file cifrato prima della sostituzione.
- Le credenziali Moodle sono conservate separatamente dal DTO accademico tramite il vault protetto; non sono dati di carriera.
- L'hardening ACL della directory è best effort: `EnsurePrivateDirectory` non propaga il fallimento di `TryRestrictDirectory`. Su filesystem senza ACL affidabili la protezione della directory è quindi una limitazione; usare storage locale con ACL per utente e verificarne l'esito su Windows.

I dettagli decisionali e le conseguenze operative sono in [ADR-0002](adr/0002-persistenza-locale-sqlcipher.md).

## Integrazione Moodle

Moodle è opzionale. `MoodleApplicationService` richiede un token disponibile prima di costruire il client; `MoodleClient`:

- valida l'endpoint tramite `OutboundEndpointPolicy`;
- richiede HTTPS per endpoint remoti e non segue redirect;
- invia il token nel corpo dei POST REST;
- applica timeout e limiti alle risposte e ai download;
- accetta download solo dalla stessa origine Moodle e li scrive tramite file temporaneo prima della pubblicazione.

**Eccezione di protocollo:** l'endpoint Moodle per il download dei file richiede `token=` nella query del singolo GET. Questa eccezione resta circoscritta al download: l'URL tokenizzato non deve essere registrato o riutilizzato in altri endpoint. Il vault usa DPAPI per l'account Windows corrente.

Vedi [ADR-0003](adr/0003-moodle-token-e-rete.md).

## Provenienza delle date di calendario

`CalendarEvent` conserva separatamente `Source` e `Verification`. Le date collegate dal planner agli esami sono create con sorgente `Planner` e verifica `Indicative`; non sono appelli ufficiali. La presenza del valore `Official` nell'enum non rende ufficiale un evento: occorre una fonte effettivamente verificata.

Vedi [ADR-0004](adr/0004-provenienza-date-esami.md).

## Regole per le modifiche

1. Mantenere una sola fonte di verità per i dati di studio: `IStudyDataStore` e il database SQLite.
2. Aggiungere migrazioni EF Core incrementali per ogni modifica dello schema; preservare i dati esistenti.
3. Validare gli aggregati prima delle scritture e mantenere atomiche le operazioni multi-record.
4. Tenere I/O e rete asincroni, cancellabili dove previsto e fuori dal thread UI.
5. Non registrare token, chiavi, note private o URL Moodle tokenizzati.
6. Trattare file importati e risposte Moodle come input non attendibili.
7. Non presentare date indicative, dati mancanti o sincronizzazioni incomplete come informazioni ufficiali o verificate.

## Documentazione correlata

- [Decision records](adr/README.md)
- [Privacy operations](security/PRIVACY-OPERATIONS.md)
- [Security audit](security/SECURITY-AUDIT.md)
- [Testing checklist](../TESTING.md)
