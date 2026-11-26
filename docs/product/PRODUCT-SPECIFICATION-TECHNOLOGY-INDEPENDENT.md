# UniTo StudyHub — Specifica di prodotto indipendente dalla tecnologia

> **Scopo del documento:** descrivere il prodotto che si vuole realizzare, i comportamenti attesi e i criteri di qualità senza vincolare la scelta degli strumenti di sviluppo, delle piattaforme o dell'architettura tecnica.
>
> **Stato:** specifica funzionale di riferimento. Le funzionalità elencate sono requisiti e obiettivi di prodotto: la loro presenza in questo documento non significa che siano già tutte implementate o verificate nel repository.
>
> **Perimetro iniziale:** il nucleo universitario, organizzativo e di studio deve essere completo e utile anche senza funzionalità di intelligenza artificiale. Qualsiasi eventuale area AI resta esclusa dal primo perimetro e richiede una decisione esplicita separata.

## 1. Visione del prodotto

UniTo StudyHub è una piattaforma personale per organizzare la vita universitaria e accompagnare lo studente durante l'intero percorso, dall'iscrizione fino al conseguimento della laurea.

Non deve essere soltanto un registro degli esami, un calendario o un contenitore di appunti. Deve essere un centro operativo nel quale le informazioni accademiche, gli impegni, i materiali e le attività di studio sono collegati e coerenti.

Lo studente deve poter passare senza interruzioni da:

**Organizzare → Pianificare → Studiare → Esercitarsi → Ripassare → Verificare → Migliorare.**

Il prodotto deve ridurre il lavoro manuale ripetitivo, rendere visibili le priorità e aiutare a prendere decisioni informate. Non deve promettere risultati accademici né trasformare stime e previsioni in informazioni ufficiali.

## 2. Obiettivi principali

1. Riunire in un unico spazio la carriera universitaria e le attività quotidiane.
2. Rendere chiaro cosa è stato completato, cosa è in corso e cosa richiede attenzione.
3. Aiutare a pianificare esami e studio in modo realistico e sostenibile.
4. Collegare materie, programmi, appunti, documenti, task, ripassi e sessioni.
5. Registrare il tempo di studio effettivo senza contare due volte la stessa attività.
6. Fornire statistiche comprensibili, basate su dati reali e accompagnate dalle relative definizioni.
7. Integrare fonti universitarie autorizzate senza aggirare permessi o controlli.
8. Proteggere i dati personali, accademici e le credenziali dello studente.
9. Restare utilizzabile anche senza connessione Internet e senza integrazioni esterne, nei limiti delle funzioni che non le richiedono.

## 3. Utenti e configurazione iniziale

Il prodotto nasce con particolare attenzione agli studenti dell'Università di Torino, ma il modello deve poter essere adattato ad altre università e corsi di laurea.

Al primo avvio lo studente deve poter configurare:

- università e dipartimento;
- corso di laurea, denominazione e codice;
- anno di immatricolazione e anno accademico corrente;
- crediti complessivi previsti, se conosciuti;
- obiettivo indicativo di laurea;
- preferenze e disponibilità settimanali;
- fuso orario;
- preferenze di notifiche e promemoria.

I dati non configurati devono restare esplicitamente tali. Per esempio, non mostrare una progressione su 180 crediti se il totale del corso non è stato inserito o verificato.

La configurazione deve poter essere modificata in seguito senza perdere lo storico.

## 4. Dashboard — Oggi

La dashboard è il punto di ingresso e deve rispondere rapidamente a quattro domande:

- Come sta procedendo la mia carriera?
- Qual è la priorità più importante di oggi?
- Quali esami, impegni o attività richiedono attenzione?
- Da dove posso iniziare a lavorare?

### 4.1 Situazione accademica

Mostrare, quando i dati lo consentono:

- crediti acquisiti e crediti complessivi previsti;
- media ponderata e media aritmetica;
- proiezione indicativa del voto finale;
- esami superati, pianificati e ancora da sostenere;
- avanzamento del percorso.

Le formule e le esclusioni devono essere consultabili. Idoneità, esami senza voto, tentativi rifiutati e attività che non concorrono alla media devono essere gestiti secondo regole esplicite.

### 4.2 Attività di oggi

Mostrare:

- una priorità principale;
- task aperti e relative scadenze;
- blocchi di studio pianificati;
- ripassi dovuti;
- sessioni in corso o recenti;
- impegni imminenti.

La priorità può essere suggerita sulla base di scadenza, importanza e carico residuo, ma deve essere spiegabile e modificabile dallo studente.

### 4.3 Prossimi eventi

Mostrare esami, lezioni, consegne, appuntamenti e scadenze personali. Ogni elemento deve rendere comprensibili origine, data di aggiornamento e attendibilità.

Se non sono presenti eventi, mostrare uno stato vuoto utile invece di creare esempi o appuntamenti fittizi.

## 5. Carriera — Materie, esami e voti

La carriera è il registro accademico personale.

### 5.1 Scheda della materia

Ogni materia può contenere:

- nome, codice e crediti;
- docente e anno accademico;
- stato della materia;
- sessione e appelli associati;
- programma e argomenti;
- prerequisiti;
- materiali e documenti;
- appunti e collegamenti;
- ore di preparazione stimate;
- note personali.

Gli stati devono distinguere almeno: da iniziare, in preparazione, pianificata, superata e archiviata.

Archiviare una materia non deve cancellarne voti, crediti, tentativi o relazioni.

### 5.2 Tentativi d'esame

Per ciascun esame conservare lo storico completo:

- data del tentativo;
- esito;
- voto ed eventuale lode;
- idoneità o prova senza voto, se applicabile;
- decisione di accettazione o rifiuto, quando prevista;
- note facoltative.

Un nuovo tentativo non deve cancellare quelli precedenti. Le regole per individuare il risultato valido devono essere esplicite e coerenti con le regole accademiche configurate.

### 5.3 Calcoli

Calcolare:

- media ponderata;
- media aritmetica;
- crediti acquisiti e mancanti;
- proiezione indicativa del voto finale;
- avanzamento del percorso.

Ogni risultato deve indicare formula, dati inclusi, dati esclusi e casi in cui non è possibile calcolarlo.

### 5.4 Simulatore What-if

Permettere di inserire voti ipotetici per gli esami futuri e confrontare scenari diversi.

Le simulazioni devono essere isolate dai dati ufficiali e non devono modificare la carriera reale. Ogni scenario deve poter essere modificato, duplicato, confrontato ed eliminato.

## 6. Pianificazione degli esami

Il pianificatore aiuta a distribuire gli esami tra le sessioni e a preparare un carico sostenibile.

Deve considerare:

- data dell'esame, se ufficiale o inserita dall'utente;
- crediti;
- difficoltà percepita;
- preparazione residua;
- ore stimate;
- priorità;
- prerequisiti;
- disponibilità settimanale;
- altri impegni già presenti.

Deve poter proporre diversi scenari, evidenziare sovraccarichi e mostrare quali esami rischiano di non essere preparati in tempo.

Le proposte non devono spostare, cancellare o sostituire impegni esistenti senza conferma esplicita. Le date suggerite devono essere chiaramente indicate come indicative.

## 7. Calendario unificato

Il calendario raccoglie tutti gli impegni dello studente.

### 7.1 Viste

- giorno;
- settimana;
- mese;
- elenco cronologico.

### 7.2 Tipologie di evento

- appelli;
- lezioni;
- studio;
- ripasso;
- task e missioni;
- consegne;
- impegni personali;
- scadenze amministrative.

### 7.3 Funzioni

- creazione, modifica e annullamento degli eventi;
- collegamento a materie, task e sessioni;
- filtri per periodo, materia e categoria;
- importazione di calendari esterni;
- gestione di ricorrenze ed eccezioni;
- gestione dei fusi orari e dei cambi dell'ora;
- rilevamento delle sovrapposizioni;
- navigazione rapida tra periodi.

Ogni evento deve conservare la provenienza e lo stato di verifica. Gli eventi ufficiali, quelli importati, quelli manuali e le proposte del pianificatore devono essere distinguibili.

## 8. Studio Lab — Spazio operativo dello studio

Studio Lab raccoglie gli strumenti utilizzati durante il lavoro quotidiano.

### 8.1 Focus Room

Ambiente per avviare e registrare sessioni di concentrazione.

Funzioni:

- avvio, pausa, ripresa, completamento e annullamento;
- timer configurabile;
- modalità Pomodoro con focus e pause brevi/lunghe;
- collegamento a materia, argomento e task;
- registrazione dei segmenti di lavoro e pausa;
- nota e risultato della sessione;
- storico delle sessioni;
- recupero coerente dello stato dopo la chiusura e la riapertura dell'applicazione.

Il tempo accreditato deve rappresentare il tempo effettivo registrato, non quello semplicemente pianificato. Le pause non devono essere conteggiate come studio.

La musica ambientale è facoltativa e deve poter essere disattivata completamente.

### 8.2 Task Manager

Ogni task può avere:

- titolo e descrizione;
- materia e argomento;
- priorità;
- data di scadenza;
- durata stimata;
- stato;
- collegamenti a missioni, sessioni ed eventi.

Stati minimi: da fare, in corso, completato e annullato.

Il tempo registrato deve poter essere confrontato con la stima. Un'attività non deve risultare completata solo perché è stata inserita nel calendario.

### 8.3 Missioni

Le missioni raggruppano piccoli obiettivi:

- giornalieri;
- settimanali;
- personalizzati.

Una missione può contenere passaggi, scadenza, difficoltà e ricompensa facoltativa. Il completamento deve essere verificabile attraverso i passaggi o le attività collegate, non attribuito automaticamente.

### 8.4 Flashcard e richiamo attivo

Consentire la creazione di schede domanda/risposta collegate a materie e argomenti.

Funzioni:

- raccolte e categorie;
- sessioni di ripasso;
- valutazione della difficoltà di richiamo;
- storico delle revisioni;
- programmazione della prossima ripetizione;
- sospensione e ripresa delle schede.

Valutazioni previste: Again, Hard, Good ed Easy. Gli intervalli di ripasso devono essere determinati da regole trasparenti e verificabili.

### 8.5 Diario degli errori

Registrare:

- materia e argomento;
- domanda o esercizio;
- risposta errata;
- risposta corretta;
- tipologia dell'errore;
- causa individuata;
- numero e data delle revisioni;
- prossimo ripasso;
- stato di risoluzione.

Gli errori devono poter essere trasformati in attività di ripasso senza perdere il collegamento con la registrazione originale.

### 8.6 Parcheggio delle distrazioni

Permettere di annotare rapidamente un pensiero o un'attività non urgente durante una sessione.

Ogni annotazione può essere associata alla sessione, avere uno stato e una data di risoluzione. Non deve essere implementata alcuna sorveglianza delle altre applicazioni o dell'attività personale.

### 8.7 Check-in personale

Registrazione volontaria di energia, concentrazione e note personali. Questi dati servono a osservare abitudini nel tempo, non a formulare diagnosi o valutazioni mediche.

### 8.8 Registro unificato delle attività

Raccogliere sessioni, ripassi, esercizi, quiz, letture, lezioni, simulazioni e inserimenti manuali.

Ogni registrazione deve poter contenere:

- inizio, fine e durata effettiva;
- fuso orario;
- materia e argomento;
- task, missione o esame collegato;
- origine e stato;
- risultato;
- valutazioni facoltative;
- note e tag.

Le attività devono essere correggibili con uno storico delle modifiche. Intervalli sovrapposti provenienti da più fonti non devono essere conteggiati due volte.

Il registro giornaliero deve poter mostrare la giornata dalle 00:00 alle 24:00 con intervalli di 15 o 30 minuti. Gli spazi non registrati devono rimanere non registrati: non si deve inventare cosa abbia fatto lo studente.

## 9. Programmi d'esame — Syllabus Tracker

Ogni materia può avere un programma suddiviso in argomenti e sottoargomenti.

Per ciascun argomento registrare:

- stato di preparazione;
- copertura;
- difficoltà percepita;
- tempo stimato e tempo effettivo;
- materiali associati;
- ultimo studio;
- ultimo ripasso;
- prossima revisione.

Stati suggeriti: da studiare, in studio, studiato, da ripassare e consolidato.

Il riepilogo deve evidenziare gli argomenti non affrontati, quelli da riprendere e quelli che richiedono ulteriore esercizio.

## 10. Banca delle domande

Una raccolta personale di domande, esercizi e prove.

Ogni elemento può essere classificato per materia, argomento, difficoltà, tipologia e fonte, e può includere risposta, spiegazione e allegati.

Lo studente deve poter creare raccolte, cercare le domande, filtrarle e selezionarle per esercitazioni personalizzate.

## 11. Simulazione degli esami

Consentire di creare prove di allenamento con:

- domande selezionate o casuali;
- tipologie diverse;
- durata configurabile;
- risposte aperte o a scelta multipla;
- criteri di valutazione espliciti;
- risultati e riepilogo per argomento;
- storico dei tentativi.

Una simulazione non deve mai essere presentata come prova ufficiale né come previsione certa del risultato dell'esame.

## 12. Documenti e materiali

La libreria personale raccoglie PDF, slide, documenti, appunti, esercizi, prove e materiali provenienti da fonti autorizzate.

Ogni elemento deve poter riportare:

- nome e tipo;
- materia e argomento;
- provenienza;
- dimensione;
- data di importazione e aggiornamento;
- impronta di contenuto per riconoscere duplicati;
- percorso e disponibilità locale;
- stato di estrazione o lettura.

Funzioni: ricerca, filtri, ordinamento, apertura del documento, apertura della cartella e visualizzazione della provenienza.

Per i documenti leggibili, il sistema può organizzare il contenuto per pagina e permettere la ricerca testuale. Ogni riferimento a una pagina deve corrispondere a una pagina realmente presente nel documento.

## 13. Integrazione con Moodle

L'integrazione è facoltativa e richiede configurazione e autorizzazioni dello studente.

Quando disponibile, deve poter recuperare:

- corsi iscritti;
- sezioni e argomenti;
- moduli e attività;
- materiali e allegati;
- eventi di calendario;
- discussioni e aggiornamenti dei forum;
- dettagli e stati personali esposti dai servizi autorizzati.

La sincronizzazione deve essere ripetibile senza duplicare dati, aggiornare gli elementi cambiati, segnalare quelli rimossi o non più accessibili e conservare provenienza e data di acquisizione.

I file devono essere organizzati per corso e sezione. Download e importazione devono essere sottoposti a limiti di dimensione, controlli sui percorsi e restrizioni sui tipi di file.

Non si devono aggirare autenticazione, ruoli, CAPTCHA, controlli di accesso o condizioni dell'università. Le informazioni non esposte dai servizi autorizzati devono restare non disponibili, con la possibilità di inserimento manuale.

## 14. Centro notifiche

Raccogliere avvisi relativi a:

- nuovi materiali;
- aggiornamenti dei corsi;
- discussioni;
- eventi imminenti;
- esami;
- consegne e scadenze;
- task arretrati;
- ripassi;
- sessioni.

Ogni avviso deve avere origine, data, stato letto/non letto e collegamento all'elemento interessato, quando disponibile.

Consentire filtri, segna come letto, segna tutto come letto, preferenze per categoria, frequenza e orari di silenzio. La sincronizzazione programmata deve essere attivata solo con scelta esplicita e deve poter essere sospesa o disattivata.

## 15. Progressi e statistiche

La sezione Progressi deve aiutare a capire cosa sta migliorando e dove serve intervenire.

### 15.1 Carriera

- crediti acquisiti;
- media e andamento dei voti;
- esami conclusi e pianificati;
- avanzamento verso la laurea.

### 15.2 Studio

- tempo totale e tempo per materia;
- sessioni completate;
- durata media;
- giorni attivi;
- attività completate;
- andamento giornaliero e settimanale.

### 15.3 Apprendimento

- schede ripassate;
- argomenti coperti;
- errori aperti e risolti;
- risultati delle esercitazioni;
- andamento delle simulazioni.

Finestre di analisi minime: 7, 30 e 90 giorni.

Ogni metrica deve dichiarare periodo, unità di misura, fonte, definizione e regole per i dati mancanti. Non mostrare trend o confronti quando il campione è insufficiente.

## 16. Riepilogo settimanale

Il riepilogo settimanale confronta obiettivi e attività realmente registrate.

Deve includere:

- obiettivi previsti e completati;
- tempo di studio;
- materie affrontate;
- ripassi effettuati;
- task completati e rimasti aperti;
- errori risolti;
- sessioni e andamento del focus.

Le osservazioni devono essere descrittive e le eventuali proposte devono restare modificabili. Non devono essere formulate come giudizi sullo studente.

## 17. Area Accademia e funzioni intelligenti

L'area Accademia è una possibile estensione futura dedicata a strumenti didattici avanzati.

**Non rientra nel primo perimetro di realizzazione.** Il nucleo del prodotto deve essere completo e funzionare senza modelli intelligenti, provider esterni o invio di contenuti a servizi di terze parti.

Qualsiasi futura introduzione di tali funzioni richiederà una decisione separata, consenso esplicito, valutazione della privacy e una specifica autonoma. La loro previsione non deve bloccare né condizionare le funzioni principali.

## 18. Impostazioni

Una sezione centralizzata deve consentire di configurare:

- profilo universitario;
- obiettivi accademici;
- disponibilità e preferenze di studio;
- timer e pause;
- notifiche;
- fuso orario;
- integrazioni;
- privacy e gestione dei dati;
- tema e preferenze di visualizzazione.

Le impostazioni devono essere comprensibili, persistenti e modificabili senza perdita di dati.

## 19. Dati, backup e privacy

La piattaforma tratta informazioni personali e accademiche. La riservatezza e la possibilità di recuperare i dati sono requisiti fondamentali.

### 19.1 Protezione

- archiviazione protetta dei dati locali;
- protezione delle credenziali con meccanismi sicuri del dispositivo;
- nessun segreto nei log o nelle esportazioni;
- controllo degli accessi ai file;
- protezione dei file importati;
- nessuna telemetria automatica;
- nessun invio di dati personali senza configurazione e consenso espliciti.

### 19.2 Backup e ripristino

- creazione manuale e pianificata su scelta dell'utente;
- elenco e storico dei backup;
- verifica d'integrità prima del ripristino;
- protezione crittografica;
- gestione di file corrotti o incompatibili;
- backup di sicurezza prima delle sostituzioni;
- conferma esplicita e messaggio finale comprensibile.

### 19.3 Importazione ed esportazione

Supportare esportazione completa e selettiva, anteprima dell'importazione, validazione, unione non distruttiva e sostituzione controllata.

Prima di operazioni distruttive deve essere possibile creare un backup. Le credenziali e le chiavi non devono essere esportate.

### 19.4 Controlli privacy

- consultazione della posizione dei dati;
- disconnessione delle integrazioni;
- rimozione delle credenziali;
- cancellazione selettiva;
- cancellazione completa;
- gestione dei materiali e dei log;
- esportazione diagnostica volontaria e redatta.

La cancellazione deve descrivere chiaramente cosa verrà rimosso e quali copie potrebbero rimanere fuori dal controllo dell'applicazione.

## 20. Esperienza d'uso e accessibilità

L'interfaccia deve essere professionale, moderna, coerente e leggibile. La struttura precisa e il linguaggio visivo potranno essere scelti indipendentemente dagli strumenti utilizzati per realizzare il prodotto.

Requisiti:

- navigazione chiara e coerente;
- gerarchia visiva comprensibile;
- dashboard non sovraccarica;
- ricerca e filtri dove utili;
- feedback visibile per salvataggi e operazioni;
- stati di caricamento, contenuto vuoto, errore e completamento;
- conferme per operazioni distruttive;
- testi ridimensionabili;
- contrasto adeguato;
- navigazione da tastiera;
- etichette comprensibili;
- informazioni non comunicate soltanto attraverso il colore;
- layout utilizzabile su dimensioni differenti.

Nessuna schermata deve mostrare controlli inattivi che sembrano funzionanti o statistiche dimostrative.

## 21. Requisiti trasversali di qualità

### Affidabilità e integrità

- Un'unica fonte coerente per i dati condivisi.
- Aggiornamenti atomici per operazioni che coinvolgono più elementi.
- Nessuna perdita di dati in caso di interruzione.
- Importazioni ripetibili senza duplicati.
- Storico delle modifiche per le informazioni che lo richiedono.
- Gestione esplicita di errori, annullamento e ripresa delle operazioni.

### Trasparenza

- Distinguere dati ufficiali, importati, manuali e stimati.
- Conservare fonte e data di acquisizione.
- Esporre formule e assunzioni.
- Indicare dati mancanti e limiti delle integrazioni.
- Non inventare appelli, attività, ore, notifiche o risultati.

### Prestazioni

- Le operazioni lunghe non devono bloccare l'interazione.
- Ricerca, filtri e riepiloghi devono restare utilizzabili con archivi di grandi dimensioni.
- Evitare ricalcoli e letture ripetitive non necessari.
- Le importazioni devono poter essere annullate e mostrare un avanzamento attendibile.

### Sicurezza

- Validare dati e file in ingresso.
- Limitare dimensioni e tipi di contenuto.
- Proteggere percorsi e credenziali.
- Non seguire destinazioni esterne non autorizzate.
- Verificare integrità prima di importare o ripristinare.
- Usare il principio del privilegio minimo.

## 22. Scelte di prodotto ancora aperte

La specifica non impone in anticipo una particolare soluzione. Prima di iniziare la nuova realizzazione devono essere decise almeno queste questioni:

1. **Piattaforme:** solo computer, oppure anche browser e dispositivi mobili?
2. **Modalità di utilizzo:** installazione personale, servizio online o combinazione delle due?
3. **Dati:** archiviazione solo sul dispositivo, sincronizzazione tra dispositivi o entrambe?
4. **Account:** uso personale senza account, profilo con accesso oppure più profili?
5. **Offline:** quali funzioni devono restare disponibili senza connessione?
6. **Distribuzione:** installazione tradizionale, pacchetto portabile o distribuzione tramite store?
7. **Integrazioni:** quali università e servizi devono essere supportati nel primo rilascio?
8. **Backup:** locale, su spazio scelto dall'utente o sincronizzato?
9. **Collaborazione:** prodotto esclusivamente personale o possibilità futura di condividere materiali e piani?
10. **Aspetto:** stile visivo definitivo, tema, densità e modalità di navigazione.

Queste decisioni devono essere prese prima di fissare i vincoli tecnici, perché influenzano struttura, costi, manutenzione, privacy e modalità di distribuzione.

## 23. Piano di realizzazione per fasi

Le fasi devono produrre risultati autonomi, verificabili e utilizzabili. Non è necessario attendere il completamento dell'intero prodotto per ottenere una prima versione utile.

### Fase 1 — Fondamenta universitarie

- profilo studente;
- materie;
- esami e tentativi;
- voti;
- crediti;
- calcolo delle medie;
- dashboard;
- obiettivo laurea;
- salvataggio, recupero e backup di base.

### Fase 2 — Organizzazione

- calendario;
- sessioni d'esame;
- importazione di calendari;
- gestione degli impegni;
- pianificatore;
- disponibilità settimanale e giornaliera.

### Fase 3 — Studio Lab

- Focus Room;
- Pomodoro;
- task;
- missioni;
- registro attività;
- flashcard;
- diario degli errori;
- distrazioni;
- check-in personale.

### Fase 4 — Apprendimento

- Syllabus Tracker;
- programmi e argomenti;
- ripassi programmati;
- banca delle domande;
- esercitazioni e simulazioni;
- analisi della preparazione.

### Fase 5 — Materiali e integrazioni

- collegamento autorizzato a Moodle;
- sincronizzazione dei corsi;
- download dei materiali;
- libreria documenti;
- ricerca nei documenti;
- centro notifiche.

### Fase 6 — Analisi

- progressi;
- statistiche;
- andamento per materia;
- riepilogo settimanale;
- report ed esportazioni.

### Fase 7 — Maturità e rilascio

- backup e ripristino completi;
- privacy e cancellazione;
- accessibilità;
- prestazioni;
- gestione degli errori;
- test di regressione;
- distribuzione e aggiornamenti.

### Fase 8 — Estensioni facoltative

Valutare soltanto dopo la stabilizzazione del nucleo principale le estensioni che richiedono nuove scelte di prodotto, nuove fonti o servizi esterni. Ogni estensione deve avere una propria valutazione di valore, costo, privacy e manutenzione.

## 24. Criteri di completamento del prodotto

Una funzione può essere considerata completa soltanto quando:

- risolve un caso d'uso reale;
- usa dati effettivi e non esempi dimostrativi;
- è collegata alle altre aree interessate;
- gestisce errori e dati mancanti;
- conserva correttamente le informazioni;
- non genera duplicati;
- è utilizzabile da tastiera e con le impostazioni di accessibilità previste;
- dispone di verifiche automatiche sui casi normali e sui casi limite;
- è documentata con limiti e assunzioni;
- è stata provata nel contesto di utilizzo previsto.

Una funzione pianificata, una schermata dimostrativa o un servizio non collegato al flusso reale non devono essere presentati come completati.

## 25. Risultato finale atteso

UniTo StudyHub deve essere un ambiente universitario personale nel quale carriera, calendario, studio, materiali e progressi si sostengono a vicenda.

Lo studente deve poter sapere cosa ha fatto, cosa deve fare, quanto lavoro rimane e quali informazioni sono ufficiali o soltanto indicative.

Il valore del prodotto non dipende da una tecnologia specifica, dalla quantità di schermate o dal numero di funzioni. Dipende dalla coerenza dei dati, dalla qualità dell'esperienza, dalla protezione della vita personale e dalla sua utilità concreta durante tutto il percorso universitario.
