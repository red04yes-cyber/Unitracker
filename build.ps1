$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\UniToStudyHub\UniToStudyHub.csproj'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw 'Installa il .NET 10 SDK da https://dotnet.microsoft.com/download/dotnet/10.0' }
dotnet restore $project
dotnet publish $project -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
$output = Join-Path $PSScriptRoot 'src\UniToStudyHub\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish'
Write-Host "Build completata: $output" -ForegroundColor Green
