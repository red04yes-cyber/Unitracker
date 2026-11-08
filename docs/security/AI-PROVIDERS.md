# Provider AI supportati

UniToStudyHub supporta tre modalità:

- **OpenAI cloud**: endpoint predefinito `https://api.openai.com/v1`, modello predefinito `gpt-4.1-mini`. Richiede una chiave API.
- **Ollama locale**: endpoint predefinito `http://127.0.0.1:11434/v1`. Richiede Ollama avviato e un modello locale già scaricato; non richiede una chiave reale.

## Configurazione

La selezione principale si effettua nella scheda **Impostazioni → AI Study Director · Provider**. È possibile scegliere OpenAI, Gemini o Ollama, impostare il modello e concedere o negare il consenso. Le preferenze non segrete vengono salvate in `ai-provider-preferences.json` nella cartella dati locale; il provider viene inizializzato al successivo avvio.

Le chiavi OpenAI e Gemini inserite nella schermata vengono salvate separatamente tramite DPAPI per l'account Windows corrente. Il campo password viene svuotato dopo il salvataggio e il valore non viene scritto nel file delle preferenze. La revoca del consenso salva lo stato disattivato e chiude l'applicazione per terminare il runtime AI corrente. Senza consenso il provider resta non configurato e nessun contenuto viene inviato.

Le variabili d'ambiente restano disponibili per configurazioni di sviluppo e deployment; le preferenze salvate dall'interfaccia hanno precedenza sulla selezione provider/modello/consenso.

| Variabile | Significato |
|---|---|
| `STUDYHUB_AI_PROVIDER` | `openai`, `gemini` oppure `ollama` |
| `STUDYHUB_AI_CONSENT` | Deve essere `true` per autorizzare richieste AI |
| `STUDYHUB_AI_MODEL` | Modello da usare; per Ollama è obbligatorio |
| `STUDYHUB_AI_BASE_URL` | Override facoltativo dell'endpoint |
| `STUDYHUB_OPENAI_API_KEY` | Chiave OpenAI, letta solo a runtime |\n| `STUDYHUB_GEMINI_API_KEY` | Chiave Google Gemini, letta solo a runtime |

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

- Le chiavi OpenAI e Gemini possono essere fornite tramite variabile d'ambiente oppure salvate con `AiCredentialStore`, che usa DPAPI per l'account Windows corrente.
- Le impostazioni non contengono il valore della chiave.
- I contenuti dei PDF sono inviati solo dopo consenso esplicito e solo quando viene invocata una funzione AI; nessun invio avviene all'importazione.
- Il testo dei PDF è marcato come contenuto non attendibile nel prompt per ridurre il rischio di prompt injection.
- L'endpoint cloud deve usare HTTPS; HTTP è consentito solo per il loopback locale.
- Il limite richieste giornaliero e il limite di dimensione del prompt sono applicati dal servizio AI.

## Limiti della prima integrazione

I piani di studio e le simulazioni orali sono proposte testuali: non vengono applicati automaticamente al calendario né persistiti nella banca domande. La configurazione del provider richiede il riavvio dell'app per applicare le nuove preferenze. Non inserire mai chiavi nei file versionati o nei log.


### Google Gemini

Seleziona **Gemini · Google AI**, inserisci una chiave API ottenuta da Google AI Studio e scegli un modello disponibile per il tuo account (ad esempio `gemini-2.5-flash`). La chiave è salvata con DPAPI nel vault locale, con nome distinto da quella OpenAI. Le richieste usano l'endpoint ufficiale Gemini `generateContent` e inviano la chiave nell'header `x-goog-api-key`, mai nella URL. È possibile usare anche la variabile d'ambiente `STUDYHUB_GEMINI_API_KEY`. Il consenso esplicito è richiesto come per gli altri provider.
