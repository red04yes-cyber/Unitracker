# ADR-0001 — WPF, MVVM e dependency injection

- **Data:** 2026-11-20
- **Stato:** Accepted — documenta la baseline già implementata su `main`
- **Ambito:** composizione dell'applicazione e separazione della presentazione

## Contesto

UniTo StudyHub è un'applicazione WPF su .NET 10. La UI deve poter cambiare senza spostare le regole accademiche, di calendario o di studio nel code-behind. Anche i servizi core devono poter essere risolti nei test senza costruire una finestra WPF.

## Decisione

- `App.OnStartup` è il composition root del processo desktop.
- `CompositionRoot.AddUniToStudyHubCore` registra database e servizi core.
- `PresentationServiceRegistration.AddUniToStudyHubViewModels` registra separatamente i ViewModel.
- `MainViewModel` e `StudyWorkspaceViewModel` espongono stato e comandi per il binding; i casi d'uso e le regole restano nei servizi e nel dominio.
- I comandi asincroni usano `AsyncRelayCommand`; i servizi non devono dipendere da controlli WPF.

## Alternative considerate

- **Business logic nel code-behind:** rifiutata perché accoppia regole e UI e rende più difficile il test.
- **Service locator statico:** rifiutato perché nasconde le dipendenze e complica l'isolamento dei test.
- **Un unico contenitore che registra anche la finestra nel core:** rifiutato; la registrazione della presentazione resta separata.

## Conseguenze

- I nuovi servizi vanno registrati nel composition root core e testati senza creare la UI.
- I ViewModel possono orchestrare comandi e stato, ma non devono diventare motori di business logic.
- I binding pubblici esistenti sono contratti: modificarli richiede aggiornamento coordinato della UI e dei test.
- Una feature che richiede una nuova scelta visuale deve dichiarare i binding necessari e non simulare una UI completa con soli servizi.

## Evidenze

- `src/UniToStudyHub/App.xaml.cs`
- `src/UniToStudyHub/Infrastructure/CompositionRoot.cs`
- `src/UniToStudyHub/Presentation/Composition/PresentationServiceRegistration.cs`
- `src/UniToStudyHub/Presentation/ViewModels/MainViewModel.cs`
- `src/UniToStudyHub/Presentation/ViewModels/StudyWorkspaceViewModel.cs`
- `src/UniToStudyHub/Presentation/Commands/AsyncRelayCommand.cs`
