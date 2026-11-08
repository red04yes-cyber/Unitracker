# Provider AI supportati

UniToStudyHub supporta due modalità tramite lo stesso adapter OpenAI-compatible:

- **OpenAI cloud**: endpoint predefinito `https://api.openai.com/v1`, modello predefinito `gpt-4.1-mini`. Richiede una chiave API.
- **Ollama locale**: endpoint predefinito `http://127.0.0.1:11434/v1`. Richiede Ollama avviato e un modello locale già scaricato; non richiede una chiave reale.

## Configurazione attuale

La selezione viene letta all'avvio dalle variabili d'ambiente del processo. Il consenso è sempre disattivato per impostazione predefinita: senza `STUDYHUB_AI_CONSENT=true` il provider rimane non configurato e nessun contenuto viene inviato.

| Variabile | Significato |
|---|---|
| `STUDYHUB_AI_PROVIDER` | `openai` oppure `ollama` |
| `STUDYHUB_AI_CONSENT` | Deve essere `true` per autorizzare richieste AI |
| `STUDYHUB_AI_MODEL` | Modello da usare; per Ollama è obbligatorio |
| `STUDYHUB_AI_BASE_URL` | Override facoltativo dell'endpoint |
| `STUDYHUB_OPENAI_API_KEY` | Chiave OpenAI, letta solo a runtime |

Esempio OpenAI (PowerShell, solo per la sessione corrente):

```powershell
$env:STUDYHUB_AI_PROVIDER = "openai"
$env:STUDYHUB_AI_CONSENT = "true"
$env:STUDYHUB_OPENAI_API_KEY = Read-Host "OpenAI API key"
```

Esempio Ollama:

```powershell
$env:STUDYHUB_AI_PROVIDER = "ollama"
$env:STUDYHUB_AI_CONSENT = "true"
$env:STUDYHUB_AI_MODEL = "llama3.2"
```

Per Ollama l'app accetta HTTP esclusivamente sull'indirizzo loopback predefinito. Un override HTTP verso host esterni o LAN viene rifiutato. Le richieste non seguono redirect.

## Protezione e privacy

- La chiave OpenAI può essere fornita tramite variabile d'ambiente oppure salvata con `AiCredentialStore`, che usa DPAPI per l'account Windows corrente.
- Le impostazioni non contengono il valore della chiave.
- I contenuti dei PDF sono inviati solo dopo consenso esplicito e solo quando viene invocata una funzione AI; nessun invio avviene all'importazione.
- Il testo dei PDF è marcato come contenuto non attendibile nel prompt per ridurre il rischio di prompt injection.
- L'endpoint cloud deve usare HTTPS; HTTP è consentito solo per il loopback locale.
- Il limite richieste giornaliero e il limite di dimensione del prompt sono applicati dal servizio AI.

## Limiti della prima integrazione

La selezione tramite interfaccia delle impostazioni e la gestione guidata della chiave verranno completate nella UI dedicata. Fino ad allora la configurazione è intenzionalmente esplicita tramite variabili d'ambiente; non inserire chiavi nei file versionati o nei log.
