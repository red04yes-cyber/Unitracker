# Importazione ed esportazione dati personali

## Formato

L'esportazione completa di PrivacyService usa un envelope JSON con:
- `schemaVersion`: versione del formato;
- `generatedAt`: istante UTC dell'esportazione;
- `inventory`: riepilogo leggibile;
- `data`: snapshot StudyData.

Le credenziali Moodle e le chiavi AI sono conservate in vault separati e non fanno parte di StudyData. Non vengono mai esportate o importate. I file PDF non sono incorporati: vengono trasferiti solo i relativi metadati e percorsi.

## Flusso sicuro

1. Leggere il file come bytes e chiamare `StudyDataImportService.PreviewAsync`.
2. Mostrare conteggi, conflitti per identificativo e avvisi senza modificare SQLite.
3. Chiedere all'utente di scegliere esplicitamente Merge oppure Replace.
4. Chiamare `ApplyAsync` solo dopo conferma.

Il file è limitato a 64 MB e 100.000 record. La versione schema deve essere supportata. La validazione riusa `StudyDataEntityMapper.NormalizeAndValidate`, che verifica identificativi, riferimenti e invarianti del dominio prima di scrivere.

### Merge

- Aggiunge solo record con identificativi non presenti.
- In caso di conflitto, il record locale prevale e non viene sovrascritto.
- Impostazioni profilo, URL Moodle e stato del timer locale restano quelli del dispositivo.
- Le credenziali non vengono mai importate.

### Replace

- Sostituisce l'intero dataset importabile.
- È rifiutato se BackupService non è configurato.
- Prima di salvare crea un backup di sicurezza verificato, cifrato con AES-256-GCM e protetto da DPAPI o passphrase.
- Se il salvataggio non riesce, la transazione SQLite mantiene i dati precedenti; il backup di sicurezza resta disponibile.

## Limiti

- Un import non trasferisce i file Moodle o i PDF fisici; trasferisce solo i metadati.
- Merge è additivo e non aggiorna né cancella record locali in conflitto.
- Le chiavi DPAPI sono legate all'account Windows; per trasferire un backup cifrato usare una passphrase scelta dall'utente.
- L'anteprima è una validazione locale, non una garanzia che un file esterno sia autentico. Il checksum del payload identifica il contenuto esaminato e consente alla UI di collegare la conferma alla stessa anteprima.
