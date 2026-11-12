# UI slice — dashboard dark violet

## Direzione approvata

- Tema scuro come impostazione visiva predefinita.
- Accento violetto/indaco.
- Animazioni ricche ma brevi: ingresso delle card, feedback hover e micro-transizioni.
- WPF/MVVM invariati; nessuna dipendenza UI aggiunta.

## Modifiche

### Design system — `src/UniToStudyHub/App.xaml`

- Passaggio del dizionario WPF-UI al tema Dark.
- Aggiornamento delle risorse semantiche: pagina, superfici, sidebar, testo, bordi, accento, stati success/warning e AI.
- Aggiunta di `AnimatedCardStyle`: fade-in all'ingresso e lieve scale-in su hover, con ritorno animato all'uscita.
- I colori restano centralizzati in risorse condivise per evitare valori incoerenti nelle schermate.

### Dashboard — `src/UniToStudyHub/MainWindow.xaml`

- Nuova gerarchia: intestazione, hero, prossimo esame, KPI carriera e riepilogo recente.
- Hero violetto scuro con elemento grafico decorativo, senza dati fittizi.
- Card KPI basate esclusivamente sui binding già esistenti.
- Etichetta esplicita che ricorda che le date generate sono indicative.
- Azioni principali collegate alla navigazione esistente verso Materie.
- Le altre sezioni e i relativi binding sono lasciati invariati in questa prima slice.

## Vincoli rispettati

- Nessuna modifica a Domain, Application, servizi, modelli o persistenza.
- Nessuna modifica a `MainWindow.xaml.cs` o ai ViewModel.
- Nessuna dipendenza NuGet aggiunta.
- Nessun dato demo o metrica inventata.
- La PR storica #35 è chiusa e superseded; questa slice parte dalla linea canonica `main`, non riapre né sovrascrive quella branch.

## Verifiche

- Controllati i binding della dashboard rispetto a `MainViewModel`.
- Controllate le risorse colore e l'ordine di dichiarazione degli stili.
- Controllato che la dashboard usi solo handler già esistenti.
- Build WPF e verifica visuale su Windows ancora da eseguire in CI/runtime. Le animazioni e il tema vanno controllati su Windows prima del merge.

## Prossima slice suggerita

Estendere la stessa identità visiva alla shell (sidebar/topbar), quindi Materie e Calendario, mantenendo la compatibilità con i binding e con i flussi già implementati.
