param(
    [string]$PublishDirectory = "src/UniToStudyHub/bin/Release/net10.0-windows10.0.19041.0/win-x64/publish"
)

$ErrorActionPreference = 'Stop'

$publishPath = (Resolve-Path $PublishDirectory).Path
$exe = Join-Path $publishPath 'UniToStudyHub.exe'
if (-not (Test-Path $exe)) {
    throw "Published executable not found: $exe"
}

$tempRoot = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$dataDirectory = Join-Path $tempRoot ("unitracker-smoke-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null

$previousDataDirectory = $env:UNITRACKER_DATA_DIR
$process = $null
$startedAt = Get-Date
try {
    $env:UNITRACKER_DATA_DIR = $dataDirectory
    $process = Start-Process -FilePath $exe -WorkingDirectory $publishPath -PassThru

    $deadline = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.HasExited) {
            $exitCode = $process.ExitCode
            $logPath = Join-Path $dataDirectory 'logs/unitracker.log'
            if (Test-Path $logPath) {
                Write-Host "Local startup diagnostics:"
                Get-Content -LiteralPath $logPath -Tail 100 | ForEach-Object { Write-Host $_ }
            }

            $events = @(Get-WinEvent -FilterHashtable @{
                LogName = 'Application'
                StartTime = $startedAt
            } -ErrorAction SilentlyContinue |
                Where-Object { $_.ProviderName -in @('.NET Runtime', 'Application Error', 'Windows Error Reporting') } |
                Sort-Object TimeCreated -Descending |
                Select-Object -First 8)
            foreach ($event in $events) {
                Write-Host "WINDOWS EVENT [$($event.TimeCreated)] [$($event.ProviderName)] [$($event.Id)]"
                Write-Host $event.Message
            }

            throw "WPF app exited before showing its window (exit code $exitCode)."
        }
    } while (($process.MainWindowHandle -eq 0 -or [string]::IsNullOrWhiteSpace($process.MainWindowTitle)) -and (Get-Date) -lt $deadline)

    if ($process.MainWindowHandle -eq 0) {
        throw "WPF app did not create a main window within 60 seconds."
    }
    if ($process.MainWindowTitle -ne 'UniTo StudyHub') {
        throw "Unexpected WPF main window title: '$($process.MainWindowTitle)'."
    }

    $database = Join-Path $dataDirectory 'unitracker.db'
    if (-not (Test-Path $database)) {
        throw "WPF startup did not create the SQLite database at $database."
    }
    if ((Get-Item $database).Length -eq 0) {
        throw "WPF startup created an empty SQLite database."
    }

    Write-Host "WPF startup smoke passed: window '$($process.MainWindowTitle)', database '$database'."
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) {
            $process.Kill()
            $process.WaitForExit()
        }
    }

    if ($null -eq $previousDataDirectory) {
        Remove-Item Env:UNITRACKER_DATA_DIR -ErrorAction SilentlyContinue
    } else {
        $env:UNITRACKER_DATA_DIR = $previousDataDirectory
    }
    Remove-Item -LiteralPath $dataDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
