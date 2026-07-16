[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\LolScout.App\LolScout.App.csproj'
$manifest = Join-Path $root 'src\LolScout.App\app.manifest'
$output = Join-Path $root 'artifacts\publish\win-x64'

function Invoke-DotNet([string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE" }
}

$manifestText = Get-Content -LiteralPath $manifest -Raw
if ($manifestText -notmatch 'requestedExecutionLevel\s+level="requireAdministrator"') {
    throw 'app.manifest must requireAdministrator.'
}

Invoke-DotNet @('test', (Join-Path $root 'LolScout.sln'), '-c', 'Release')
Invoke-DotNet @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', '-o', $output)

$exe = Join-Path $output 'LolScout.App.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'Published executable is missing.' }

$unexpected = Get-ChildItem -LiteralPath $output -File | Where-Object { $_.Name -ne 'LolScout.App.exe' }
if ($unexpected) { throw "Single-file publish contains unexpected files: $($unexpected.Name -join ', ')" }

$exeBytes = [IO.File]::ReadAllBytes($exe)
$exeText = [Text.Encoding]::UTF8.GetString($exeBytes)
$exeWideText = [Text.Encoding]::Unicode.GetString($exeBytes)
if ($exeText -notmatch 'requireAdministrator') { throw 'Published executable does not contain the administrator manifest.' }

$forbiddenNames = Get-ChildItem -LiteralPath $output -Recurse -File | Where-Object {
    $_.Name -match '(?i)(token|ticket|session|cookie|response)' -or $_.Extension -match '(?i)^\.(log|dmp|json|har)$'
}
if ($forbiddenNames) { throw "Sensitive artifact found: $($forbiddenNames.FullName -join ', ')" }

$pathMarkers = @('C:\\Users\\', 'C:\\tmp\\', '\\AppData\\')
foreach ($marker in $pathMarkers) {
    if ($exeText.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $exeWideText.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Published executable contains a local-path marker: $marker"
    }
}

Write-Host "Published and security-checked: $exe"
