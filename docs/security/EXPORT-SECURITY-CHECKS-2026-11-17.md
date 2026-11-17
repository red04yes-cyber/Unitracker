# Audit mirato sicurezza export — 17 novembre 2026

## Perimetro e metodo

Revisione source-level di StudyDataExportService, dei test ExportAndImportSafetyTests e dei workflow Windows/security esistenti. Non è un penetration test e non comprende file personali reali.

**Esito:** 36 controlli mirati. Sette difetti/limiti sono stati corretti in questa branch; gli altri controlli risultano già coperti dal codice o dai test esistenti. Nessuna modifica alle funzionalità AI.

## Registro dei controlli

| # | Controllo | Esito |
|---:|---|---|
| 1 | Rifiuto di StudyData nullo | OK |
| 2 | Rifiuto di richiesta export nulla prima della selezione | Corretto |
| 3 | Rifiuto intervallo con data finale precedente | OK |
| 4 | Limite massimo di 3.661 giorni | OK |
| 5 | Overflow del confine esclusivo dopo DateOnly.MaxValue | Corretto |
| 6 | Risoluzione del fuso orario richiesto | OK |
| 7 | Errore controllato per fuso non trovato/non valido | OK |
| 8 | Mezzanotte locale inesistente (DST gap) | Corretto |
| 9 | Mezzanotte locale ambigua (DST overlap) | Corretto |
| 10 | Intervallo con fine esclusiva del giorno successivo | OK |
| 11 | Selezione per sovrapposizione temporale | OK |
| 12 | Filtro corso con confronto ordinal | OK |
| 13 | Filtro corso vuoto interpretato come assenza di filtro | OK |
| 14 | Filtro per tipo attività | OK |
| 15 | Esclusione delle attività eliminate per default | OK |
| 16 | Inclusione delle attività eliminate solo su opt-in | OK |
| 17 | Sessione collegata inesistente non aggiunta | OK |
| 18 | Sessione collegata di un altro corso esclusa | OK |
| 19 | Sessione collegata fuori intervallo esclusa | OK |
| 20 | Esclusione dei cicli Pomodoro su richiesta | OK |
| 21 | Note private delle attività omesse per default | OK |
| 22 | Note private delle sessioni omesse per default | OK |
| 23 | IncludeNotes=false applicato ai due tipi di record | OK |
| 24 | Token/chiavi non letti dal servizio di export StudyData | OK |
| 25 | Prefissi formula CSV =, +, - e @ protetti | OK |
| 26 | Prefissi whitespace e caratteri di controllo considerati | OK |
| 27 | Prefissi Unicode invisibili di categoria Format | Corretto |
| 28 | Quote CSV raddoppiate e campi testuali quotati | OK |
| 29 | BOM UTF-8 e formattazione numerica/date invariant | OK |
| 30 | Celle XLSX testuali serializzate come inline strings | OK |
| 31 | Nessun elemento formula XLSX generato per testo utente | OK |
| 32 | Escape dei caratteri XML riservati | OK |
| 33 | Rimozione dei code point vietati da XML 1.0 | Corretto |
| 34 | Gestione di sequenze UTF-16 isolate | Corretto |
| 35 | Limite Excel di 32.767 code point per cella | Corretto |
| 36 | Escape HTML predefinito del serializer JSON | OK |

## Correzioni effettive

1. Guardia esplicita per richiesta nulla.
2. Rifiuto dell'ultima data rappresentabile quando non è possibile costruire il confine esclusivo successivo.
3. Risoluzione deterministica della mezzanotte ambigua scegliendo l'offset più grande.
4. Avanzamento al primo minuto locale valido quando la mezzanotte è inesistente.
5. Rilevamento dei prefissi formula preceduti da caratteri Unicode Format.
6. Filtraggio dei code point non ammessi da XML 1.0 e normalizzazione dei surrogati isolati.
7. Rifiuto esplicito di celle XLSX oltre il limite documentato.

I controlli 8–9, 27, 33–35 sono coperti anche da test nuovi. I controlli già presenti non hanno richiesto modifiche.

## Limiti residui

- Il limite per cella non costituisce un tetto alla memoria totale dell'esportazione.
- Non è stato aggiunto un cap arbitrario per CSV/JSON.
- Il test DST dipende dalle regole timezone del runner Windows; l'esito CI sarà riportato separatamente.