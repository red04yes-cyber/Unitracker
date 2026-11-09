# Riepilogo della preparazione agli esami

## Scopo

`ExamReadinessService.CalculateOverview` fornisce una vista aggregata e in sola
lettura della preparazione registrata per le materie non archiviate. Non modifica
`StudyData`, non introduce una seconda persistenza e non richiede migrazioni.

## Regole

- Le materie con `Archived = true` o stato `Archived` sono escluse.
- Le materie con evidenze disponibili sono ordinate dal punteggio più basso al
  più alto; quelle senza evidenze sono elencate alla fine.
- Un punteggio parziale resta distinto da una valutazione completa tramite
  `IsComplete` e `MissingData`.
- `AverageAvailableScore` è la media semplice dei punteggi disponibili.
  Include i punteggi parziali e non sostituisce i relativi indicatori di dati
  mancanti.
- `CfuWeightedAvailableScore` è la media ponderata per CFU dei soli corsi con
  punteggio disponibile e CFU positivi. È null quando non esiste un denominatore
  valido.
- I punteggi aggregati sono indicatori organizzativi basati sui dati locali:
  non sono voti, previsioni ufficiali né garanzie di superamento.
- `EstimatedRemainingSyllabusMinutes` somma le stime residue fornite dai
  syllabus registrati; non deduce tempi per argomenti privi di stima.

## Verifica

`ExamReadinessOverviewTests` verifica ordinamento, esclusione delle materie
archiviate, punteggi parziali e completi, medie semplici e ponderate, assenza di
dati, CFU non positivi e assenza di mutazioni nello snapshot.

La verifica completa deve essere confermata dai workflow GitHub Actions Windows
(build, test, publish e smoke test) e dai controlli di sicurezza della PR.
