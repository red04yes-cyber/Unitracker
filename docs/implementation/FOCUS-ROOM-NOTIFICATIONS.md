# Focus Room, Pomodoro e centro notifiche

## Ambito

Questa slice è derivata dalla UI dark violet della PR #76 su un branch separato.
Non modifica la PR #76 e non effettua merge.

## Pomodoro

- La schermata Studio Lab usa il `PomodoroService` persistente già presente, non un secondo countdown locale.
- Le scadenze assolute vengono riconciliate dal timer UI ogni secondo; le transizioni effettive vengono salvate tramite `IStudyDataStore`.
- Focus, pausa breve e pausa lunga sono fasi dello stesso ciclo; la pausa lunga viene scelta dalla configurazione del servizio dopo quattro focus completati.
- Dopo una pausa completata viene mostrato il comando esplicito per avviare il focus successivo.
- I preset esistenti impostano durata focus e pausa breve; la pausa lunga è configurata a tre volte la pausa breve.
- Le sessioni focus legacy prive di un record Pomodoro continuano a essere visualizzate e gestite tramite `FocusSessionService`.

## Musica Focus Room

Playlist iniziale: **Now or Never** di Purrple Cat (8 tracce MP3).
La musica viene riprodotta in streaming da OpenGameArt; è necessaria una connessione Internet.
La pagina dell'album indica la licenza **Creative Commons Attribution 4.0 International (CC BY 4.0)**. L'interfaccia mostra l'attribuzione a Purrple Cat e un collegamento alla licenza.
Controlli: selezione traccia, pausa, ripresa, stop, traccia successiva e volume.
Le tracce non sono presentate come public domain: CC BY 4.0 consente il riutilizzo con attribuzione.

Fonti:
- Album e file MP3: https://opengameart.org/content/purrple-cat-now-or-never-lofi-album
- Licenza: https://creativecommons.org/licenses/by/4.0/
- Profilo autore: https://opengameart.org/users/purrplecat

## Centro notifiche

`NotificationCenterService` genera promemoria locali dai dati esistenti:
- eventi di calendario non annullati che iniziano entro 24 ore e non sono già terminati;
- attività non completate con scadenza entro 24 ore, incluse quelle già scadute;
- transizioni Pomodoro osservate mentre l'app è aperta.

Il centro non crea eventi o scadenze fittizi. Gli avvisi Pomodoro arretrati durante la chiusura dell'app non vengono riprodotti come notifiche nuove. Il comando "Segna tutti come letti" li nasconde per la sessione corrente; lo stato letto/non letto non è ancora persistito e non vengono inviate notifiche push/Windows quando l'app è chiusa.

## Verifiche

La PR resta draft mentre vengono controllati i workflow e la compatibilità WPF.

- Aggiunti test unitari per filtro delle scadenze, eventi annullati, attività completate e finestra temporale configurabile.
- Build WPF, test completi e riproduzione audio devono essere verificati tramite CI Windows e test runtime su Windows dopo l'apertura della PR.
- Non dichiarare il supporto audio offline: i file sono riprodotti da URL HTTPS esterni.
