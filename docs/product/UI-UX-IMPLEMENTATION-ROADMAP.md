# UniTo StudyHub — UI/UX implementation roadmap

> Documento operativo per la realizzazione dell'interfaccia WPF.
> Responsabilità: UI, UX, design system, navigazione, accessibilità e integrazione dei binding.
> La logica di dominio, i servizi, la persistenza e le integrazioni tecniche restano in carico al lavoro funzionale separato.

## 1. Principi non negoziabili

1. **Una sola fonte di verità.** La UI non duplica, inventa o corregge autonomamente dati di carriera, calendario, studio o analytics.
2. **Stato comprensibile.** Ogni informazione comunica se è ufficiale, importata, inserita dall'utente, stimata o generata dall'AI.
3. **Nessuna funzione finta.** Un controllo è attivo solo quando esiste un comando reale e testato. Le funzioni future sono descritte come non ancora disponibili, senza pulsanti inerti.
4. **Priorità prima della quantità.** Ogni schermata ha una sola azione primaria e una gerarchia chiara.
5. **Tracciabilità.** Le fonti, gli aggiornamenti, le assunzioni e i limiti sono accessibili senza appesantire la vista principale.
6. **Progressive disclosure.** I dettagli avanzati appaiono quando servono, non tutti insieme.
7. **Accessibilità desktop.** Navigazione da tastiera, focus visibile, testo leggibile, contrasto sufficiente e nessuna informazione affidata soltanto al colore.

## 2. Linguaggio visivo

### Direzione

Desktop premium, contemporaneo e sobrio. L'interfaccia deve sembrare uno strumento di lavoro universitario, non un social, un gioco o una dashboard aziendale generica.

### Token semantici

| Token | Valore light | Uso |
|---|---|---|
| Ink / Navy | #17243B | Titoli, testo principale, sidebar |
| Accent | #3157D5 | Azione primaria, selezione, link |
| Page | #F3F6FB | Sfondo generale |
| Surface | #FFFFFF | Card, dialog, pannelli |
| Border | #E4EAF2 | Separazioni |
| Muted | #718096 | Metadati e testo secondario |
| Success | #21865D | Esiti positivi |
| Warning | #C98224 | Attenzione e scadenze |
| AI | #7654C8 | Origine o strumenti AI |

Tutti i colori delle viste devono derivare da risorse semantiche condivise. I temi chiaro e scuro useranno gli stessi token, con valori diversi. Evitare colori hardcoded nelle singole schermate.

### Tipografia e superfici

- Segoe UI Variable, fallback Segoe UI.
- Titolo pagina: 26–30 px; titolo sezione: 16–18 px; testo: 13–15 px; metadati: 11–12 px.
- Griglia di spaziatura a multipli di 4/8 px.
- Card con raggio 10–14 px e bordo leggero.
- Ombre appena percettibili, solo dove migliorano la gerarchia.
- Icone semplici e coerenti; accompagnare con testo le azioni non ovvie.
- Evitare gradienti decorativi, effetti vistosi e animazioni non funzionali.

## 3. Shell e navigazione

### Struttura persistente

- Sidebar: Oggi, Materie, Calendario, Studio Lab, Accademia, Progressi.
- Impostazioni e integrazioni accessibili dalla topbar o da una sezione secondaria.
- Topbar contestuale: nome dell'area corrente, eventuale breadcrumb, stato sincronizzazione e accesso rapido alle impostazioni.
- Area contenuti con scroll verticale indipendente.
- Sidebar collassabile su larghezze ridotte, mantenendo tooltip e accessibilità delle icone.
- Pagina e filtri correnti non devono resettarsi senza motivo quando si cambia area.

### Comportamenti

- La voce attiva deve essere riconoscibile anche senza colore.
- Tab e frecce gestiscono la navigazione; Invio/Spazio attivano i comandi.
- Escape chiude dialog e popover non distruttivi.
- Ogni dialog restituisce il focus al controllo che lo ha aperto.
- Le azioni primarie non devono essere nascoste in menu contestuali.

## 4. Schermate principali

## 4.1 Oggi — dashboard strategica

**Domanda a cui risponde:** cosa richiede attenzione adesso?

Ordine dei contenuti:
1. Saluto contestuale sobrio e data.
2. Focus del giorno: una priorità reale o un empty state che invita a pianificare.
3. Prossima scadenza / prossimo appello, con indicazione della fonte.
4. KPI carriera: CFU acquisiti, media ponderata, media aritmetica e proiezione del voto base, solo se calcolabili.
5. Materie attive e archiviate.
6. Attività recenti e ultimi risultati.
7. In futuro: ripassi dovuti, sessioni focus, missioni e rischio di preparazione.

**Stati:**
- Profilo non configurato: invito a completarlo, senza target presunti.
- Nessun esame: stato vuoto e collegamento a Materie/Calendario.
- Nessuna attività giornaliera: suggerimento non invasivo per definire una priorità.
- Dati parziali: mostrare i valori disponibili e spiegare quelli mancanti.
- Errore di aggiornamento: conservare l'ultimo dato valido, indicando che potrebbe non essere aggiornato.

**Regola KPI:** non mostrare rapporti come 36/180 senza un totale CFU configurato. Non trasformare una stima in un dato ufficiale.

## 4.2 Materie — catalogo, sessione e archivio

**Domanda a cui risponde:** quali insegnamenti sto preparando e qual è la loro situazione?

Struttura:
- Header con titolo, conteggi e azione primaria "Aggiungi materia".
- Ricerca per nome/codice e filtri per stato, sessione e CFU quando supportati.
- Sezioni logiche: In corso, Sessione, Archivio.
- Elenco a card responsive; densità compatta disponibile quando il numero di materie cresce.
- Dettaglio materia in pannello o pagina dedicata.

Card materia:
- Nome e codice.
- CFU.
- Stato leggibile.
- Prossimo appello se presente e verificato.
- Indicatore di avanzamento syllabus solo quando il tracker è disponibile.
- Nessuna azione distruttiva direttamente sulla card.

Dettaglio materia:
- Panoramica.
- Syllabus e argomenti.
- Tentativi d'esame e voti.
- Appelli e calendario.
- Materiali, PDF e appunti.
- Errori, flashcard e sessioni di studio collegate.
- Progressi e preparazione.

**Flussi:**
- Aggiunta/modifica tramite form con validazione inline.
- Registrazione di un tentativo con voto, data, lode se supportata e scelta esplicita di accettazione/rifiuto quando il modello lo permette.
- Archiviazione/ripristino con conferma del significato dell'azione.
- Eliminazione solo con riepilogo delle relazioni che saranno coinvolte.
- Lo storico dei tentativi non deve scomparire quando cambia lo stato della materia.

## 4.3 Calendario — impegni e pianificazione

**Domanda a cui risponde:** cosa succede, quando e quanto è affidabile la data?

Viste: settimana, giorno e mese. La settimana è la vista predefinita per la pianificazione operativa; il mese per orientarsi; il giorno per eseguire il piano.

Filtri:
- Esami e appelli.
- Lezioni.
- Sessioni focus.
- Ripassi.
- Scadenze personali.

Ogni evento mostra:
- Titolo, fascia oraria e materia.
- Tipo di evento.
- Stato di verifica.
- Provenienza e ultimo aggiornamento, consultabili nel dettaglio.

**Distinzione obbligatoria:**
- Ufficiale verificato: badge neutro con testo "Ufficiale" e fonte.
- Importato: indicazione della piattaforma/file di origine.
- Personale: evento creato dallo studente.
- Suggerito dal planner: etichetta "Proposta", mai "Appello".

Le proposte del planner appaiono in anteprima; l'utente può accettarle, modificarle o rifiutarle. Spostamenti e conflitti non vengono applicati silenziosamente.

## 4.4 Studio Lab — workspace di studio

**Domanda a cui risponde:** su cosa sto lavorando e come mantengo la concentrazione?

La schermata principale privilegia il timer/sessione attiva e la materia associata. Gli strumenti secondari sono disponibili tramite pannelli dedicati.

Moduli:
- Focus/Pomodoro.
- Missioni e task.
- Ripasso/Active Recall.
- Flashcard.
- Diario degli errori.
- Parcheggio distrazioni.
- Formule e schemi.
- Check-in energia.
- Storico focus.

Durante una sessione:
- Timer sempre leggibile e stato chiaro (focus, pausa breve, pausa lunga, sospesa).
- Materia e obiettivo della sessione visibili.
- Azioni essenziali: pausa, riprendi, termina.
- Conferma prima di terminare una sessione con tempo non registrato, se applicabile.
- Non eseguire automaticamente azioni distrattive.

A fine sessione:
- Riepilogo del tempo effettivo.
- Esito/nota facoltativa.
- Collegamento a task, argomento o errore.
- Proposta di ripasso solo se esiste una regola supportata.

Flashcard:
- Una carta per volta.
- Risposta nascosta fino alla richiesta.
- Valutazione esplicita della difficoltà/recupero.
- Prossimo ripasso e motivazione comprensibili.
- Navigazione da tastiera e supporto a sessioni rapide.

Diario errori:
- Errore, risposta corretta, causa e argomento.
- Azione per trasformarlo in flashcard/ripasso, se supportata.
- Pattern ricorrenti solo dopo un numero di osservazioni sufficiente.

## 4.5 Accademia — AI e strumenti didattici

**Domanda a cui risponde:** quale supporto didattico mi serve, con quali fonti?

Non presentare Accademia come una chat vuota. Organizzare la schermata per attività:
- AI Study Director.
- Memory Engine.
- Feynman Coach.
- Expert profiles.
- Exam Simulator.
- PDF Intelligence.
- Ricerca e conoscenza personale.

Ogni risposta AI deve separare visivamente:
- Dati dello studente.
- Fonti caricate o importate.
- Informazioni ufficiali esterne.
- Contenuto generato dal modello.

Ogni output presenta:
- Fonti consultabili.
- Riferimenti a pagina quando si basa su PDF.
- Indicazione dell'incertezza o dei dati mancanti.
- Azioni "Accetta", "Modifica" e "Ignora" quando propone cambi di piano.
- Nessuna modifica automatica al calendario o alla carriera.

Per gli esperti, ogni profilo deve dichiarare il metodo didattico e lo scopo; evitare avatar o personificazioni eccessive.

## 4.6 Progressi — analytics e riflessione

**Domanda a cui risponde:** cosa sta migliorando e cosa richiede attenzione?

Selettore periodo: settimana, mese, semestre, intera carriera, con intervallo personalizzato quando supportato.

Sezioni:
- Carriera: CFU, media, voti, tentativi.
- Studio: tempo focus e sessioni.
- Memoria: flashcard, accuratezza e ripassi.
- Preparazione: readiness per esame e sue componenti.
- Errori: frequenza, tipologia e andamento.
- Regolarità: continuità e distribuzione del carico.

Ogni visualizzazione espone titolo, unità, periodo, definizione e fonte. Niente grafici decorativi. I punteggi compositi hanno un pannello "Come viene calcolato"; le stime sono marcate come stime.

## 4.7 Impostazioni e profilo

Aree:
- Profilo universitario e obiettivi.
- Preferenze di studio.
- Tema chiaro/scuro/sistema.
- Accessibilità e dimensione testo.
- Moodle e altre integrazioni.
- Provider AI e credenziali.
- Backup e ripristino.
- Notifiche Windows.
- Privacy e gestione dati.

Le credenziali devono essere mascherate e mai mostrate in chiaro dopo il salvataggio. Le operazioni di backup/ripristino espongono destinazione, data, dimensioni e risultato della verifica.

## 5. Mappatura delle 20 funzionalità avanzate

| Funzionalità | Punto UI | Interazione principale |
|---|---|---|
| What-if Simulator | Materie / Progressi | Inserire scenari e confrontare gli esiti senza alterare la carriera |
| Prerequisites Graph | Dettaglio materia | Esplorare prerequisiti e dipendenze |
| Session Simulator | Calendario | Confrontare scenari di appelli e carico |
| Exam Readiness Score | Oggi / Materia / Progressi | Aprire componenti e motivazioni del punteggio |
| Syllabus Tracker | Dettaglio materia / Studio Lab | Segnare argomenti e stato di preparazione |
| Knowledge Graph | Accademia | Esplorare connessioni tra concetti e fonti |
| Question Bank | Accademia / Studio Lab | Cercare, filtrare e allenarsi per argomento |
| Oral Trainer | Accademia | Simulare domanda, risposta e feedback |
| Error Pattern Detector | Progressi / Diario errori | Aprire pattern, esempi e azioni consigliate |
| Feynman Coach | Accademia / Dettaglio materia | Spiegare un concetto e ricevere domande di verifica |
| Citations Engine | Accademia / PDF | Aprire fonte e pagina della citazione |
| Exam Trend Analysis | Materia / Progressi | Analizzare trend con dati sufficienti |
| Time Budget Manager | Oggi / Calendario | Distribuire il tempo disponibile e modificare il piano |
| Energy Intelligence | Studio Lab / Oggi | Usare check-in volontari per adattare i suggerimenti |
| Smart Windows Notifications | Impostazioni | Configurare categorie, orari e quiet hours |
| University Admin Hub | Area dedicata in Accademia o Impostazioni | Consultare scadenze e procedure con fonti verificabili |
| Deep Work Mode | Studio Lab | Avviare una sessione immersiva e gestire le distrazioni |
| Secure Backup Center | Impostazioni | Creare, verificare e ripristinare backup |
| Personal Knowledge Vault | Accademia | Cercare e collegare note, documenti e concetti |
| Weekly Review | Oggi / Progressi | Esaminare settimana, risultati e obiettivi successivi |

## 6. Componenti condivisi

Costruire componenti riusabili, evitando stili duplicati:
- PageHeader con titolo, descrizione, breadcrumb e azione primaria.
- KpiCard con label, valore, unità e nota sulla fonte.
- StatusBadge con testo e icona, non solo colore.
- SourceIndicator per provenienza, freschezza e verifica.
- EmptyState con spiegazione e azione pertinente.
- ErrorState con retry quando sicuro.
- LoadingState che non blocca l'intera finestra.
- SearchFilterBar con filtri applicati visibili e azione di reset.
- ConfirmDialog con conseguenze esplicite.
- DetailPanel per approfondimenti contestuali.
- Toast/status feedback per operazioni concluse.
- ChartPanel con definizione metrica e periodo.
- AIResponsePanel con citazioni e azioni di revisione.

## 7. Stati trasversali

Ogni vista che carica o salva dati deve distinguere:
- In caricamento.
- Dati disponibili.
- Nessun dato.
- Nessun risultato della ricerca.
- Dati parziali.
- Dati non aggiornati.
- Operazione in corso.
- Operazione riuscita.
- Errore recuperabile.
- Errore non recuperabile.

Non cancellare un dato valido dalla schermata solo perché un aggiornamento fallisce. Le azioni distruttive richiedono conferma; le azioni reversibili possono usare feedback e annullamento quando il servizio lo consente.

## 8. Contratti e integrazione con i ViewModel

La UI deve consumare contratti espliciti, non interrogare direttamente repository o database.

Per ciascuna schermata servono:
- ViewModel dedicato o sottoviewmodel con responsabilità chiara.
- Proprietà tipizzate e notifiche di cambiamento.
- Comandi asincroni e cancellabili.
- Stati IsLoading, HasError, ErrorMessage e EmptyState quando pertinenti.
- Validazione esposta al form.
- Messaggi di successo/errore adatti all'utente.
- DTO specifici per il display, senza copiare la logica di dominio nella vista.

La UI non decide formule di media, readiness, priorità o scheduling. Presenta le definizioni fornite dai servizi e apre il dettaglio della spiegazione.

## 9. Ordine di consegna UI/UX

### Fase UI 1 — consolidamento della shell
- Rifinire sidebar e topbar contestuale.
- Migliorare layout ridimensionabile e densità.
- Unificare gli empty/error/loading states.
- Migliorare accessibilità e focus da tastiera.
- Separare i ViewModel per area quando i contratti tecnici sono stabili.

### Fase UI 2 — carriera e materie
- Profilo universitario.
- Dettaglio materia.
- Storico tentativi.
- What-if.
- Prerequisiti e syllabus.
- Session Simulator.

### Fase UI 3 — calendario
- Giorno/settimana/mese.
- Provenienza e verifica degli eventi.
- Conflitti e revisione delle proposte.
- Pianificazione del tempo.

### Fase UI 4 — Studio Lab
- Timer e sessione attiva.
- Missioni, flashcard e ripassi.
- Diario errori.
- Deep Work e check-in energia.

### Fase UI 5 — Accademia
- Study Director.
- Memory Engine e Feynman Coach.
- Question Bank e Oral Trainer.
- PDF Intelligence con citazioni.
- Knowledge Graph e Knowledge Vault.

### Fase UI 6 — Progressi e strumenti personali
- Analytics e readiness trasparente.
- Weekly Review.
- University Admin Hub.
- Notifiche.
- Backup Center e impostazioni complete.
- Tema scuro e preferenze.

Le fasi possono sovrapporsi quando i servizi tecnici sono disponibili, ma una schermata non deve essere dichiarata completa finché i binding reali, gli stati e i flussi non sono verificati.

## 10. Definition of Done UI/UX

Una schermata è pronta quando:
- È collegata a dati e comandi reali.
- Non contiene pulsanti inerti o dati dimostrativi.
- Gestisce caricamento, vuoto, errore, successo e dati parziali.
- Mantiene layout leggibile alla dimensione minima supportata.
- Non richiede scroll orizzontale per le operazioni ordinarie.
- È navigabile con tastiera e ha focus visibile.
- Espone unità, fonte e significato delle metriche.
- Le azioni rischiose hanno conferma e feedback.
- Tema chiaro e scuro mantengono contrasto e gerarchia.
- È verificata manualmente su Windows oltre che tramite build e test automatici.
