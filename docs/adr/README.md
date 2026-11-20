# Architecture Decision Records

Gli ADR registrano decisioni tecniche e le relative conseguenze. Sono documenti versionati: una decisione superata va sostituita o marcata come superseded, non riscritta senza storico.

Gli ADR qui elencati descrivono la baseline osservata su `main` al 20 novembre 2026. **Accepted** significa che il documento registra una scelta già presente nel codice; non dichiara completati i flussi che non hanno test o verifica runtime.

| ADR | Decisione | Stato |
|---|---|---|
| [0001 — WPF, MVVM e dependency injection](0001-wpf-mvvm-dependency-injection.md) | Separazione della presentazione dalla logica e composizione DI | Accepted — baseline implementata |
| [0002 — Persistenza locale SQLCipher](0002-persistenza-locale-sqlcipher.md) | EF Core/SQLite cifrato come archivio unico, chiave DPAPI e migrazioni | Accepted — baseline implementata |
| [0003 — Token Moodle e confine di rete](0003-moodle-token-e-rete.md) | Vault DPAPI, endpoint HTTPS, POST REST e download same-origin | Accepted — baseline implementata |
| [0004 — Provenienza delle date d'esame](0004-provenienza-date-esami.md) | Separazione tra origine dell'evento e stato di verifica | Accepted — baseline implementata |

## Formato per nuovi ADR

Ogni nuovo record deve contenere:

- **Stato**: Proposed, Accepted, Deprecated o Superseded.
- **Data** e contesto verificabile.
- **Decisione**: scelta concreta, non una lista di desideri.
- **Alternative considerate** e motivi per cui non sono state scelte.
- **Conseguenze**: costi, limiti e obblighi per i cambiamenti futuri.
- **Evidenze**: file, test e PR che supportano lo stato dichiarato.

Non descrivere come implementato un comportamento che compare solo in una issue, in una PR non integrata o in una proposta.
