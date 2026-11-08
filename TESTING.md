# MVP test checklist

## Desktop e dati locali
- [ ] Avvio su Windows 10/11 con .NET 10.
- [ ] Aggiunta e persistenza di corsi e CFU.
- [ ] Registrazione voto e aggiornamento media/grafico.
- [ ] Simulazione voto senza modificare lo storico.
- [ ] Pianificazione sessioni e completamento esame.
- [ ] Visualizzazione obiettivo laurea ottobre 2027.

## Moodle (istanza autorizzata)
- [ ] Verificare `core_webservice_get_site_info`.
- [ ] Verificare `core_enrol_get_users_courses` e `core_course_get_contents`.
- [ ] Verificare `core_calendar_get_calendar_events`.
- [ ] Verificare `mod_forum_get_forum_discussions`.
- [ ] Verificare download di PDF e slide.
- [ ] Verificare che host esterni e redirect non vengano seguiti.
- [ ] Verificare protezione DPAPI del token.

## Sicurezza e privacy (Windows)
- [ ] Secret scanning CI verde su PR e push (`.github/workflows/security-scan.yml`).
- [ ] Connessione Moodle, riavvio dell'app e token ancora valido.
- [ ] Disconnessione: token rimosso, richieste rifiutate, carriera e voti invariati.
- [ ] Backup con password: creazione, verifica e ripristino; password errata e file alterato rifiutati; un backup corrotto non sovrascrive il database valido.
- [ ] Backup DPAPI: ripristino con lo stesso utente Windows; un altro utente non può leggerlo.
- [ ] Import PDF oltre 25 MB e file con firma non PDF rifiutati; PDF con troppe pagine rifiutato.
- [ ] Export: note private assenti di default e presenti solo nell'export completo; nessun token o chiave AI.
- [ ] Log in `%LOCALAPPDATA%\UniToStudyHub\logs` senza token, note o percorsi personali.
- [ ] Cancellazione dati: corsi, note, token, chiavi AI, backup e log rimossi; riavvio pulito.
- [ ] AI: senza consenso nessuna chiamata; endpoint `http` o privato rifiutato.
- [ ] SQLite non è cifrato: verificare che il disco usi BitLocker/EFS e che la documentazione non dichiari il contrario.

## Formule
- Media ponderata = somma(voto × CFU) / somma(CFU) degli esami che concorrono alla media.
- Proiezione indicativa = media ponderata × 110 / 30.
- Le date proposte dal pianificatore non sono appelli ufficiali.
