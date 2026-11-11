param(
    [string]$PublishDirectory = "src/UniToStudyHub/bin/Release/net10.0-windows/win-x64/publish"
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
    if ($process.MainWindowTitle -eq 'Errore di avvio') {
        $logPath = Join-Path $dataDirectory 'logs/unitracker.log'
        if (Test-Path $logPath) {
            Write-Host "Startup failure diagnostics:"
            Get-Content -LiteralPath $logPath -Tail 160 | ForEach-Object { Write-Host $_ }
        } else {
            Write-Host "Startup failure log not found at: $logPath"
        }
        throw "The application displayed its startup error dialog. See diagnostics above."
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


    # Exercise every NavigationView destination through real mouse input.
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;

public static class UniToStudyHubSmokeDesktop
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    public static void ClickAt(IntPtr hWnd, int x, int y)
    {
        _ = SetForegroundWindow(hWnd);
        if (!SetCursorPos(x, y))
            throw new InvalidOperationException("Unable to position the mouse for UI smoke testing.");

        Thread.Sleep(100);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }
}
'@

    $navigationChecks = @(
        @{ Name = 'Materie'; NavigationId = 'Navigation.Courses'; HeadingId = 'PageHeading.Courses' },
        @{ Name = 'Calendario'; NavigationId = 'Navigation.Calendar'; HeadingId = 'PageHeading.Calendar' },
        @{ Name = 'Studio Lab'; NavigationId = 'Navigation.StudyLab'; HeadingId = 'PageHeading.StudyLab' },
        @{ Name = 'Accademia'; NavigationId = 'Navigation.Academy'; HeadingId = 'PageHeading.Academy' },
        @{ Name = 'Progressi'; NavigationId = 'Navigation.Progress'; HeadingId = 'PageHeading.Progress' },
        @{ Name = 'Impostazioni'; NavigationId = 'Navigation.Settings'; HeadingId = 'PageHeading.Settings' },
        @{ Name = 'Oggi'; NavigationId = 'Navigation.Today'; HeadingId = 'PageHeading.Dashboard' }
    )

    foreach ($check in $navigationChecks) {
        $automationRoot = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        $navigationCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            $check.NavigationId)
        $navigationItem = $automationRoot.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $navigationCondition)
        if ($null -eq $navigationItem) {
            throw "Navigation item '$($check.Name)' was not exposed to Windows UI Automation."
        }

        $point = $navigationItem.GetClickablePoint()
        [UniToStudyHubSmokeDesktop]::ClickAt(
            $process.MainWindowHandle,
            [int][Math]::Round($point.X),
            [int][Math]::Round($point.Y))

        $heading = $null
        $navigationDeadline = (Get-Date).AddSeconds(5)
        do {
            Start-Sleep -Milliseconds 200
            $automationRoot = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
            $headingCondition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
                $check.HeadingId)
            $heading = $automationRoot.FindFirst(
                [System.Windows.Automation.TreeScope]::Descendants,
                $headingCondition)
        } while ($null -eq $heading -and (Get-Date) -lt $navigationDeadline)

        if ($null -eq $heading -or $heading.Current.IsOffscreen) {
            throw "Navigation to '$($check.Name)' did not display its expected page heading '$($check.HeadingId)'."
        }
        Write-Host "Navigation smoke passed: $($check.Name)."
    }

    # Capture the resulting window as an optional CI artifact for visual inspection.
    try {
        Add-Type -AssemblyName System.Drawing.Common -ErrorAction Stop
        $windowRect = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle).Current.BoundingRectangle
        $left = [int][Math]::Round($windowRect.Left)
        $top = [int][Math]::Round($windowRect.Top)
        $width = [int][Math]::Round($windowRect.Width)
        $height = [int][Math]::Round($windowRect.Height)
        if ($width -gt 0 -and $height -gt 0) {
            $bitmap = [System.Drawing.Bitmap]::new($width, $height)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.CopyFromScreen($left, $top, 0, 0, $bitmap.Size)
                New-Item -ItemType Directory -Path 'artifacts' -Force | Out-Null
                $screenshotPath = Join-Path (Resolve-Path 'artifacts').Path 'windows-navigation-smoke.png'
                $bitmap.Save($screenshotPath, [System.Drawing.Imaging.ImageFormat]::Png)
                Write-Host "Windows navigation screenshot: $screenshotPath"
            } finally {
                $graphics.Dispose()
                $bitmap.Dispose()
            }
        }
    } catch {
        Write-Warning "Unable to capture optional Windows UI screenshot: $($_.Exception.Message)"
    }

    Write-Host "WPF startup and seven-destination navigation smoke passed."
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
