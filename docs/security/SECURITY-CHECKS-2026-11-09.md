# Security checks — 9 novembre 2026

## Perimetro e metodo

Revisione mirata del branch `main` del 9 novembre 2026 su vault, endpoint, Moodle, download, PDF, import/export, backup, file locali, diagnostica e workflow. I controlli sotto sono verifiche statiche dei contratti e dei relativi test presenti; non sono un penetration test né sostituiscono le verifiche runtime su Windows.

## Controlli eseguiti

1. **Nomi del vault e traversal** — OK. `ProtectedSecretStore.IsValidName` limita lunghezza e caratteri e rifiuta separatori e sequenze `..`.
2. **Dimensione del vault** — OK. L'envelope ha un limite esplicito; letture vuote o oltre il limite sono trattate come corrotte.
3. **Protezione dei segreti** — OK nel percorso Windows: il vault usa l'astrazione del protector DPAPI, non testo in chiaro.
4. **Separazione dei segreti** — OK. L'entropia è legata al nome del segreto, quindi un blob copiato sotto un altro nome non viene accettato.
5. **Azzeramento dei buffer** — OK. I buffer byte contenenti segreti vengono azzerati nei blocchi `finally`.
6. **Vault corrotto** — OK. Un blob mancante, corrotto o appartenente a un altro protector non produce un fallback a credenziali precedenti.
7. **URL in uscita** — OK. La policy rifiuta credenziali, query e frammenti nell'URL base.
8. **SSRF verso reti riservate** — OK nella policy condivisa: sono bloccati loopback, link-local, reti private, CGNAT, multicast e forme IP non canoniche considerate.
9. **Loopback Moodle predefinito** — RISCHIO GIÀ TRATTATO DA PR APERTA. Su `main`, il costruttore Moodle abilita ancora il parametro loopback; la PR #61 propone il default-deny. Non duplicato in questo branch.
10. **Redirect HTTP** — OK. Il client Moodle creato dall'app disabilita l'auto-redirect.
11. **Trasporto del token** — OK con distinzione di protocollo. Le chiamate API inviano il token nel corpo POST; i download Moodle usano il parametro richiesto dal protocollo solo su URL same-origin. Il token non viene scritto nei log.
12. **Risposte API Moodle** — OK. Il corpo JSON è letto con limite di 8 MB.
13. **Origine dei download** — OK. I download richiedono stesso schema, host e porta dell'istanza Moodle.
14. **Dimensione dei download** — OK. Sono controllati sia Content-Length sia i byte effettivamente trasferiti, con limite di 250 MB.
15. **Risposte HTML al posto dei file** — OK. Una pagina HTML viene rifiutata.
16. **Estensioni pericolose** — OK. La sincronizzazione blocca eseguibili, script e documenti Office con macro.
17. **Nomi remoti** — OK. `SafeName` riduce il nome al segmento finale, rimuove caratteri di controllo e gestisce i nomi dispositivo Windows.
18. **Numero di file per corso** — OK. La sincronizzazione è limitata a 1.000 file.
19. **Volume totale per corso** — OK. Il download si arresta al limite aggregato di 1 GB.
20. **Contenimento filesystem e reparse point** — CORRETTO IN QUESTO BRANCH. Il controllo precedente era solo lessicale e non rilevava link simbolici/junction. Ora `IsInside` rifiuta reparse point sul confine e nei componenti esistenti; il download viene validato rispetto alla radice Moodle, prima e dopo la creazione delle cartelle.
21. **Dimensione PDF** — OK. L'importer rifiuta file oltre 25 MB e ricontrolla la dimensione sullo stream aperto.
22. **Tipo e integrità PDF** — OK. Sono richieste estensione e firma `%PDF-`; hash e parsing derivano dagli stessi byte letti una sola volta.
23. **Stream PDF non seekable** — CORRETTO IN QUESTO BRANCH. L'estrattore legge al massimo 25 MB più un byte di rilevamento prima di rifiutare, senza copiare uno stream arbitrariamente grande in memoria.
24. **Numero pagine PDF** — OK. Il limite di 5.000 pagine è applicato anche dall'importer, non solo dall'estrattore predefinito.
25. **Testo estratto PDF** — OK. Limiti effettivi di 200.000 caratteri per pagina e 4.000.000 complessivi; l'ultima pagina viene troncata al budget residuo.
26. **Dimensione import JSON** — OK. L'import completo rifiuta payload vuoti o oltre 64 MB.
27. **Profondità e schema JSON** — OK. Profondità massima 64 e controllo esplicito di versione, oggetto radice e proprietà `data`.
28. **Record importati** — OK. Massimo 100.000 record e normalizzazione/validazione prima della scrittura.
29. **Strategie import** — OK. Merge conserva i record locali in conflitto; Replace richiede un servizio backup configurato e crea un backup di sicurezza.
30. **CSV formula injection** — OK. I campi testuali che iniziano con `=`, `+`, `-` o `@` dopo spazi/controlli vengono neutralizzati.
31. **XLSX e XML** — OK. I valori testuali sono inline string, non formule; il contenuto è escapato XML.
32. **Note private negli export** — OK. Sono escluse per default e incluse solo con opzione esplicita.
33. **Cifratura backup** — OK. Il formato corrente usa AES-256-GCM con chiave casuale per backup.
34. **Derivazione password backup** — OK. PBKDF2-HMAC-SHA256 usa 600.000 iterazioni di default, con minimo e massimo convalidati in lettura.
35. **Limiti e verifica backup** — OK. Payload limitato, manifest/hash controllati e database verificato con `PRAGMA integrity_check`.
36. **Ripristino** — OK. Il backup viene autenticato e verificato su file temporaneo e viene creato un safety backup prima della sostituzione del database.
37. **ACL e scritture atomiche** — OK come hardening best-effort: DACL per l'utente su Windows, modalità 0700/0600 su Unix, file temporanei e rename atomico.
38. **Redazione log** — OK. Redazione di token/password/Bearer/chiavi/percorsi, timeout delle regex e rimozione dei caratteri di controllo.
39. **Rotazione log** — OK. I log locali sono limitati a 512 KB per file con due rotazioni conservate.
40. **Workflow e permessi CI** — OK per il workflow security: permessi `contents: read`, checkout senza credenziali persistite, Gitleaks con output redatto e audit NuGet bloccante. L'allowlist Gitleaks ampia su `main` è oggetto della PR #61 e non è duplicata qui.

## Correzione applicata

Il problema concreto era un bypass del contenimento dei percorsi tramite filesystem link. La verifica precedente confrontava solo stringhe normalizzate: un percorso apparentemente figlio della cartella corso poteva attraversare una junction o un link simbolico verso un'altra directory.

- `LocalFileSecurity.IsInside` ora controlla il confine e ogni componente esistente, inclusi i link dangling, e fallisce chiuso se non può verificare il percorso.
- `DocumentSyncService` risolve prima l'eventuale reindirizzamento legittimo della cartella Documenti scelta dall'utente (ad esempio OneDrive), poi verifica le cartelle applicative prima di applicare ACL o creare directory, mantiene la radice di sicurezza a livello `Moodle` (non della singola cartella corso) e ripete il controllo prima del download.
- I 10 nuovi casi automatici coprono link di directory/file, radice simbolica, link dangling, pulizia dei temporanei, percorso annidato legittimo, rifiuto del confine stesso, stream PDF sovradimensionato, stream vuoto e cancellazione prima della lettura.
- L'estrattore PDF limita la lettura anche per stream non seekable e azzera i buffer temporanei; il testo estratto non supera il budget complessivo di 4.000.000 di caratteri.

## Rischi residui e limiti

- La PR #61 resta la sede della correzione per il default loopback Moodle e per la riduzione delle eccezioni Gitleaks; non sono state modificate le stesse aree.
- La verifica dei link riduce il rischio di escape statico, ma non è una primitiva filesystem handle-relative: un processo locale concorrente con permessi di scrittura sulla cartella Documenti potrebbe tentare una sostituzione tra controllo e apertura. Le ACL per utente e i controlli ripetuti riducono la finestra, senza eliminarla completamente.
- I test simbolici ritornano senza asserzione se il runner non concede la creazione di link. La CI Windows deve quindi essere controllata anche per confermare che tali casi siano stati realmente eseguiti.
- Non è stato eseguito un penetration test, né è stata verificata l'applicazione WPF interattivamente in questa sessione.

## Fuori perimetro

Nessuna modifica AI, provider, chiamata a modelli, XAML, temi, navigazione, dipendenza o schema SQLite.


## Follow-up verification — 12 November 2026

The first Windows PR run for this workstream exposed one regression-test failure: a dangling file symbolic link could be created successfully, but the FileInfo.LinkTarget/DirectoryInfo.LinkTarget checks did not identify it on the Windows runner. The remaining 301 tests passed; the failure was specifically IsInsideRejectsDanglingFileSymbolicLinks.

LocalFileSecurity.IsReparsePoint now also calls File.ResolveLinkTarget and Directory.ResolveLinkTarget with returnFinalTarget=false before the attribute fallback. These APIs inspect the link entry even when its target is absent. The regression test remains enabled; the corrected implementation is not considered verified until the new Windows CI run passes.

The original Windows failure is documented here for traceability: https://github.com/red04yes-cyber/Unitracker/actions/runs/42706181354.
