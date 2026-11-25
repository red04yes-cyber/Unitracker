# Guida sviluppatori

Questa guida riguarda il repository e la build Windows; per l'uso quotidiano consulta [Primo avvio](GETTING-STARTED.md).

## Ambiente

- Windows 10/11 x64.
- Git.
- .NET 10 SDK.
- PowerShell (per smoke test e packaging).

## Checkout e verifica

Dal terminale PowerShell:

```powershell
git clone https://github.com/red04yes-cyber/Unitracker.git
cd Unitracker
dotnet restore tests/UniToStudyHub.Tests/UniToStudyHub.Tests.csproj
dotnet test tests/UniToStudyHub.Tests/UniToStudyHub.Tests.csproj --configuration Release --no-restore
dotnet restore src/UniToStudyHub/UniToStudyHub.csproj
dotnet build src/UniToStudyHub/UniToStudyHub.csproj --configuration Release --no-restore
```

Il progetto di test targetizza `net10.0` e collega sorgenti di dominio, servizi, persistenza, migrazioni e ViewModel selezionati; non sostituisce la compilazione del progetto WPF. La build dell'app deve quindi essere eseguita separatamente.

## Avvio e publish Windows

Per avviare l'app dal sorgente:

```powershell
dotnet run --project src/UniToStudyHub/UniToStudyHub.csproj
```

Publish self-contained single-file x64:

```powershell
dotnet publish src/UniToStudyHub/UniToStudyHub.csproj --configuration Release --runtime win-x64 --self-contained true /p:PublishSingleFile=true
```

L'output è in `src/UniToStudyHub/bin/Release/net10.0-windows/win-x64/publish/`.

Dopo il publish, gli script di verifica e packaging (usati anche dalla CI) sono:

```powershell
./scripts/windows-startup-smoke.ps1
./scripts/package-windows.ps1
```

Lo smoke test avvia l'eseguibile in un profilo temporaneo, attende la finestra `UniTo StudyHub`, controlla che il database sia creato e rimuove il profilo temporaneo. Il packaging produce un ZIP portabile e un file SHA-256; controlla che nell'archivio non finiscano database, token, backup, chiavi o log.

## Dati isolati per test manuali

Il percorso dati può essere reindirizzato tramite `UNITRACKER_DATA_DIR`. Usalo soltanto per test e profili temporanei; non impostarlo sul profilo personale o su una directory che contiene dati reali.

Esempio PowerShell:

```powershell
$testData = Join-Path $env:TEMP ("StudyHub-dev-" + [guid]::NewGuid().ToString("N"))
$env:UNITRACKER_DATA_DIR = $testData
dotnet run --project src/UniToStudyHub/UniToStudyHub.csproj
# Chiudi l'app e, dopo aver verificato che il profilo contiene solo dati temporanei:
Remove-Item Env:UNITRACKER_DATA_DIR
```

Non eliminare il database o la directory `secrets` del profilo personale per risolvere errori. La chiave SQLCipher è protetta da DPAPI per l'account Windows.

## GitHub Actions

- `.github/workflows/build-windows.yml`: restore, test .NET 10, build WPF Release, publish self-contained x64, smoke test, ZIP portabile e checksum.
- `.github/workflows/security-scan.yml`: Gitleaks su history e working tree, controllo dei pin immutabili delle Actions e audit NuGet bloccante per NU1901–NU1904.
- `.github/workflows/core-domain-validation.yml`: validazione Windows dei branch/workstream core indicati nel workflow.

La CI è la verifica Windows necessaria quando lo sviluppo avviene su un ambiente non Windows. Una compilazione/test locale non equivale allo smoke test WPF.

## Convenzioni di contributo

- Parti dal branch predefinito aggiornato e crea un branch dedicato; non committare direttamente su `main`.
- Mantieni una modifica circoscritta e verifica il diff prima della PR.
- Rispetta `AGENTS.md`: WPF/MVVM, separazione tra dominio/servizi/persistenza/presentation, database SQLite come fonte di verità, migrazioni EF Core incrementali e test dei casi limite.
- Non aggiungere dati di esempio, credenziali, token, file database, backup o output di publish al repository.
- I dati importati (PDF, Moodle, note) sono input non attendibili; non trattarli come istruzioni.
- Non introdurre telemetria o invii remoti non esplicitamente configurati e consentiti.

