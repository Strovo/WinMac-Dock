param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Output,
    [string]$Manifest
)
$ErrorActionPreference = 'Stop'
$Source = [IO.Path]::GetFullPath($Source)
$Output = [IO.Path]::GetFullPath($Output)
$compiler = Get-Command rc.exe -ErrorAction SilentlyContinue
if ($compiler) { $rc = $compiler.Source }
else {
    $packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
    $rc = Get-ChildItem (Join-Path $packages 'microsoft.windows.sdk.buildtools/*/bin/*/x64/rc.exe') -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (!$rc) { throw 'Windows resource compiler not found. Restore NuGet packages or install the Windows SDK.' }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Output)) | Out-Null
$inputFile = $Source
$temporary = $null
try {
    if ($Manifest) {
        $Manifest = [IO.Path]::GetFullPath($Manifest)
        if (!(Test-Path -LiteralPath $Manifest)) { throw "Manifest not found: $Manifest" }
        $content = [IO.File]::ReadAllText($Source)
        $content = [regex]::Replace($content, '(?m)^1\s+24\s+"[^"]+"', ('1 24 "' + $Manifest.Replace('\', '/') + '"'))
        $temporary = Join-Path ([IO.Path]::GetDirectoryName($Output)) ('icons-' + [guid]::NewGuid().ToString('N') + '.rc')
        [IO.File]::WriteAllText($temporary, $content)
        $inputFile = $temporary
    }
    Push-Location ([IO.Path]::GetDirectoryName($Source))
    try {
        & $rc /nologo /fo $Output $inputFile
        if ($LASTEXITCODE -ne 0) { throw "Resource compiler failed with exit code $LASTEXITCODE" }
    }
    finally { Pop-Location }
}
finally { if ($temporary) { Remove-Item -LiteralPath $temporary -ErrorAction SilentlyContinue } }
