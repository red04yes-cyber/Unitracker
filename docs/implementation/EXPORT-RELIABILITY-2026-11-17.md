# Export affidabile e confini temporali — 17 novembre 2026

## Ambito

Questa slice corregge casi limite dell'esportazione CSV/XLSX già esistente. Non introduce un nuovo formato, una dipendenza, una chiamata di rete o una seconda fonte dati.

## Modifiche

- Export rifiuta una richiesta nulla con ArgumentNullException prima di accedere ai filtri.
- Gli intervalli che terminano a DateOnly.MaxValue vengono rifiutati esplicitamente: non è rappresentabile il confine esclusivo del giorno successivo.
- Il confine iniziale di una data locale gestisce i fusi che saltano la mezzanotte (primo minuto locale valido) e quelli che la ripetono (offset massimo, quindi istante UTC più anticipato).
- Il controllo formula CSV considera anche i caratteri Unicode della categoria Format come prefissi invisibili, oltre a whitespace e controlli.
- Le stringhe XLSX vengono filtrate secondo i caratteri ammessi da XML 1.0. I caratteri di controllo vietati vengono rimossi; sequenze UTF-16 isolate sono convertite nel carattere sostitutivo durante l'enumerazione Unicode.
- Prima di creare lo ZIP XLSX, ogni cella testuale viene verificata rispetto al limite Excel di 32.767 unità di codice UTF-16. Un valore oltre il limite produce un errore esplicito invece di un file che Excel potrebbe riparare o rifiutare.

## Verifiche aggiunte

I test coprono prefissi invisibili in CSV, XML 1.0 non valido, Unicode supplementare e surrogati isolati, limite cella esatto e superato, richiesta nulla, date massime e transizioni DST a mezzanotte ambigua o inesistente.

## Limiti

- Non è stato introdotto un limite globale ai record CSV/JSON: questi formati non hanno il limite di righe del foglio di calcolo e un limite arbitrario potrebbe impedire l'esportazione di archivi legittimi.
- Il limite di 32.767 caratteri si applica solo alle celle XLSX; CSV e JSON conservano il testo originale.
- La scansione dei caratteri XLSX avviene dopo la selezione/materializzazione delle righe. Non è un limite di memoria per l'intero export.
- Le transizioni storiche dei fusi dipendono dal database timezone del sistema operativo; i test Windows CI verificano i casi IANA usati dalle fixture.

## Verifica

La build/test Windows .NET 10 e il security workflow sono demandati ai workflow esistenti sulla branch dedicata. Non viene effettuato merge automatico.