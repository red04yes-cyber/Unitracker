# Riepiloghi giornalieri del registro attività

## Problema

`ProgressService.Calculate` costruiva le metriche giornaliere richiamando `ActivityLedgerService.Summarize` una volta per ogni data. Ogni chiamata ricostruiva gli intervalli delle attività e percorreva nuovamente il registro. Per una finestra di 90 giorni il costo di questa parte cresceva quindi con il prodotto tra giorni e attività.

## Modifica

`ActivityLedgerService.SummarizeByDay` ricostruisce gli intervalli una sola volta, li limita alla finestra richiesta e divide ciascun intervallo sui confini dei giorni civili nel fuso orario selezionato. Per ogni giorno aggrega gli intervalli con le stesse regole già usate dal registro:

- i secondi registrati sono l'unione degli intervalli, quindi lo stesso minuto non viene conteggiato due volte;
- i secondi sovrapposti indicano il tempo coperto da almeno due attività;
- gli intervalli che attraversano la mezzanotte vengono divisi, senza perdere secondi;
- i confini giornalieri sono calcolati con il fuso orario, quindi le giornate di 23 o 25 ore mantengono la durata reale.

`ProgressService.Calculate` continua a produrre il riepilogo complessivo e ora usa il risultato giornaliero aggregato, invece di richiedere un riepilogo completo per ogni giorno. I giorni senza attività rimangono presenti nel report con valori pari a zero.

## Verifica

I test di regressione coprono:

- un'attività che attraversa la mezzanotte e si sovrappone a una seconda attività, con verifica dei secondi unici e sovrapposti su entrambe le date;
- il consumo reale di 2 ore durante il salto primaverile di Europe/Rome e di 1 ora nell'ora ripetuta autunnale;
- il rifiuto di intervalli di date invertiti;
- l'integrazione con le metriche giornaliere di `ProgressService`.

Non è stato introdotto un benchmark sintetico: la modifica riduce il numero di scansioni del registro nella costruzione delle metriche giornaliere, ma il guadagno in millisecondi non è stato misurato su dataset reali.

## Ambito e limiti

Nessuna modifica a XAML, database, schema, migrazioni, dipendenze, servizi remoti o componenti AI. La logica di unione e sovrapposizione resta condivisa con il registro esistente; questa slice non modifica la definizione delle metriche di studio né il modo in cui vengono attribuite le sessioni ai giorni.
