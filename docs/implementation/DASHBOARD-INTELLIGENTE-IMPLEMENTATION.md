# Dashboard intelligente — guida step-by-step

## Obiettivo

Rendere la pagina **Oggi** un punto operativo quotidiano: esami in arrivo, attività da completare, tempo di studio della settimana, obiettivo settimanale, suggerimento di studio spiegabile e scadenze imminenti.

La dashboard usa esclusivamente dati locali già presenti in `StudyData`. Non introduce AI, provider, API, servizi remoti, notifiche automatiche o dati dimostrativi.

## Stato di partenza verificato

- La dashboard WPF e il relativo redesign visuale sono già presenti nel branch `work/phase-5-dashboard-redesign` (PR #59).
- `MainViewModel` carica il singolo snapshot `StudyData`, espone il prossimo esame e coordina `StudyWorkspaceViewModel`.
- `StudyWorkspaceViewModel` espone task attivi, sessioni Focus e metriche settimanali.
- `StudyData` contiene esami pianificati, task, eventi calendario, flashcard e attività registrate.
- `ProgressService` calcola tempo registrato dal registro attività, evitando di sommare due volte un intervallo collegato a una sessione Focus.
- Le date del planner restano indicative; gli eventi conservano fonte e stato di verifica.

## Sequenza di implementazione

### Step 1 — definire un riepilogo derivato e testabile

Creare un servizio applicativo puro/read-only che riceve `StudyData` e un istante `now` iniettabile. Restituisce un riepilogo immutabile con:

1. prossimo esame non completato e conto alla rovescia;
2. task aperti in scadenza oggi o già in ritardo;
3. tempo di studio registrato da lunedì a oggi;
4. progresso verso l'obiettivo settimanale configurato;
5. suggerimento deterministico con motivazione;
6. scadenze calendario e task nei prossimi 14 giorni.

Nessun calcolo deve scrivere o normalizzare i dati ricevuti.

### Step 2 — definire regole trasparenti

- **Esame:** prima data pianificata non passata, escludendo gli esami completati. Il conteggio usa giorni di calendario locali; non attribuisce ufficialità alla data.
- **Task di oggi:** task `Todo` o `InProgress` con scadenza locale non successiva a oggi. Gli arretrati restano visibili.
- **Ore settimanali:** usare `ProgressSnapshot.FocusMinutes` nel periodo lunedì–oggi. Il valore deriva dai secondi effettivi delle sessioni Focus e non include le pause. Il registro manuale `RecordedMinutes` è una fonte distinta e non viene sommato: nel flusso WPF corrente non è collegato automaticamente alle sessioni Focus, quindi unirli produrrebbe un totale non dimostrabile.
- **Obiettivo:** rapporto tra minuti Focus e `WeeklyStudyHours × 60`, limitato visivamente a 100%; i minuti effettivi non vengono troncati.
- **Scadenze:** task non completati e soli eventi calendario `Deadline` non cancellati, da ora ai successivi 14 giorni. Fonte e verifica dell'evento restano esplicite.
- **Suggerimento:** prima il task già in ritardo/oggi con priorità più alta e scadenza più vicina; poi il task imminente più prioritario; quindi le flashcard dovute; infine un'indicazione sul prossimo esame o un messaggio che invita a configurare i dati mancanti. A parità di condizioni, l'ordinamento è stabile.
- Ogni suggerimento mostra la regola che lo ha prodotto. Non usa modelli linguistici né invia dati fuori dal dispositivo.

### Step 3 — collegare il riepilogo al ViewModel

Iniettare il servizio nel ViewModel della dashboard e ricalcolare il riepilogo dopo:
- caricamento iniziale dello snapshot;
- modifica di esami o date;
- creazione/modifica/eliminazione di eventi;
- aggiunta/completamento/rimozione di task;
- avvio, pausa, ripresa o conclusione delle sessioni Focus;
- avanzamento del timer, con ricalcolo del riepilogo al cambio di ogni minuto di studio effettivo.

La UI deve mostrare stati vuoti espliciti quando mancano esami, task, attività o un obiettivo configurato.

### Step 4 — integrare i componenti nella pagina Oggi

Mantenere il design system WPF esistente e aggiungere:
- card esame con nome, data e giorni rimanenti;
- lista task odierni con scadenza, priorità e azione di completamento;
- card ore settimanali e barra progresso obiettivo;
- card suggerimento con motivazione e accesso a Studio Lab;
- elenco scadenze imminenti con data e provenienza/verifica.

Tutti i binding devono puntare ai dati reali. La lista task deve riutilizzare il task persistito e il comando già esistente, senza creare una seconda fonte dati.

### Step 5 — testare regole e casi limite

Aggiungere test deterministici per:
- esame più vicino, esami completati e date passate;
- task oggi, arretrati, futuri, senza scadenza e completati;
- scadenze calendario, eventi cancellati e tipi evento non pertinenti;
- settimana che inizia lunedì e assenza di obiettivo configurato;
- tempo Focus effettivo, pause escluse, aggiornamento delle sessioni in corso e percentuale obiettivo limitata al 100%;
- priorità del suggerimento, fallback flashcard/esame e dataset vuoto;
- assenza di mutazioni dello snapshot.

Usare un orologio esplicito nei test, senza rete o account Moodle.

### Step 6 — verifica e consegna

- Verificare diff, binding XAML e uso di soli servizi locali.
- Eseguire i test .NET 10 e i workflow CI Windows esistenti.
- Controllare che nessun file o servizio AI sia modificato.
- Nessuna modifica a schema, migrazioni, formati di persistenza o dipendenze, salvo necessità dimostrata.
- Dichiarare separatamente test eseguiti, CI e verifica manuale WPF su Windows.

## Criteri di accettazione

- Ogni numero e riga visualizzata deriva da dati persistiti o è indicata come non configurata.
- Il servizio riepilogo è read-only e usa un istante iniettabile.
- I task possono essere completati dal flusso esistente senza duplicati.
- Le date indicative non sono etichettate come appelli ufficiali.
- Le ore settimanali derivano dalle sessioni Focus effettive e non da stime; le pause non sono conteggiate.
- Suggerimento e motivazione sono deterministici e interamente locali.
- Test e CI non richiedono credenziali o chiamate a servizi esterni.
