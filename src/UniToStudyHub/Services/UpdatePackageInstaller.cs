using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace UniToStudyHub.Services;

/// <summary>Downloads, verifies and stages the official portable update before replacing the app.</summary>
public sealed class UpdatePackageInstaller(HttpClient httpClient)
{
    public async Task<bool> StageAndLaunchAsync(
        AvailableAppUpdate update,
        int processId,
        string applicationDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var packageName = $"UniToStudyHub-{update.Version}-win-x64-portable.zip";
        var checksumName = packageName + ".sha256";
        var tempRoot = Path.Combine(Path.GetTempPath(), "UniToStudyHub-Update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var archivePath = Path.Combine(tempRoot, packageName);
            var checksumPath = Path.Combine(tempRoot, checksumName);
            var packageUri = new Uri(update.ReleaseDownloadBase, Uri.EscapeDataString(packageName));
            var checksumUri = new Uri(update.ReleaseDownloadBase, Uri.EscapeDataString(checksumName));

            await DownloadBoundedAsync(packageUri, archivePath, 250 * 1024 * 1024, cancellationToken);
            await DownloadBoundedAsync(checksumUri, checksumPath, 4096, cancellationToken);

            var checksumText = await File.ReadAllTextAsync(checksumPath, cancellationToken);
            var expectedHash = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (expectedHash is null || expectedHash.Length != 64 ||
                !expectedHash.All(Uri.IsHexDigit))
                throw new InvalidDataException("Il file SHA-256 della release non è valido.");

            await using (var stream = File.OpenRead(archivePath))
            {
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expectedHash), Convert.FromHexString(actualHash)))
                    throw new InvalidDataException("La verifica SHA-256 dell'aggiornamento non è riuscita.");
            }

            var payload = Path.Combine(tempRoot, "payload");
            Directory.CreateDirectory(payload);
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    var destination = Path.GetFullPath(Path.Combine(payload, entry.FullName));
                    var prefix = payload.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Il pacchetto contiene un percorso non consentito.");
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: false);
                }
            }

            if (!File.Exists(Path.Combine(payload, "UniToStudyHub.exe")))
                throw new InvalidDataException("Il pacchetto non contiene l'eseguibile atteso.");

            var target = Path.GetFullPath(applicationDirectory);
            if (!File.Exists(Path.Combine(target, "UniToStudyHub.exe")))
                throw new InvalidOperationException("Non riesco a identificare la cartella di installazione corrente.");

            var scriptPath = Path.Combine(tempRoot, "apply-update.ps1");
            var script = BuildInstallerScript(processId, target, payload, tempRoot);
            await File.WriteAllTextAsync(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);

            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            System.Diagnostics.Process.Start(start)
                ?? throw new InvalidOperationException("Windows non ha avviato il programma di aggiornamento.");
            return true;
        }
        catch
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort cleanup */ }
            throw;
        }
    }

    private async Task DownloadBoundedAsync(Uri uri, string destination, int maximumBytes, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 0 && response.Content.Headers.ContentLength > maximumBytes)
            throw new InvalidDataException("Il pacchetto di aggiornamento supera la dimensione consentita.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        var total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total = checked(total + read);
            if (total > maximumBytes) throw new InvalidDataException("Download oltre il limite consentito.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static string BuildInstallerScript(int processId, string target, string payload, string tempRoot)
    {
        static string Q(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        return $@"
$ErrorActionPreference = 'Stop'
$target = {Q(target)}
$payload = {Q(payload)}
$tempRoot = {Q(tempRoot)}
$backup = Join-Path $tempRoot 'previous-install'
$failed = Join-Path $tempRoot 'failed-install'
try {{
  try {{ $p = Get-Process -Id {processId} -ErrorAction Stop; $p.WaitForExit() }} catch {{ }}
  if (-not (Test-Path -LiteralPath (Join-Path $payload 'UniToStudyHub.exe'))) {{ throw 'Pacchetto incompleto.' }}
  if (Test-Path -LiteralPath $backup) {{ Remove-Item -LiteralPath $backup -Recurse -Force }}
  Copy-Item -LiteralPath $target -Destination $backup -Recurse -Force
  Get-ChildItem -LiteralPath $target -Force | Remove-Item -Recurse -Force
  Copy-Item -Path (Join-Path $payload '*') -Destination $target -Recurse -Force
  if (-not (Test-Path -LiteralPath (Join-Path $target 'UniToStudyHub.exe'))) {{ throw 'Installazione incompleta.' }}
  Start-Process -FilePath (Join-Path $target 'UniToStudyHub.exe')
  Start-Sleep -Seconds 3
  Remove-Item -LiteralPath $backup -Recurse -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}} catch {{
  try {{
    if (Test-Path -LiteralPath $target) {{ Get-ChildItem -LiteralPath $target -Force | Remove-Item -Recurse -Force }}
    if (Test-Path -LiteralPath $backup) {{ Copy-Item -Path (Join-Path $backup '*') -Destination $target -Recurse -Force }}
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show('Aggiornamento non completato. La versione precedente è stata ripristinata. ' + $_.Exception.Message, 'UniToStudyHub', 'OK', 'Error') | Out-Null
  }} catch {{ }}
}}
";
    }
}
