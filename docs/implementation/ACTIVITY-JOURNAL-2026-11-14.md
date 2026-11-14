# Registro attività giornaliero

Data: 14 novembre 2026  
Branch: `feature/activity-journal-timesheet-20261114`

## Scopo

Esporre nella schermata **Progressi** il registro unificato delle attività già persistito da `ActivityLedgerService`. La funzione non introduce una seconda fonte dati e non richiede una migrazione: riusa `StudyActivities` e `ActivityRevisions`.

## Flussi disponibili

- **Timeline giornaliera** con slot da 15, 30 o 60 minuti. La durata segue il giorno locale reale (23/24/25 ore nei giorni di cambio DST); gli slot mostrano secondi effettivi e non trasformano il tempo vuoto in attività presunte.
- **Riepilogo del giorno** con tempo registrato, numero di attività, tempo sovrapposto e intervalli senza registrazioni.
- **Heatmap delle ultime sette giornate**, 24 celle per giorno, calcolata sugli intervalli reali.
- **Quick-log manuale** per studio, ripasso, quiz, esercizi, lettura e simulazione.
- **Collegamenti** facoltativi a materia, argomento del syllabus e task aperto. La selezione di un task con materia allinea la materia del quick-log e restringe le opzioni compatibili.
- **Filtro per tipologia**, navigazione giorno precedente/successivo e ritorno a oggi.
- **Rimozione logica auditabile delle sole voci manuali**: è richiesto un motivo e il ledger conserva la revisione. Non è prevista un'azione di ripristino nell'interfaccia; le attività originate dal timer non possono essere rimosse da questa azione.

## Regole temporali e conteggio

- Il quick-log usa data e orari locali espliciti nel formato `HH:mm`.
- L'intervallo deve terminare dopo l'inizio nello stesso giorno; gli intervalli a cavallo della mezzanotte non sono ancora supportati dal modulo.
- Gli orari locali inesistenti o ambigui durante i cambi DST vengono rifiutati invece di scegliere silenziosamente un offset.
- Timeline, heatmap e riepiloghi delegano l'aritmetica al ledger. Le sovrapposizioni sono segnalate e i secondi coperti più volte vengono conteggiati una sola volta.
- Il pulsante **Aggiorna** ricalcola il diario dal modello in memoria; i cambi di stato del focus lo aggiornano automaticamente.

## Privacy

- La nota del quick-log è privata per impostazione predefinita.
- Negli export standard il titolo, gli orari e i metadati dell'attività restano esportabili; il testo della nota privata viene escluso. L'interfaccia ora lo dichiara esplicitamente per evitare di far credere che l'intera attività sia omessa.
- Titolo e nota rispettano i limiti del ledger (400 e 12.000 caratteri). Le validazioni di riferimento, intervallo e lunghezza restano centralizzate in `ActivityLedgerService`.
- Le revisioni e le note restano nel database locale; nessun servizio esterno viene contattato. I selettori di materia, syllabus e task si sincronizzano con il modello condiviso; ogni aggiornamento riutilizza una sola query per le sovrapposizioni e dizionari indicizzati per le ricerche di materie e sessioni.

## UI/UX introdotta

1. Sezione dedicata in Progressi, con gerarchia e descrizione dei dati.
2. Selettore del giorno e navigazione precedente/successivo/oggi.
3. Granularità selezionabile 15/30/60 minuti.
4. Filtro localizzato per tipologia.
5. KPI per tempo registrato, attività, sovrapposizioni e tempo non registrato.
6. Avviso esplicito quando gli intervalli si sovrappongono.
7. Timeline scorrevole della giornata.
8. Heatmap settimanale con tooltip per ora.
9. Quick-log con materia, syllabus, task e orari.
10. Nota privata predefinita e indicazione precisa dei campi esportabili.
11. Stato vuoto diverso quando il filtro non trova risultati.
12. Rimozione manuale con motivo e storico, senza cancellare le sessioni timer.

## Verifica automatica

La nuova suite `ActivityJournalViewModelTests` copre inizializzazione, timeline e heatmap, quick-log e collegamenti, validazione orari, filtri, granularità, sovrapposizioni, navigazione data, privacy, rimozione auditabile, protezione delle attività timer e rollback in caso di errore di persistenza.

Nessuna dipendenza o tabella nuova è stata introdotta.
