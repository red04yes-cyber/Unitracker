# Roadmap e backlog

## Fonte di verità

Il backlog operativo e lo stato delle attività sono mantenuti nelle [GitHub Issues](https://github.com/red04yes-cyber/Unitracker/issues) e nelle [Pull Request aperte](https://github.com/red04yes-cyber/Unitracker/pulls). Una PR aperta descrive lavoro proposto: non è parte del prodotto distribuito finché non viene integrata e verificata.

Questo documento evita di duplicare una lista di issue che diventerebbe rapidamente obsoleta. Descrive il perimetro del prodotto presente su `main` e i criteri con cui ordinare gli incrementi.

## Baseline presente su main

La codebase contiene già:

- gestione locale di corsi, carriera, tentativi e calcoli accademici;
- calendario con eventi manuali, import ICS e collegamenti indicativi degli esami pianificati;
- pianificazione dello studio con anteprima e applicazione controllata al calendario;
- Study Lab con sessioni focus, task, attività registrate, missioni, diario errori, flashcard e progressi;
- syllabus, banca domande, preparazione all'esame e ricerca locale nei PDF importati;
- backup cifrati, verifica/ripristino, import/export e strumenti privacy;
- integrazione Moodle facoltativa per account e servizi autorizzati.

La presenza di un servizio o di un modello non dimostra da sola che ogni percorso abbia una UI completa o una verifica runtime su Windows. Consultare test e PR per lo stato puntuale di ciascun flusso.

## Ordine di priorità

1. Protezione dei dati locali, segreti, importazioni e ripristino.
2. Integrità delle relazioni tra corsi, esami, calendario, task e attività.
3. Stabilità di timer, sincronizzazione e operazioni asincrone.
4. Flussi quotidiani per lezioni, materiali, esami e pianificazione.
5. Accessibilità, usabilità, prestazioni e manutenzione.

Gli incrementi devono essere verticali, circoscritti e testabili. Non introdurre dati dimostrativi, dipendenze o servizi remoti senza una necessità verificata.

## Decisioni architetturali

Le decisioni che vincolano l'implementazione sono registrate in [docs/adr](adr/README.md). Ogni nuova proposta deve indicare se descrive una scelta già implementata o se richiede ancora approvazione e lavoro.
