# Primo avvio e guida rapida

UniTo StudyHub è un'app desktop Windows per organizzare corsi, voti, calendario, esami e attività di studio. Moodle è facoltativo: le funzioni locali non richiedono di collegare l'account universitario.

## Requisiti

- Windows 10/11 x64.
- Per eseguire il pacchetto portabile self-contained non è necessario installare un runtime .NET separato.
- Per compilare dal sorgente serve il .NET 10 SDK.
- Per Moodle: una piattaforma raggiungibile, Web Services REST abilitati e un token autorizzato dall'istanza.

Il repository documenta il publish portabile Windows x64; prima di distribuire un pacchetto verifica la provenienza e il checksum SHA-256 pubblicato insieme all'archivio.

## Primo avvio

1. Avvia `UniToStudyHub.exe`.
2. Inserisci i corsi e i relativi CFU; registra gli esami e i voti quando disponibili.
3. Usa calendario e planner per organizzare eventi e date di studio. Le date proposte dal planner sono indicative, non appelli ufficiali.
4. Registra sessioni, task e materiali di studio secondo le sezioni disponibili.
5. Crea e verifica periodicamente un backup cifrato prima di aggiornamenti importanti o cambiamenti al dispositivo.

## Collegamento Moodle (facoltativo)

1. Usa solo l'istanza Moodle dell'Università o un'altra istanza per cui hai autorizzazione.
2. Chiedi all'amministratore o consulta la documentazione dell'istanza per abilitare Web Services REST e creare un token con le capability necessarie. Non usare token di terzi.
3. Nell'area Moodle dell'app inserisci l'URL base dell'istanza (HTTPS; senza credenziali, query o frammenti) e il token Web Services.
4. Premi **Verifica e salva token**. La verifica contatta Moodle prima di salvare il token.
5. Se la verifica riesce, usa **Sincronizza Moodle** per aggiornare i dati accessibili.
6. Per interrompere il collegamento, usa **Disconnetti**: il token viene rimosso e i riferimenti di collegamento vengono puliti; i corsi, i voti e lo storico locale non vengono cancellati.

La sincronizzazione è limitata alle funzioni Web Services e ai permessi del token. Se una funzione, un file o un tentativo non è esposto da Moodle, StudyHub non lo può recuperare. Non aggira SSO, MFA, ruoli o controlli di accesso.

**Non condividere o committare il token.** Non inserirlo in screenshot, issue, log, URL o file di configurazione versionati.

## Dati, backup e privacy

- Il database è salvato normalmente in `%LOCALAPPDATA%\UniToStudyHub\unitracker.db` e cifrato con SQLCipher.
- La chiave del database e il token Moodle sono protetti tramite DPAPI per l'account Windows corrente.
- I file scaricati da Moodle sono separati dal database e si trovano nella cartella Documenti gestita da StudyHub; considera anche la protezione del volume (BitLocker/EFS).
- I backup sono cifrati. Un backup protetto da password **non** rende trasferibile la chiave DPAPI del database: il ripristino del database richiede lo stesso account Windows che ha generato la chiave.
- L'esportazione completa JSON contiene dati personali e note private, ma non token o chiavi API. Conservala in un luogo sicuro.
- La cancellazione completa richiede conferma; puoi scegliere se eliminare anche i backup cifrati. La cancellazione non è annullabile.

Per i dettagli, i limiti e la procedura di cancellazione consulta [Privacy e protezione dei dati](security/PRIVACY.md).

## Limiti conosciuti

- StudyHub non è un client Esse3 e non garantisce la correttezza delle date ufficiali. Verifica gli appelli sui canali UniTo.
- La disponibilità dei dati Moodle dipende dalle capability abilitate e dal token autorizzato.
- I file Moodle non sono cifrati da SQLCipher; la loro protezione dipende dai permessi del filesystem e dalla cifratura del volume.
- L'app non invia telemetria o dati personali a servizi remoti automaticamente. I collegamenti Moodle e gli eventuali servizi configurati vengono contattati solo quando avvii le operazioni pertinenti.

