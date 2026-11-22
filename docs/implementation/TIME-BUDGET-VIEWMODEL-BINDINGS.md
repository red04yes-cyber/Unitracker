# Binding del budget di studio

## Scopo

`TimeBudgetService` è registrato nel composition root, ma il risultato non era esposto dal workspace WPF. `StudyWorkspaceViewModel` ora pubblica un contratto di sola lettura per collegare il budget alla UI senza spostare regole di calcolo nel ViewModel.

## Proprietà disponibili

- `CurrentTimeBudgetPlan`: riepilogo prodotto dal servizio (`WeeklyMinutes`, `AvailableMinutes`, `RequiredMinutes`, `UnallocatedMinutes`, `IsOverCapacity` e avvisi).
- `TimeBudgetAllocations`: righe pronte per il binding, una per esame. Usare questa raccolta per i dettagli per-esame.
- `TimeBudgetWarnings`: avvisi originali del servizio.
- `TimeBudgetStatus`: sintesi leggibile che distingue budget sufficiente, sovraccarico e stime mancanti.
- `HasTimeBudgetAllocations`, `HasTimeBudgetWarnings`, `HasTimeBudgetUnknownEstimates`, `TimeBudgetUnknownEstimateCount`, `TimeBudgetAtRiskCount` e `IsTimeBudgetOverCapacity`: indicatori per stati vuoti e riepiloghi.
- `TimeBudgetCapacityDisplay`, `TimeBudgetRequiredDisplay` e `TimeBudgetUnallocatedDisplay`: valori già formattati per i binding testuali.

## Riga per-esame e dati mancanti

`TimeBudgetAllocationRow` proietta corso, scadenza, minuti stimati/allocati, settimane disponibili, rischio e spiegazione.

`HasEstimate` è `false` quando i minuti stimati sono zero o negativi. In quel caso la spiegazione della riga dichiara che la copertura non è valutabile: una stima assente non viene presentata come un esame coperto da zero minuti.

Per i dettagli per-esame, la UI dovrebbe quindi usare `TimeBudgetAllocations` e non `CurrentTimeBudgetPlan.Allocations`, che resta il risultato grezzo del servizio.

## Aggiornamento

Il workspace ricalcola il budget:
- quando riceve `Initialize(StudyData)` all'avvio o dopo un ripristino;
- dopo il salvataggio del profilo accademico e dei limiti settimanali/giornalieri;
- quando il ViewModel principale aggiorna il riepilogo degli esami dopo la pianificazione o il ricaricamento dei dati.

Se un altro caso d'uso modifica direttamente esami o limiti, deve richiamare `RefreshTimeBudget()` dopo aver aggiornato i dati.

## Confini

- Il budget è una stima indicativa, non un appello ufficiale né una promessa di disponibilità nel calendario.
- Non crea task, sessioni o eventi e non salva allocazioni.
- Il ViewModel proietta il risultato: formule, priorità e capacità restano responsabilità di `TimeBudgetService`.
- Non sono stati modificati XAML, temi, navigazione o layout. Il proprietario della UI può collegare le proprietà senza duplicare la logica applicativa.
