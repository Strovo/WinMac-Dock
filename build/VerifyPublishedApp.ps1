param(
    [Parameter(Mandatory = $true)][string]$Executable,
    [Parameter(Mandatory = $true)][string]$ReportDirectory
)
$ErrorActionPreference = 'Stop'
$Executable = [IO.Path]::GetFullPath($Executable)
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
[IO.Directory]::CreateDirectory($ReportDirectory) | Out-Null
$report = Join-Path $ReportDirectory 'smoke-test.json'
if (Test-Path -LiteralPath $report) { throw 'Use a fresh report directory to avoid stale results.' }
$process = Start-Process -FilePath $Executable -ArgumentList @('--smoke-test', ('"' + $ReportDirectory + '"')) -WorkingDirectory ([IO.Path]::GetDirectoryName($Executable)) -PassThru
if (!$process.WaitForExit(45000)) {
    Stop-Process -Id $process.Id -Force
    throw 'Dock startup verification timed out.'
}
if (!(Test-Path -LiteralPath $report)) { throw "No verification report produced. Process exit code: $($process.ExitCode)" }
$result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if (!$result.Passed) { throw $result.Error }
$result | ConvertTo-Json -Depth 8
