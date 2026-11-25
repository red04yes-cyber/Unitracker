# Troubleshooting

Questa pagina raccoglie verifiche non distruttive per i problemi più comuni. Non eliminare file database, chiavi, backup o directory di configurazione per tentare di risolvere un errore.

## L'app non si avvia

1. Se usi il sorgente, verifica che il .NET 10 SDK sia installato e che restore/build terminino senza errori.
2. Se usi il pacchetto portabile, verifica che l'archivio provenga da una fonte affidabile e che il checksum SHA-256 corrisponda al file pubblicato.
3. Se Windows segnala un blocco, controlla la provenienza del pacchetto e le impostazioni di sicurezza del dispositivo; il pacchetto portabile non è firmato digitalmente.
4. Se l'app si chiude all'avvio, conserva una copia dei log locali per diagnosi, ma redigi dati personali, URL e token prima di condividerli.

## Database o migrazione non riusciti

- Il database normalmente si trova in `%LOCALAPPDATA%\UniToStudyHub\unitracker.db`; la directory può essere reindirizzata per test tramite `UNITRACKER_DATA_DIR`.
- Il database è cifrato con SQLCipher e la chiave è protetta da DPAPI per l'account Windows che lo ha creato.
- Se un database legacy viene convertito, l'app verifica il database cifrato prima di sostituire il file originale.
- Se l'apertura o la migrazione fallisce, **non cancellare il database, la directory `secrets` o la chiave** e non creare un database vuoto sopra quello esistente. Chiudi l'app, conserva i file e recupera da un backup verificato solo dopo aver capito la causa.

Una reinstallazione o il trasferimento a un altro account Windows non trasferisce automaticamente la chiave DPAPI.

## Moodle non si connette o la sincronizzazione è incompleta

Controlla, senza condividere il token:

- L'URL è la radice HTTPS dell'istanza Moodle corretta; non include credenziali, query o frammenti.
- Web Services REST sono abilitati e il token è attivo, associato all'utente giusto e autorizzato alle funzioni necessarie.
- L'istanza è raggiungibile e il certificato HTTPS è valido.
- Il token non è scaduto/revocato. Premi **Verifica e salva token** per ripetere il controllo di connessione.
- Per i dati mancanti, verifica con l'amministratore Moodle le capability e le funzioni Web Services. StudyHub non aggira ruoli o controlli e non può recuperare dati non esposti al token.
- Le richieste hanno un timeout di 45 secondi: rete lenta o server non disponibile possono interrompere l'operazione.

Se vuoi interrompere l'accesso, **Disconnetti** elimina il token e i riferimenti di collegamento, ma non cancella corsi, voti o lo storico locale.

## Un allegato Moodle non viene scaricato

I download sono limitati alla stessa origine Moodle e sono soggetti a limiti di dimensione/numero e a controlli di sicurezza. Sono rifiutati tipi di file eseguibili e macro potenzialmente pericolosi. Un file oltre i limiti, un URL esterno, un permesso mancante o un link non più valido può non essere scaricato.

Non aggirare il blocco rinominando un eseguibile o scaricandolo da un link non verificato. Se il materiale è necessario, chiedi al docente o all'amministratore un formato sicuro o una fonte autorizzata.

## Backup o ripristino non riuscito

- Verifica che il file backup sia completo e che sia disponibile la password, se richiesta.
- I backup sono cifrati e verificati; un file corrotto o una password errata non deve essere usato per sostituire il database attivo.
- Il ripristino crea prima una copia di sicurezza dei dati correnti e sostituisce il database solo dopo le verifiche.
- I backup creati con protezione DPAPI sono legati all'account Windows. Anche un backup protetto da password contiene il database cifrato con una chiave DPAPI: il ripristino richiede l'account Windows originale.
- Non modificare manualmente il contenitore o i manifest. Se la verifica fallisce, conserva il backup originale e prova una copia, non sovrascrivere l'unico backup.

Per i dettagli del formato e dei limiti consulta [Privacy e protezione dei dati](security/PRIVACY.md).

## Import/export o dati mancanti

- L'esportazione JSON completa contiene note e dati personali; non contiene token o chiavi API. Proteggi il file e cancellane le copie non necessarie.
- Prima di importare, controlla l'anteprima e la modalità selezionata; usa il merge se vuoi mantenere i dati esistenti.
- Il report XLSX di progressi è un report compatto e non equivale al backup ripristinabile.
- Se un import è fallito, conserva il file originale e l'errore redatto; non riprovare con il replace senza un backup verificato.

## Diagnostica e privacy

Il pacchetto diagnostico richiede consenso esplicito e non include il database personale, credenziali o documenti. Può includere log locali redatti. Controlla l'anteprima e il contenuto prima di inviarlo a qualcuno; non caricarlo automaticamente su servizi esterni.

La cancellazione completa dei dati richiede conferma. Puoi scegliere se eliminare anche i backup cifrati; senza quella selezione, i backup restano. L'operazione è irreversibile: esporta prima ciò che vuoi conservare e verifica il file esportato.

