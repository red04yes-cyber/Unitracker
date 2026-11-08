# Provider AI supportati

UniToStudyHub supporta due modalità tramite lo stesso adapter OpenAI-compatible:

- **OpenAI cloud**: endpoint predefinito `https://api.openai.com/v1`, modello predefinito `gpt-4.1-mini`. Richiede una chiave API.
- **Ollama locale**: endpoint predefinito `http://127.0.0.1:11434/v1`. Richiede Ollama avviato e un modello locale già scaricato; non richiede una chiave reale.

## Configurazione

La selezione principale si effettua nella scheda **Impostazioni → AI Study Director · Provider**. È possibile scegliere OpenAI o Ollama, impostare il modello e concedere o negare il consenso. Le preferenze non segrete vengono salvate in `ai-provider-preferences.json` nella cartella dati locale; il provider viene inizializzato al successivo avvio.

La chiave OpenAI inserita nella schermata viene salvata separatamente tramite DPAPI per l'account Windows corrente. Il campo password viene svuotato dopo il salvataggio e il valore non viene scritto nel file delle preferenze. La revoca del consenso salva lo stato disattivato e chiude l'applicazione per terminare il runtime AI corrente. Senza consenso il provider resta non configurato e nessun contenuto viene inviato.

Le variabili d'ambiente restano disponibili per configurazioni di sviluppo e deployment; le preferenze salvate dall'interfaccia hanno precedenza sulla selezione provider/modello/consenso.

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

Per Ollama l'app accetta esclusivamente endpoint loopback (localhost/127.0.0.1/::1), sia HTTP sia HTTPS; qualunque host remoto o LAN viene rifiutato. Le richieste non seguono redirect.

## Protezione e privacy

- La chiave OpenAI può essere fornita tramite variabile d'ambiente oppure salvata con `AiCredentialStore`, che usa DPAPI per l'account Windows corrente.
- Le impostazioni non contengono il valore della chiave.
- I contenuti dei PDF sono inviati solo dopo consenso esplicito e solo quando viene invocata una funzione AI; nessun invio avviene all'importazione.
- Il testo dei PDF è marcato come contenuto non attendibile nel prompt per ridurre il rischio di prompt injection.
- L'endpoint cloud deve usare HTTPS; HTTP è consentito solo per il loopback locale.
- Il limite richieste giornaliero e il limite di dimensione del prompt sono applicati dal servizio AI.

## Limiti della prima integrazione

I piani di studio e le simulazioni orali sono proposte testuali: non vengono applicati automaticamente al calendario né persistiti nella banca domande. La configurazione del provider richiede il riavvio dell'app per applicare le nuove preferenze. Non inserire mai chiavi nei file versionati o nei log.
