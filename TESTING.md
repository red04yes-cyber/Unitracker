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

## Formule
- Media ponderata = somma(voto × CFU) / somma(CFU) degli esami che concorrono alla media.
- Proiezione indicativa = media ponderata × 110 / 30.
- Le date proposte dal pianificatore non sono appelli ufficiali.
