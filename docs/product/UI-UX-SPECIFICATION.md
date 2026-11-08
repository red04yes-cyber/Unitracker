# UniTo StudyHub — UI/UX Product Specification

> Stato: specifica di prodotto e interazione per la riprogettazione desktop.
> Ambito: WPF, Windows 10/11, .NET 10, MVVM.
> Principio guida: StudyHub deve essere una centrale operativa universitaria, non un contenitore di schermate e tabelle.

## 1. Obiettivo del prodotto

All'apertura l'utente deve capire in pochi secondi:
1. come sta procedendo la propria carriera;
2. qual è la prossima priorità universitaria;
3. quali esami e attività richiedono attenzione;
4. dove intervenire per pianificare o studiare.

La UI deve essere premium, sobria, densa quanto basta e immediatamente leggibile. Nessuna statistica, missione, ora di studio, appello o notifica può essere inventata per riempire uno spazio.

## 2. Architettura di navigazione

La navigazione primaria è una sidebar persistente, con quest'ordine:

| Voce | Ruolo | Stato iniziale |
|---|---|---|
| Oggi | Dashboard strategica e priorità | Attiva |
| Materie | Catalogo, sessioni e archivio esami | Attiva |
| Calendario | Appelli, lezioni e piano di studio | Progressiva: appelli pianificati prima del calendario completo |
| Studio Lab | Focus, missioni, errori e strumenti di studio | Da implementare |
| Accademia | Esperti didattici e strumenti AI | Da implementare |
| Progressi | Analytics e andamento nel tempo | Progressiva |

Impostazioni e integrazioni sono accessibili dalla topbar o dalla sezione Impostazioni, non come voce primaria concorrente alle sei aree.

Le sezioni non ancora implementate possono essere mostrate come non disponibili con un'etichetta esplicita; non devono sembrare funzionanti, contenere pulsanti finti o generare dati dimostrativi. La navigazione deve restare indipendente dal contenuto della singola pagina.

## 3. Shell desktop

- Finestra iniziale centrata, dimensioni di riferimento 1360 × 860 px.
- Dimensioni minime target: 980 × 680 px; layout fluido sotto la dimensione di riferimento.
- Sidebar circa 224–240 px, con possibilità di collasso in modalità icone.
- Topbar compatta: titolo/breadcrumb della pagina, ricerca quando realmente supportata, stato delle integrazioni e accesso alle impostazioni.
- Area contenuti con margini interni 24–32 px e larghezza controllata.
- Scroll verticale nella sola area contenuti; mai scroll orizzontale per usare le funzioni ordinarie.
- Titolo pagina, descrizione breve e azione primaria sempre nella stessa gerarchia visiva.

## 4. Design system

### Palette

| Token | Light | Utilizzo |
|---|---|---|
| Ink / Navy | #17243B | Testi principali, sidebar |
| Accent / Blue | #3157D5 | Azioni primarie, selezione |
| Page | #F3F6FB | Sfondo della finestra |
| Surface | #FFFFFF | Card e pannelli |
| Border | #E4EAF2 | Separazione dei contenuti |
| Muted | #718096 | Testi secondari |
| Success | #21865D | Esiti positivi e avanzamento |
| Warning | #C98224 | Priorità e scadenze |
| AI / Violet | #7654C8 | Funzioni intelligenti |

Il tema scuro deve riutilizzare gli stessi token semantici attraverso risorse dinamiche, senza valori colore codificati nelle singole viste. Il tema chiaro è predefinito.

### Tipografia e superfici

- Font: Segoe UI Variable, fallback Segoe UI.
- Titolo pagina 26–30 px; titoli sezione 16–18 px; testo 13–15 px; metadati 11–12 px.
- Spaziatura coerente su multipli di 4/8 px.
- Card con raggio 10–14 px, bordo sottile, ombra molto leggera.
- Pulsanti primari solo per la singola azione principale; azioni secondarie neutre.
- Icone coerenti, semplici e sempre accompagnate da testo dove l'azione non è ovvia.
- Colore mai usato come unico mezzo per comunicare stato.

### Stati e feedback

Ogni vista deve progettare esplicitamente:
- caricamento;
- contenuto disponibile;
- archivio vuoto;
- ricerca senza risultati;
- errore di caricamento/salvataggio;
- sincronizzazione in corso, riuscita o fallita;
- azione distruttiva in attesa di conferma.

I messaggi di errore devono dire cosa è successo e quale azione sicura è possibile fare. I salvataggi devono produrre feedback visibile e non bloccare la UI.

## 5. Oggi — Dashboard strategica

La dashboard è la pagina iniziale. Gerarchia dall'alto verso il basso:

1. Saluto sobrio e contesto del giorno.
2. **Focus di oggi**: una sola priorità dominante. In Fase 1, se non esistono task/sessioni, mostrare uno stato vuoto onesto e un collegamento alle materie; non simulare un piano.
3. Indicatori di carriera alimentati dal database:
   - CFU acquisiti;
   - media ponderata;
   - media aritmetica, se disponibile e definita;
   - voto base di laurea/proiezione, con etichetta chiara;
   - esami superati e ancora da sostenere, solo se il modello consente di calcolarli senza ambiguità.
4. Prossimi appelli: data, materia, origine del dato e aggiornamento; se non disponibili, stato vuoto.
5. Attività recenti/ultimi voti.
6. In una fase successiva: missioni quotidiane, curva dell'oblio, ripassi dovuti e ore di focus.

Regola: non mostrare un totale CFU come 36/180 se il totale previsto non è configurato nel profilo. Non mostrare una media, un appello o un obiettivo non ricavabile dai dati reali.

## 6. Materie — catalogo e archivio

La pagina Materie deve evitare la tabella enorme come layout dominante.

### Struttura
- Header con titolo, conteggi e azione "Aggiungi materia".
- Ricerca e filtri per stato, sessione e CFU quando sono collegati a dati reali.
- Schede materia con nome, codice, CFU, docente quando presente, stato e prossimo appello quando verificato.
- Tre viste logiche: In corso, Sessioni, Archivio.
- L'archivio deve rappresentare esami effettivamente superati/registrati; non va confuso con una semplice proprietà di UI.
- Dettaglio materia con programma, appelli, materiali Moodle, appunti e andamento; mostrare solo le sezioni già supportate dai dati.

### Modifica dei dati
- Aggiunta e modifica con validazione inline.
- Registrazione voto con materia selezionata, voto, lode se supportata e data.
- Eliminazione con conferma che specifichi l'effetto sui dati collegati.
- Archiviazione e ripristino non devono alterare voti, CFU o relazioni accademiche.
- Dopo ogni modifica, indicatori e viste dipendenti si aggiornano automaticamente.

## 7. Calendario

Il calendario dovrà avere viste giorno/settimana/mese e filtri per materia e tipo di evento.

Tipi di evento previsti:
- appelli ufficiali;
- lezioni importate;
- sessioni di studio;
- ripassi;
- missioni e scadenze personali.

Il planner propone blocchi in base a ore disponibili, difficoltà, data d'esame e ripassi dovuti. Gli spostamenti automatici richiedono conferma e non sovrascrivono impegni esistenti.

Ogni appello deve conservare fonte, data di acquisizione e stato di verifica. I dati indicativi del planner non devono essere visualizzati come appelli ufficiali.

## 8. Studio Lab

Studio Lab è il workspace operativo, con una singola azione primaria di avvio sessione e accesso secondario agli strumenti.

Moduli:
- Pomodoro intelligente;
- minuti/ore di focus e registro sessioni;
- missioni giornaliere e missioni create dall'utente;
- Active Recall;
- flashcard e schede personali;
- diario degli errori (materia, domanda, errore, correzione, causa);
- parcheggio distrazioni;
- formule e cheat sheet;
- planner settimanale;
- check-in energia;
- streak e statistiche di focus.

Un errore può generare una voce di ripasso. Il completamento di una missione aggiorna calendario, registro focus e analytics. Le missioni non vanno generate senza una regola esplicita e dati disponibili.

## 9. Accademia e strumenti AI

Accademia è organizzata per attività e contesto di materia, non come una chat unica.

Esperti:
- apprendimento;
- progettazione didattica;
- preparazione esami;
- progettazione sostenibile;
- metacognizione.

Strumenti:
- AI Study Director: piano giornaliero motivato e modificabile;
- Memory Engine: ripasso dilazionato, spiegazione orale/scritta e valutazione del recupero;
- Exam Simulator: fac-simile coerenti con syllabus, appunti e prove fornite;
- PDF Intelligence: estratti/sintesi/domande con riferimenti a documento e pagina;
- Learning Analytics: tempi, risultati, errori e aree deboli.

I contenuti generati dall'AI devono distinguere chiaramente fonti caricate, dati ufficiali e spiegazioni del modello. Le fonti devono essere ispezionabili. Non presentare materiale sintetico come prova ufficiale UniTo.

## 10. Progressi

Progressi raccoglie:
- CFU e andamento carriera;
- media e andamento voti;
- esami conclusi e pianificati;
- focus e sessioni;
- missioni completate;
- accuratezza dei ripassi;
- errori ricorrenti e aree deboli.

Ogni grafico deve rispondere a una domanda concreta. Mostrare periodo, unità, origine e definizione della metrica. Evitare grafici decorativi e confronti non significativi.

## 11. Architettura WPF/MVVM

- Mantenere WPF e MVVM.
- Shell e navigazione indipendenti dalle pagine.
- Un View e un ViewModel per area: Dashboard, Materie, Calendario, Studio Lab, Accademia, Progressi, Impostazioni.
- Evitare di continuare ad ampliare un unico MainViewModel come contenitore di tutte le funzionalità.
- Servizi applicativi e dominio condividono i casi d'uso; le viste non contengono logica di persistenza o calcolo.
- Design token in risorse WPF condivise; temi tramite DynamicResource e dizionari dedicati.
- Controlli riutilizzabili per card, KPI, empty state, badge, toolbar, dialog e feedback.
- Le pagine non implementate devono essere dichiarate come tali e non avere comandi inerti.

## 12. Roadmap e criteri di accettazione

| Fase | Ambito | Criterio di uscita |
|---|---|---|
| 1 | Shell, design system, Oggi, Materie | Navigazione stabile, dati reali, archivio coerente, tema chiaro, layout adattivo |
| 2 | Calendario e appelli | Eventi con fonte e aggiornamento, planner confermabile, nessuna sovrascrittura silenziosa |
| 3 | Studio Lab | Timer persistente, missioni, registro focus, errori e ripassi collegati |
| 4 | Accademia e Memory Engine | Piano verificabile, ripassi tracciabili, AI con fonti e feedback |
| 5 | Exam Simulator, PDF Intelligence, Analytics | Simulazioni basate su fonti, citazioni pagina, metriche verificabili |
| 6 | Importazioni e sincronizzazioni | Import idempotente, gestione duplicati, origine e freschezza dei dati |

### Definition of Done per ogni fase
- Build Windows Release riuscita.
- Test unitari e di integrazione per le nuove regole.
- Nessuna regressione di persistenza o migrazione.
- Prova manuale su Windows a finestra ridimensionata e con tema chiaro/scuro.
- Stati vuoti, errore, caricamento e conferma testati.
- Nessun dato dimostrativo in produzione.
- Aggiornamento delle schermate accessibile da tastiera.
- Evidenza di test allegata alla PR.

---

## Decisioni definitive

1. La home è **Oggi**.
2. **Materie** è il centro della carriera e dell'archivio.
3. **Calendario** è la fonte operativa delle scadenze e della pianificazione.
4. **Studio Lab** è il workspace di studio.
5. **Accademia** è il livello didattico e AI.
6. **Progressi** è la vista analitica.
7. Una sola fonte dati alimenta tutte le aree; non si duplicano informazioni a mano.
8. Affidabilità e trasparenza prevalgono su animazioni, gamification e densità informativa.
