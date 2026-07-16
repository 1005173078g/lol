[CmdletBinding()]
param([switch] $PublicRelease)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$solution = Join-Path $root 'LolScout.sln'
$project = Join-Path $root 'src\LolScout.App\LolScout.App.csproj'
$sourceManifest = Join-Path $root 'src\LolScout.App\app.manifest'
$output = Join-Path $root 'artifacts\publish\win-x64'
$exe = Join-Path $output 'LolScout.App.exe'
$sdkToolsVersion = '10.0.28000.2270'

if ($PublicRelease -and $env:RIOT_PRODUCT_REGISTRATION_CONFIRMED -cne 'true') {
    throw 'Public release is blocked: set RIOT_PRODUCT_REGISTRATION_CONFIRMED=true only after confirming current Riot Developer Portal product registration and policy compliance.'
}

function Invoke-DotNet([string[]] $Arguments) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE" }
}

function Assert-AdministratorManifest([string] $Path) {
    [xml] $document = Get-Content -LiteralPath $Path -Raw
    $nodes = $document.SelectNodes("//*[local-name()='requestedExecutionLevel']")
    if ($nodes.Count -ne 1 -or $nodes[0].GetAttribute('level') -cne 'requireAdministrator') {
        throw "Manifest must contain exactly one requireAdministrator requestedExecutionLevel: $Path"
    }
}

function Find-MtExe {
    $packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    $toolRoot = Join-Path $packageRoot "microsoft.windows.sdk.buildtools\$sdkToolsVersion"
    $candidate = Get-ChildItem -LiteralPath $toolRoot -Recurse -Filter mt.exe -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'x64' } | Select-Object -First 1
    if (-not $candidate) { throw "mt.exe not found in Microsoft.Windows.SDK.BuildTools $sdkToolsVersion. Run dotnet restore." }
    return $candidate.FullName
}

function Get-PrintableStrings([byte[]] $Bytes) {
    $ascii = [Text.Encoding]::ASCII.GetString($Bytes)
    $wide = [Text.Encoding]::Unicode.GetString($Bytes)
    return (([regex]::Matches($ascii, '[\x20-\x7E]{4,}') | ForEach-Object Value) +
        ([regex]::Matches($wide, '[\x20-\x7E]{4,}') | ForEach-Object Value)) -join "`n"
}

function Assert-NoSourceSecrets {
    $tracked = & git -C $root ls-files -- src README.md docs/troubleshooting.md
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed during source security scan.' }
    $files = $tracked | Where-Object { $_ -match '(?i)\.(cs|xaml|xml|md)$' } | ForEach-Object { Get-Item (Join-Path $root $_) }
    $credentialPattern = '(?i)\b(authorization|cookie)\b\s*:\s*(basic|bearer|[^\s"'']{8,})|\b(token|ticket|session)\b\s*=\s*[^\s"'']{8,}'
    $absolutePathPattern = '(?i)([a-z]:[\\/](users|documents and settings|tmp|temp|windows)[\\/]|\\\\[^\\\s]+\\[^\\\s]+)'
    foreach ($file in $files) {
        $text = Get-Content -LiteralPath $file.FullName -Raw
        if ($text -match $credentialPattern) { throw "Credential-like assignment found in tracked text: $($file.FullName)" }
        if ($text -match $absolutePathPattern) { throw "Absolute machine path found in tracked text: $($file.FullName)" }
    }
}

Assert-AdministratorManifest $sourceManifest
Assert-NoSourceSecrets
Invoke-DotNet @('restore', $solution)
Invoke-DotNet @('restore', $project, '-r', 'win-x64')
$mt = Find-MtExe
Invoke-DotNet @('test', $solution, '-c', 'Release', '--no-restore')

if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
Invoke-DotNet @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None',
    "-p:PathMap=$root=/_/", '-o', $output)

$files = @(Get-ChildItem -LiteralPath $output -Recurse -File)
$directories = @(Get-ChildItem -LiteralPath $output -Recurse -Directory)
if ($files.Count -ne 1 -or $files[0].FullName -cne $exe -or $directories.Count -ne 0) {
    throw 'Publish output must contain exactly LolScout.App.exe and no subdirectories.'
}

$temporaryManifest = Join-Path ([IO.Path]::GetTempPath()) ("LolScout-manifest-{0}.xml" -f [guid]::NewGuid())
try {
    & $mt "-inputresource:$exe;#1" "-out:$temporaryManifest"
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $temporaryManifest)) { throw 'mt.exe failed to extract the PE manifest.' }
    Assert-AdministratorManifest $temporaryManifest
}
finally { Remove-Item -LiteralPath $temporaryManifest -Force -ErrorAction SilentlyContinue }

$forbiddenName = $files | Where-Object { $_.Name -match '(?i)(token|ticket|session|cookie|response)' -or $_.Extension -match '(?i)^\.(log|dmp|json|har)$' }
if ($forbiddenName) { throw "Sensitive artifact name found: $($forbiddenName.FullName)" }

$strings = Get-PrintableStrings ([IO.File]::ReadAllBytes($exe))
$userPatterns = if ($env:USERNAME -ieq 'Administrator') {
    @('(?i)[\\/]Users[\\/]Administrator(?:[\\/]|$)', '(?i)[\\/]Administrator[\\/]')
} elseif ($env:USERNAME) {
    @('(?i)(^|[^A-Za-z0-9])' + [regex]::Escape($env:USERNAME) + '([^A-Za-z0-9]|$)')
} else { @() }
foreach ($pattern in $userPatterns) {
    if ($strings -match $pattern) { throw "Published executable contains the build username in a machine-specific context: $env:USERNAME" }
}
$forbiddenValues = @($root, $root.Replace('\', '/'), 'secret-one', 'secret-two', 'fictional-secret',
    'fictional-token', 'session=secret', 'nested-secret', 'exception-secret', 'array-secret', '23.9 seconds') | Where-Object { $_ }
foreach ($value in $forbiddenValues) {
    if ($strings.IndexOf($value, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Published executable contains a forbidden build/test/probe value: $value"
    }
}

Write-Host "Published, manifest-validated, and security-scanned: $exe"
