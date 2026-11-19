# Controlli sicurezza UI — shell WPF sidebar — 19 novembre 2026

## Perimetro e metodo

Revisione source-level della sola shell e del controllo StudyHubSidebar, con test statici xUnit sul catalogo e sul markup. Non è un penetration test né una verifica runtime dell'albero visuale su Windows.

## Registro dei controlli

| # | Area / componente | Rischio e metodo | Esito | Evidenza |
|---:|---|---|---|---|
| 1 | Catalogo rotte | Modifica di destinazioni via input esterno; ispezione del catalogo statico | Superato | SidebarNavigationCatalog.cs: voci compilate nel binario |
| 2 | Indici pagine | Indici duplicati o fuori sequenza; test xUnit | Superato | NavigationCatalogKeepsAllPageRoutesStableAndUnique |
| 3 | Click navigazione | Tag arbitrari o tipo inatteso; controllo del pattern di matching | Superato | NavigationItem_Click accetta solo SidebarNavigationItem |
| 4 | Binding selezione | Perdita del binding two-way alla selezione; test statico e revisione API DP | Superato | SetCurrentValue(SelectedIndexProperty, item.PageIndex) |
| 5 | Navigazione URI / Frame | Apertura di destinazioni arbitrarie; ricerca delle API nel controllo | Non applicabile | Nessun Frame, Navigate o binding URI |
| 6 | Processi / shell | Avvio di processi o comandi tramite voce di menu; ispezione code-behind | Superato | Il gestore modifica solo l'indice selezionato |
| 7 | Rete | Richieste HTTP o trasferimento di dati dalla shell; ispezione dei file aggiunti | Non applicabile | Il controllo non contiene client di rete |
| 8 | Credenziali e token | Esposizione di segreti in tooltip, binding o log; revisione proprietà bindate | Non applicabile | Il catalogo contiene solo titolo, descrizione, glyph e indice |
| 9 | Persistenza | Scritture database o modifiche ai dati utente dalla navigazione; ispezione code-behind | Non applicabile | Nessun servizio o store richiamato |
| 10 | File system | Path arbitrari o accesso a file dal controllo; ispezione code-behind | Non applicabile | Nessuna API filesystem |
| 11 | Import / export | Azioni di import o export involontarie da pulsanti di navigazione | Non applicabile | Le voci non espongono comandi di import/export |
| 12 | Deserializzazione | XAML o dati dinamici deserializzati da input non attendibile | Superato | Markup dichiarativo statico, nessun loader dinamico |
| 13 | Host contenuti | Visualizzazione di contenuto diverso dalla pagina selezionata; verifica binding | Superato | PageContentHost usa solo MainTabs.SelectedContent |
| 14 | Registro pagine | Doppio presenter visuale o interazione con il registro nascosto; test XAML | Superato | Template vuoto e nessun ContentPresenter nel template del registro |
| 15 | Accessibilità pulsanti | Controlli senza nome leggibile dai lettori di schermo; test markup | Superato | AutomationProperties.Name e HelpText derivati dal catalogo |
| 16 | Modalità compatta | Perdita della destinazione quando i testi sono nascosti; revisione/test XAML | Superato | Tooltip e AutomationProperties restano sul Button |
| 17 | Focus tastiera | Navigazione da tastiera senza indicatore; revisione template | Superato | Trigger IsKeyboardFocused sul bordo di Button e ToggleButton |
| 18 | Collasso layout | La larghezza locale impedisce l'attivazione dello stile; test markup | Superato | Setter stile 236 px, trigger IsCollapsed 80 px, MinWidth 80 |
| 19 | Animazioni / effetti | Effetti e animazioni della vecchia shell persistono; test markup | Superato | Nessun Storyboard, DoubleAnimation, BackEase o DropShadowEffect nel controllo/sidebar |
| 20 | Handler XAML | Click indirizzato a handler inesistente; test regex su code-behind embed | Superato | SettingsShortcutTargetsTheSettingsPageAndSidebarClickUsesCatalogPageIndex |
| 21 | Risorse WPF | Chiavi static/dynamic non dichiarate; test delle risorse | Superato | SidebarResourceReferencesResolveAgainstApplicationAndControlResources |
| 22 | Routing Impostazioni | Apertura della pagina Profilo al posto di Impostazioni; test code-behind | Superato | NavigateToSettings_Click seleziona indice 7 |
| 23 | Pagine nascoste | Pagina Profilo non selezionabile per Visibility=Collapsed; test pagine registrate | Superato | Gli otto TabItem restano selezionabili |
| 24 | Drag/drop e input | Handler di trascinamento o input raw aggiunti alla shell | Non applicabile | Nessun handler drag/drop o input libero |
| 25 | Dipendenze | Aumento superficie supply chain; confronto progetto | Superato | Nessuna dipendenza NuGet aggiunta |
| 26 | Workflow CI | Permessi GitHub o secrets esposti; confronto workflow base | Superato | Nessuna modifica ai workflow o ai permessi |
| 27 | Dati personali | Telemetria o contenuto personale nei metadati di navigazione | Superato | Solo stringhe statiche e indici |
| 28 | Eccezioni / logging | Errori o dati di binding scritti in log dedicati; revisione code-behind | Superato | Nessun logger o catch che registra dati |
| 29 | Tema / risorse | Colori o markup provenienti da input runtime; revisione riferimenti | Superato | Risorse WPF note, nessun caricamento dinamico |
| 30 | Concorrenza UI | Modifiche cross-thread alla selezione; revisione dei callback | Superato | Gestori WPF sincroni sul dispatcher UI; nessun task/thread creato |

## Riepilogo

- 30 controlli documentati: 23 superati e 7 non applicabili.
- Problemi di sicurezza effettivi rilevati: nessuno.
- Correzioni di vulnerabilità di sicurezza: nessuna necessaria; la revisione ha confermato che questa slice non aggiunge superfici di rete, persistenza, filesystem o gestione segreti.
- Guardrail introdotti: selezione tipizzata, preservazione del binding two-way, automation names, focus visibile e test di risorse XAML.
- Limiti: analisi statica; non dimostra il comportamento WPF runtime né la sicurezza contro codice malevolo eseguito nello stesso processo utente.
