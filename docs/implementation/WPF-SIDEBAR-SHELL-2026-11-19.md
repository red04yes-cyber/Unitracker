# Shell WPF: sidebar collassabile e host contenuti — 19 novembre 2026

## Obiettivo

Semplificare la navigazione primaria dopo il redesign WPF, riducendo la logica visuale del TabControl usato come sidebar e rendendo la barra comprimibile per schermi laptop.

## Implementazione

- Presentation/Controls/StudyHubSidebar.xaml è il controllo visuale della sidebar: ItemsControl con pulsanti WPF reali, larghezza espansa 236 px e larghezza compatta 80 px.
- IsCollapsed è una DependencyProperty collegata al toggle. In modalità compatta restano visibili le icone; nome e descrizione sono disponibili tramite tooltip e AutomationProperties.
- La voce selezionata usa AccentSoftBrush e una barra verticale accent di 3 px. Il focus da tastiera evidenzia il bordo del pulsante e del toggle.
- La navigazione non usa storyboard o animazioni: la selezione è immediata (0 ms) e non usa BackEase, overlay o glow.
- SidebarNavigationCatalog mantiene in un unico punto l'ordine e gli indici delle pagine. Le sei aree primarie sono seguite da Profilo e Impostazioni come destinazioni secondarie.
- La selezione è sincronizzata in modo bidirezionale con il selettore di pagine. Il controllo usa SetCurrentValue per non rimuovere il binding quando un pulsante aggiorna l'indice.
- Il contenuto visibile è ospitato da ContentControl (PageContentHost). Dopo InitializeComponent, il code-behind trasferisce gli otto contenuti dei TabItem in un dizionario per istanza e svuota i TabItem; il TabControl resta nascosto con template vuoto e viene usato solo come registro di selezione. Solo PageContentHost possiede e visualizza la pagina selezionata, mentre il dizionario conserva le istanze non selezionate.
- Il collegamento topbar “Impostazioni” ora seleziona la pagina Impostazioni (indice 7); la pagina Profilo resta raggiungibile dalla sidebar (indice 6).
- Finestra iniziale 1360 × 860 px e minimo 980 × 680 px, allineati alla specifica prodotto.

## Mappa delle pagine

| Indice | Voce |
|---:|---|
| 0 | Oggi |
| 1 | Materie |
| 2 | Calendario |
| 3 | Studio Lab |
| 4 | Accademia |
| 5 | Progressi |
| 6 | Profilo |
| 7 | Impostazioni |

Gli indici sono attualmente un contratto di compatibilità con gli otto TabItem esistenti. Il test NavigationCatalogKeepsAllPageRoutesStableAndUnique impedisce buchi o riordini accidentali.

## Verifica e limiti

- Test statici/unitari aggiunti per catalogo, notifica PropertyChanged, selezione, binding, collasso, focus/accessibilità, risorse, assenza di animazioni e routing Impostazioni.
- Il test host non carica WPF: XAML e code-behind del controllo sono inclusi come risorse per verifiche strutturali, mentre il catalogo C# è compilato nel test project.
- Il toggle di collasso è in memoria e non viene persistito tra riavvii; la specifica richiede la possibilità di collassare, non una preferenza persistente.
- Le pagine esistenti non sono state ancora estratte in UserControl separati o in una route factory tipizzata. Il TabControl nascosto è un adattatore temporaneo: un successivo refactoring può rimuovere l'indice numerico spostando ogni pagina in una view dedicata.
- Le animazioni indipendenti della vista calendario non fanno parte della navigazione della sidebar e sono rimaste invariate.
- Lo smoke test GitHub Windows verifica l'avvio WPF, ma non sostituisce una prova manuale di click, cambio pagina e DPI su un laptop reale.
- Nessuna modifica a servizi, dati persistiti, database, dipendenze o funzionalità AI.
