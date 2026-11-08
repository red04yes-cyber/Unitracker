param(
    [string]$PublishDirectory = "src/UniToStudyHub/bin/Release/net10.0-windows/win-x64/publish",
    [string]$OutputDirectory = "artifacts",
    [string]$Version = ""
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$publishPath = if ([IO.Path]::IsPathRooted($PublishDirectory)) {
    [IO.Path]::GetFullPath($PublishDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $PublishDirectory))
}
$outputPath = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDirectory))
}

if (-not (Test-Path -LiteralPath $publishPath -PathType Container)) {
    throw "Publish directory not found: $publishPath"
}
$executable = Join-Path $publishPath 'UniToStudyHub.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Published executable not found: $executable"
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$project = Get-Content -LiteralPath (Join-Path $repoRoot 'src/UniToStudyHub/UniToStudyHub.csproj') -Raw
    $Version = [string]$project.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Version must be semantic (for example 0.2.0); received '$Version'."
}

$forbidden = @(Get-ChildItem -LiteralPath $publishPath -File -Recurse | Where-Object {
    $_.Name -match '^(unitracker\.db(?:-wal|-shm)?|token\.bin|moodle-token\.secret|.*\.utbk|.*\.secret|unitracker.*\.log)$'
})
if ($forbidden.Count -gt 0) {
    throw "The publish directory contains user data or secrets and cannot be packaged."
}

New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$staging = Join-Path $outputPath ('.staging-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
$zipName = "UniToStudyHub-$Version-win-x64-portable.zip"
$zipPath = Join-Path $outputPath $zipName
$checksumPath = "$zipPath.sha256"

try {
    Copy-Item -Path (Join-Path $publishPath '*') -Destination $staging -Recurse -Force
    $readme = @"
UniToStudyHub $Version — Windows x64 portable

Avvio:
  Esegui UniToStudyHub.exe. Il pacchetto è self-contained e non richiede un
  runtime .NET separato.

Aggiornamento:
  Chiudi l'applicazione, estrai la nuova versione in una nuova cartella e
  avvia il nuovo UniToStudyHub.exe. Non cancellare i dati utente.

Dati:
  I dati dell'utente sono conservati separatamente dai binari in
  %LOCALAPPDATA%\UniToStudyHub. Il pacchetto non include database, token,
  chiavi API, backup personali o log.

Rimozione:
  Chiudi l'applicazione ed elimina la cartella estratta. I dati utente non
  vengono cancellati automaticamente; per cancellarli usa i controlli Privacy
  dell'applicazione o la procedura documentata.

Firma:
  Questo pacchetto non è firmato digitalmente. Verifica SHA-256 prima di
  distribuirlo o archiviarlo.
"@
    Set-Content -LiteralPath (Join-Path $staging 'PORTABLE-README.txt') -Value $readme -Encoding UTF8

    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    if (Test-Path -LiteralPath $checksumPath) { Remove-Item -LiteralPath $checksumPath -Force }
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zipPath -CompressionLevel Optimal

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $unsafeEntry = $archive.Entries | Where-Object {
            $_.FullName -match '(^|/)(unitracker\.db|token\.bin|moodle-token\.secret|[^/]+\.utbk|[^/]+\.secret|unitracker.*\.log)$'
        } | Select-Object -First 1
        if ($null -ne $unsafeEntry) {
            throw "Package contains a forbidden user-data entry."
        }
        if (-not ($archive.Entries | Where-Object { $_.FullName -eq 'UniToStudyHub.exe' })) {
            throw "Portable package does not contain UniToStudyHub.exe at its root."
        }
    }
    finally {
        $archive.Dispose()
    }

    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath $checksumPath -Value "$hash *$zipName" -Encoding ASCII
    Write-Host "Created package: $zipPath"
    Write-Host "SHA-256: $hash"
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}
