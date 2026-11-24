# Esportazione completa dei dati di studio

## Contratti e integrazione

StudyDataExportService espone due percorsi distinti:
- Export: report di progresso compatto già usato da StudyWorkspaceViewModel.ExportProgressAsync. Mantiene attività, sessioni, segmenti, riepilogo e cicli Pomodoro; non include automaticamente materiali, note, voti o check-in.
- ExportStudyData: esportazione completa e intenzionale dei dati di studio. Imposta IncludeStudyLabRecords e produce le raccolte descritte sotto.

La UI attuale resta invariata. Il controllo dedicato "Esporta dati di studio" deve chiamare ExportStudyData solo dopo un'azione esplicita dell'utente e deve esporre le opzioni di privacy/filtro di StudyExportRequest. Non riutilizzare il pulsante "Esporta report XLSX", che continua a generare il report compatto. Nessun XAML è stato modificato in questa slice.

## Formati

ExportStudyData produce:
- un bundle di file CSV UTF-8 con BOM, uno per raccolta;
- un file XLSX con fogli separati;
- un JSON versionato (schemaVersion: 1.1) con le stesse raccolte strutturate.

StudyExportRequest.IncludeStudyLabRecords è false per default; ExportStudyData lo imposta a true. Export ordinario non materializza né serializza le raccolte aggiuntive.

## Raccolte esportate

Oltre ad attività, sessioni, segmenti e cicli Pomodoro, vengono esportati:
- task e missioni, con i passi delle missioni in una raccolta separata;
- flashcard e storico delle revisioni;
- diario degli errori e check-in energia/focus;
- syllabus e question bank;
- voti, tentativi d'esame e appelli pianificati;
- dati delle materie come riferimento.

Le risposte delle simulazioni non vengono esportate: il modello corrente non persiste uno storico dei tentativi del question bank. Non vengono quindi creati risultati sintetici.

## Intervallo e filtri

L'intervallo From–To è inclusivo e interpretato nel fuso orario indicato da TimeZoneId.
- Attività, sessioni e cicli vengono selezionati per sovrapposizione con l'intervallo.
- Task e missioni vengono selezionati usando le rispettive date di creazione, aggiornamento, avvio, scadenza o completamento.
- Flashcard e ripassi usano creazione, aggiornamento, scadenza e ultima revisione; un ripasso è incluso solo se la relativa flashcard è stata selezionata.
- Errori, check-in, domande, voti, tentativi e appelli usano le rispettive date significative.
- I topic del syllabus usano ultima data di studio/aggiornamento; quelli senza date restano inclusi come dati di riferimento.
- Le materie sono dati di riferimento: vengono incluse tutte quelle ammesse dal filtro per corso, anche se non modificate nel periodo.

CourseIds filtra le raccolte collegate a una materia. Una lista non vuota composta solo da ID mancanti/blank produce un export vuoto, non un export senza filtro. I check-in energia/focus sono globali e non hanno un CourseId: vengono omessi quando è attivo un filtro per materia, evitando di attribuire a un corso dati personali non collegati.

## Privacy e sicurezza

- IncludeNotes = false esclude descrizioni, note, testi di domande, contenuti delle flashcard, risposte testuali e altri campi liberi.
- IncludeResult = false omette voti, lodi, esiti e risposte corrette.
- Le note delle attività e delle sessioni marcate private restano escluse quando IncludePrivateNotes = false.
- Le note dei check-in energia/focus sono trattate come private e vengono esportate solo quando IncludePrivateNotes = true.
- Gli URL del syllabus non vengono esportati, perché query string e parametri potrebbero contenere credenziali.
- Non vengono esportati testo estratto o pagine PDF, percorsi locali, URL Moodle di configurazione, token, credenziali/provider AI o snapshot di ActivityRevisionData.
- Le stringhe CSV passano dalla protezione anti-formula esistente; i valori testuali XLSX sono serializzati come inlineStr, non come formule.

L'esportazione completa è una copia esplicita dei dati selezionati, non un backup ripristinabile. Per backup e ripristino continuano a valere i flussi dedicati di BackupService.

## Verifica

StudyDataExportCoverageTests copre il mantenimento del report compatto, bundle CSV, fogli XLSX, JSON, filtri per corso e data, filtri non validi fail-closed, omissione dei campi testuali/risultati, note private, righe orfane, formula injection e assenza di contenuti PDF, URL Moodle/syllabus e snapshot di audit.
