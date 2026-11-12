# UI slice — dashboard, shell, materie, calendario e profilo dark violet

## Direzione approvata

- Tema scuro come impostazione visiva predefinita.
- Accento violetto/indaco.
- Animazioni ricche ma brevi: ingresso delle card, micro-scale hover e transizioni di navigazione/calendario.
- WPF/MVVM invariati; nessuna dipendenza UI aggiunta.

## Modifiche

### Design system — `src/UniToStudyHub/App.xaml`

- Tema WPF-UI Dark e palette semantica violet/dark.
- Stile condiviso `AnimatedCardStyle`: fade/scale-in leggero all'ingresso, scale hover e ritorno animato.
- Pulsanti con micro-feedback hover e pressed.
- Palette centralizzata per mantenere coerenza tra schermate.

### Shell, sidebar e topbar — `src/UniToStudyHub/MainWindow.xaml`

- Sidebar più leggibile, con indicatore laterale violetto per la sezione attiva e superfici hover discrete.
- Header con gerarchia tipografica più chiara e stato Moodle contenuto in una pill con troncamento del testo lungo.
- Transizione breve di dissolvenza e scorrimento verticale quando cambia la sezione.
- Ripristinato il focus visuale predefinito per la navigazione da tastiera; non viene più disabilitato sul TabItem.

### Materie

- Filtri disposti in un WrapPanel per adattarsi meglio a larghezze diverse.
- Campo di ricerca con nome accessibile esplicito e tooltip descrittivo.
- Card materia con ingresso animato e feedback hover, mantenendo nome, codice, CFU e stato esistenti.
- Pannelli di aggiunta e registrazione tentativo integrati nello stesso linguaggio visivo.
- Ricerca, filtri e conteggi continuano a usare i binding e gli handler già presenti.

### Calendario

- Transizione breve di fade/slide quando cambia la modalità mese, settimana o giorno.
- Card dei giorni e delle viste settimanale/giornaliera con animazioni coerenti.
- Corretto il fondo bianco hard-coded delle celle mensili, ora coerente con il tema scuro.
- Rimangono invariati eventi, selezione, navigazione temporale, provenienza e stato di verifica.

### Progressi e animazioni

- KPI e pannelli di andamento usano lo stile animato condiviso.
- Le barre di progresso continuano a rappresentare i valori reali già forniti dai ViewModel; nessun valore o grafico sintetico aggiunto.
- Le animazioni sono brevi e decorative: non bloccano input né modificano la logica.


### Profilo studente — `src/UniToStudyHub/MainWindow.xaml`

- Aggiunto un pulsante avatar circolare nella parte alta della sidebar; usa un glifo vettoriale di sistema come avatar predefinito e apre la pagina interna.
- Aggiunta la pagina interna “Il mio profilo”, con ingresso animato coerente con le altre sezioni.
- Riepilogo carriera collegato a valori già esposti dal ViewModel: CFU acquisiti, medie, numero insegnamenti, sessioni e attività di studio.
- Informazioni universitarie e obiettivi settimanali riutilizzano i campi e il comando di salvataggio del profilo accademico già esistenti.
- Elenco insegnamenti basato sulla raccolta già esposta dall'app.
- Privacy: riutilizzati gli handler esistenti per esportazione JSON e cancellazione completa, con conferma; l'opzione di includere i backup condivide il controllo già presente nelle impostazioni.
- Per mantenere invariati gli indici usati dalla navigazione esistente, la pagina profilo occupa l'indice 6 e la pagina Impostazioni passa all'indice 7; il pulsante superiore e quello avatar usano l'handler di navigazione già presente.
- Limiti espliciti: nome/cognome, avatar selezionabili, insegnamenti preferiti, obiettivo di media e target annuale di CFU non sono attualmente esposti da modello/ViewModel e non vengono simulati. La pagina mostra invece i dati effettivamente disponibili e i campi accademici già persistiti.

## Vincoli rispettati

- Intervento autorizzato dall'utente come eccezione UI/UX limitata alle aree richieste, nonostante i divieti UI presenti in `AGENTS.md` e nel brief tecnico.
- Nessuna modifica a `MainWindow.xaml.cs`, ViewModel, Domain, Application, servizi, modelli o persistenza. La navigazione del profilo riutilizza l'handler XAML già esistente.
- Nessuna dipendenza NuGet aggiunta.
- Nessun dato demo, metrica inventata o binding applicativo nuovo.
- La PR storica #35 è chiusa/superseded; questa slice resta sulla branch `ui/dashboard-dark-violet` e sulla PR #76.

## Verifiche

- Controllati i binding esistenti per materie e calendario e mantenute le relative sorgenti.
- Controllati i riferimenti alle risorse e agli handler già presenti.
- Build WPF e test XAML da eseguire tramite CI Windows dopo questo aggiornamento, inclusa la verifica del binding dell'avatar alla pagina profilo.
- La verifica visuale e l'interazione runtime su Windows restano necessarie: una build CI positiva non equivale a un test UX.
