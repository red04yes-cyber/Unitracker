# Limiti di ingresso per backup e ripristino

## Confini di dimensione

Il servizio applica gli stessi limiti in creazione, verifica e ripristino:

- payload SQLite: massimo 512 MiB;
- container `.utbk`: massimo payload più l'header, la chiave avvolta e il tag GCM (calcolato da `MaximumContainerBytes`);
- manifest JSON laterale: massimo 16 KiB.

I file vuoti, i formati con estensione non supportata e i contenuti che superano il limite vengono rifiutati prima di poter sostituire il database attivo.

## Lettura e copia limitate

- I container cifrati e i manifest sono letti da stream con limite applicato alla lunghezza effettiva, non solo a una precedente `FileInfo.Length`.
- La lettura controlla EOF anticipato e un eventuale byte aggiuntivo; la copia legacy confronta la quantità trasferita con la lunghezza iniziale.
- Gli handle sorgente condividono la lettura ma non nuove scritture su Windows; i controlli di lunghezza durante il trasferimento coprono comunque file che cambiano.
- I buffer temporanei usati per copie e hash vengono azzerati.
- Le directory di backup, staging e verifica e i file temporanei plaintext vengono controllati con ACL restrictive; se la restrizione non riesce, creazione/verifica/ripristino si interrompono invece di continuare con una copia leggibile.
- La lista backup non calcola hash su file fuori dai limiti: tali file possono essere mostrati per consentire all'utente di individuarli, ma non ricevono un hash calcolato dal servizio.

## Verifica dei backup legacy

I backup v1 `.db` senza manifest restano supportati e vengono verificati tramite SQLite. Se esiste un manifest, il servizio controlla anche `Sha256` e `PayloadLength` sul file temporaneo copiato in modo limitato. Il ripristino non procede se uno dei valori non coincide.

I container `.utbk` continuano a richiedere autenticazione AES-GCM; se il manifest contiene hash o lunghezza del payload, entrambi devono corrispondere.

## Verifiche automatiche

I test coprono lettura/copia entro e oltre un limite, manifest sovradimensionato, checksum legacy errato, lunghezza legacy errata e rifiuto di estensioni non supportate. I test di round-trip, password errata, container alterato, formato futuro e backup legacy valido restano invariati.

Non viene introdotta una nuova dipendenza, una migrazione o una modifica alla UI. Il limite di 512 MiB è quello già applicato alla creazione dei backup; la modifica lo rende coerente anche per input e ripristino.
