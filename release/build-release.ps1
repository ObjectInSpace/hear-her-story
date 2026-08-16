# Builds the shippable release zip from local assets only.
#
# The payload/ tree is everything that does not change between releases: the
# trimmed net35 MelonLoader, the patched Tomlet, the speech DLLs and the
# accessibility library. Only Mods/HearHerStory.dll differs release to release,
# so this compiles that fresh and drops it in.
#
# Deliberately does NOT rebuild the tree from the game install at
# D:\root\her story — that install carries the full 43 MB MelonLoader, not the
# trimmed set, and packaging from it bloats the bundle.
#
#   .\release\build-release.ps1              # version read from Plugin.cs
#   .\release\build-release.ps1 -Version 0.5.0

[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$root    = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\HearHerStory\HearHerStory.csproj'
$payload = Join-Path $PSScriptRoot 'payload'
$out     = Join-Path $PSScriptRoot 'out'

# The MelonInfo attribute is what the loader and the player actually see, so it
# is the authority on the version number rather than the csproj or the tag.
if (-not $Version) {
    $plugin = Get-Content (Join-Path $root 'src\HearHerStory\Plugin.cs') -Raw
    if ($plugin -notmatch '"Hear Her Story",\s*"([0-9.]+)"') {
        throw "Could not read the version from Plugin.cs; pass -Version explicitly."
    }
    $Version = $Matches[1]
}

Write-Host "Building Hear Her Story $Version" -ForegroundColor Cyan

if (-not (Test-Path $payload)) {
    throw "Missing payload tree at $payload"
}

# Catch the drift that shipped 0.4.1 reporting itself as 0.4.0: three files
# carry the version and only the tag was moved.
$csproj = Get-Content $project -Raw
if ($csproj -notmatch "<Version>$([regex]::Escape($Version))</Version>") {
    throw "HearHerStory.csproj <Version> does not match $Version. Bump it, Plugin.cs MelonInfo, and the OnInitializeMelon log line together."
}

$startup = Get-Content (Join-Path $root 'src\HearHerStory\Plugin.cs') -Raw
if ($startup -notmatch "Hear Her Story $([regex]::Escape($Version)) starting\.") {
    throw "The OnInitializeMelon startup log line does not mention $Version."
}

dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$dll = Join-Path $root 'src\HearHerStory\bin\Release\net35\HearHerStory.dll'
if (-not (Test-Path $dll)) { throw "Built mod DLL not found at $dll" }

# Stage into a temp tree so a failure part-way cannot leave payload/ polluted
# with a stale mod DLL that would then ship in the next release.
$stage = Join-Path ([System.IO.Path]::GetTempPath()) "hhs-release-$Version-$PID"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

try {
    Copy-Item "$payload\*" $stage -Recurse -Force
    New-Item -ItemType Directory -Force -Path (Join-Path $stage 'Mods') | Out-Null
    Copy-Item $dll (Join-Path $stage 'Mods\HearHerStory.dll') -Force

    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $zip = Join-Path $out "HearHerStory-$Version.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }

    Compress-Archive -Path "$stage\*" -DestinationPath $zip -CompressionLevel Optimal

    $files = (Get-ChildItem $stage -Recurse -File).Count
    $mb    = [math]::Round((Get-Item $zip).Length / 1MB, 2)

    # The bundle has been 43 files since 0.4.1; a change means something was
    # added or dropped and wants looking at before it ships. Counted from the
    # staging tree rather than the zip, because Compress-Archive also writes
    # directory entries and `unzip -l` counts those as members.
    if ($files -ne 43) {
        Write-Warning "Bundle has $files files; expected 43. Check payload/ for strays or omissions."
    }

    Write-Host ""
    Write-Host "  $zip" -ForegroundColor Green
    Write-Host "  $files files, $mb MB"
    Write-Host ""
    Write-Host "Publish with:" -ForegroundColor Cyan
    Write-Host "  gh release create v$Version `"$zip`" --title `"Hear Her Story $Version`" --notes-file release\notes-$Version.md"
}
finally {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
}
