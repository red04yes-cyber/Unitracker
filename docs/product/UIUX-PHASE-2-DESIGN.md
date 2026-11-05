# UI/UX Phase 2 — StudyHub

## Obiettivo
Integrare le funzionalità della technical slice 2 nell'interfaccia WPF mantenendo il linguaggio visivo già approvato: navy `#17243B`, blu `#3157D5`, superfici chiare, Segoe UI Variable, gerarchia tipografica sobria, angoli moderati, spaziatura coerente e stati accessibili. Nessun dato dimostrativo: gli stati vuoti sono parte del design.

## Regole d'integrazione
- `StudyData`/SQLite resta l'unica fonte dati. La UI non crea store, calcoli o countdown autonomi.
- Le chiamate asincrone mostrano busy state, disabilitano l'azione concorrente e presentano errori recuperabili.
- Le date planner/indicative sono sempre marcate come stime; gli eventi ufficiali/importati mostrano provenienza e verifica.
- Nessuna risposta AI o citazione appare come attendibile senza stato configurato e validazione della citazione.
- Ripristino backup richiede conferma esplicita e mostra esito/percorso del backup di sicurezza.
- Nessuna schermata mostra token, chiavi API o segreti.

## Calendario

### Struttura
Header con titolo del periodo, navigazione precedente/successivo e azione Oggi. Segmenti Giorno / Settimana / Mese. La selezione del periodo deve essere evidente e mantenere la stessa grammatica visiva delle altre pagine.

### Mese
Griglia 7 colonne con intestazioni lun-dom, celle con numero del giorno e indicatori evento. Il giorno selezionato ha superficie blu tenue; oggi ha un anello/indicatore distinto. Il pannello laterale o inferiore mostra gli eventi del giorno selezionato, ordinati per ora.

### Settimana e giorno
La prima integrazione usa un’agenda settimanale a sette colonne e un’agenda giornaliera a colonna singola, con orari, titoli e provenienza. Non è ancora una griglia temporale verticale: resta una possibile rifinitura. Gli eventi che attraversano la mezzanotte sono filtrati per intervallo di ciascun giorno senza duplicare il record. Gli eventi all-day sono etichettati separatamente.

### Evento
Editor in dialog/pannello con titolo, categoria, data, ora inizio/fine, tutto il giorno. Per eventi importati/Planner mostrare origine e stato di verifica in sola lettura. Le azioni Modifica/Elimina devono rispettare i comandi reali del ViewModel. Planner/Indicative usa badge neutro “Pianificato”, Official “Verificato”, Unverified “Da verificare”; non codificare lo stato solo con il colore.

## Studio Lab

### Layout
Dashboard a due aree: Focus in primo piano; sotto/accanto Attività e Ripasso. Sezioni secondarie per Diario errori e check-in energia/distrazioni.

### Focus
Timer grande con durata residua, stato (Pronto/In corso/In pausa/Scaduto), materia opzionale e controlli Avvia/Pausa/Riprendi/Termina/Annulla. Il display interroga `FocusSessionService.Snapshot`; il timer visuale aggiorna solo la presentazione. Dopo riapertura mostra la sessione ricostruita dal servizio.

### Task e flashcard
Task con materia, scadenza, priorità e minuti stimati; filtri Oggi/Prossimi/Completati. Flashcard con conteggio dovute e flusso di ripasso; rating Again/Hard/Good/Easy ben distinti. Stato vuoto con invito ad aggiungere la prima attività/carta.

### Diario
Errori raggruppati per materia/argomento con causa, stato di revisione e prossima revisione. Energia e distrazioni sono annotazioni volontarie, non giudizi o punteggi.

## AI & PDF Intelligence

### Libreria documenti
Elenco documenti con nome, materia, data importazione, pagine e stato estrazione. Duplicato, OCR richiesto, importazione fallita e documento pronto hanno stati distinti e descrittivi.

### Lettore e ricerca
Vista a due pannelli: libreria documenti con anteprima del testo estratto pagina per pagina e risultati di ricerca. Ogni risultato mostra numero pagina e snippet. La UI non etichetta ancora le risposte AI come citazioni verificate; il provider predefinito resta non configurato.

### Assistente
Input domanda/riassunto con selezione documento. Stati obbligatori:
- NotConfigured: spiegare che manca la configurazione AI; non mostrare output generato.
- Configured: mostrare risposta solo dopo completamento; validare ogni citazione prima di etichettarla verificata.
- Refused: spiegazione breve e neutra.
- Error: messaggio recuperabile, nessuna risposta parziale spacciata per completa.
Mostrare consenso e destinazione del trattamento prima di inviare contenuti a un provider remoto.

## Progressi

Dashboard sul periodo settimanale corrente con card di tempo focus, sessioni completate, task completati, flashcard dovute, errori aperti e streak. Il goal settimanale e i minuti di focus per materia sono resi con barre di avanzamento. Le metriche derivano esclusivamente da `ProgressService`; non è ancora presente un selettore periodo o una serie giornaliera.

Se il campione è insufficiente, sostituire il numero con “Dati insufficienti” e indicare quale attività serve registrare. Pattern errori solo se supera il minimo campione; weekly review presenta osservazioni e azioni come suggerimenti, non diagnosi.

## Sistema e backup

Sezione Impostazioni > Dati e backup con:
- posizione database e cartella backup (sola lettura se non configurabile);
- elenco backup con data, dimensione e versione manifest;
- azioni Verifica, Crea backup e Ripristina;
- stato integrità non verificato finché `VerifyBackupAsync` non termina;
- checksum e risultato verifica in dettaglio espandibile.

Ripristino: dialog di conferma con percorso selezionato, avviso di sostituzione dati, backup di sicurezza preventivo e possibilità di annullare prima di confermare. Dopo l'operazione mostrare percorso del backup di sicurezza restituito dal servizio. Non promettere cifratura: il manifest non è cifrato.

## Accessibilità e responsive
- Navigazione completa da tastiera e focus visibile.
- Contrasto conforme, testo ridimensionabile e nessuna informazione affidata solo al colore.
- Etichette esplicite per icone e pulsanti; target interattivi adeguati.
- Layout adattabile a finestre strette: pannelli affiancati diventano impilati; griglia calendario conserva leggibilità tramite scroll orizzontale quando necessario.
- Empty/loading/error/success state progettati per ogni sezione.

## Sequenza di implementazione UI
1. Risolvere e integrare il calendario con viste mese/settimana/giorno, senza cambiare la sorgente dati.
2. Creare viste e ViewModel dedicati Studio Lab.
3. Creare libreria PDF e flusso AI con stati espliciti.
4. Collegare Progressi ai servizi e alle formule reali.
5. Collegare Sistema/Backup con verifica e conferma ripristino.
6. Eseguire build e test; completare verifica visuale/runtime su Windows prima del merge.

## Dipendenze da chiarire con il ramo tecnico
- ViewModel dedicati o contratti binding per Studio Lab, PDF/AI, Progressi e Backup.
- Il risultato di `BackupService.RestoreAsync` deve esporre il percorso del backup di sicurezza; la UI non deve dedurlo.
- Per il timer, esporre uno snapshot aggiornato e comandi async con cancellazione.
- Per PDF/AI, esporre progresso/cancellazione import e stato provider/consenso.
- Per il calendario, il ViewModel deve esporre selettore periodo e collezioni per giorno/settimana/mese, mantenendo `CalendarEvents` come sorgente unica.
