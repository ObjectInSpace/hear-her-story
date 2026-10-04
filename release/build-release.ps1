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
#   .\release\build-release.ps1 -Slim        # for an existing MelonLoader install
#   .\release\build-release.ps1 -Both        # full and slim in one go
#
# -Slim assumes the player already has MelonLoader and drops the stock loader
# from the bundle. It still ships MelonLoader/net35/Tomlet.dll, because that
# file is ours: a stock install carries the broken one, and the failure is
# silent. See MODIFICATIONS.txt and release/README.md.

[CmdletBinding()]
param(
    [string]$Version,
    [switch]$Slim,
    [switch]$Both
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

# Everything the slim bundle ships, relative to the game root. Named explicitly
# rather than derived by excluding MelonLoader/, because Tomlet.dll lives inside
# that tree and must survive the cut — an exclude rule that looked right would
# drop the one loader file we actually patched.
$slimPaths = @(
    'Mods',
    'UserLibs',
    'UniversalSpeech.dll',
    'nvdaControllerClient.dll',
    'MelonLoader\net35\Tomlet.dll',
    'MelonLoader\MODIFICATIONS.txt',
    'MelonLoader\Documentation'
)

# Stage into a temp tree so a failure part-way cannot leave payload/ polluted
# with a stale mod DLL that would then ship in the next release.
function New-Bundle {
    param(
        [AllowEmptyString()][string]$Suffix = '',
        [Parameter(Mandatory)][int]$Expected,
        [string[]]$Include
    )

    $stage = Join-Path ([System.IO.Path]::GetTempPath()) "hhs-release-$Version$Suffix-$PID"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null

    try {
        Copy-Item "$payload\*" $stage -Recurse -Force
        New-Item -ItemType Directory -Force -Path (Join-Path $stage 'Mods') | Out-Null
        Copy-Item $dll (Join-Path $stage 'Mods\HearHerStory.dll') -Force

        if ($Include) {
            # Build the wanted set first, then delete the rest, so a typo in
            # $slimPaths surfaces as a missing file rather than a silent no-op.
            $keep = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
            foreach ($rel in $Include) {
                $abs = Join-Path $stage $rel
                if (-not (Test-Path $abs)) { throw "Slim bundle wants $rel, which is not in the staged payload." }
                foreach ($f in Get-ChildItem $abs -Recurse -File) { [void]$keep.Add($f.FullName) }
            }
            Get-ChildItem $stage -Recurse -File |
                Where-Object { -not $keep.Contains($_.FullName) } |
                Remove-Item -Force
            Get-ChildItem $stage -Recurse -Directory |
                Sort-Object { $_.FullName.Length } -Descending |
                Where-Object { -not (Get-ChildItem $_.FullName -Recurse -File) } |
                Remove-Item -Recurse -Force

            # Only this bundle carries install notes: the full one is "extract
            # and play", whereas this one overwrites a file in the player's
            # existing MelonLoader and has to say so.
            Copy-Item (Join-Path $PSScriptRoot 'slim-README.txt') `
                      (Join-Path $stage 'HearHerStory-README.txt') -Force
        }

        New-Item -ItemType Directory -Force -Path $out | Out-Null
        $zip = Join-Path $out "HearHerStory-$Version$Suffix.zip"
        if (Test-Path $zip) { Remove-Item $zip -Force }

        Compress-Archive -Path "$stage\*" -DestinationPath $zip -CompressionLevel Optimal

        $files = (Get-ChildItem $stage -Recurse -File).Count
        $mb    = [math]::Round((Get-Item $zip).Length / 1MB, 2)

        # File counts have been stable since 0.4.1; a change means something was
        # added or dropped and wants looking at before it ships. Counted from the
        # staging tree rather than the zip, because Compress-Archive also writes
        # directory entries and `unzip -l` counts those as members.
        if ($files -ne $Expected) {
            Write-Warning "Bundle $zip has $files files; expected $Expected. Check payload/ for strays or omissions."
        }

        Write-Host ""
        Write-Host "  $zip" -ForegroundColor Green
        Write-Host "  $files files, $mb MB"
        return $zip
    }
    finally {
        Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$made = @()
if ($Both -or -not $Slim) { $made += New-Bundle -Suffix ''      -Expected 43 }
if ($Both -or $Slim)      { $made += New-Bundle -Suffix '-slim' -Expected 9 -Include $slimPaths }

Write-Host ""
Write-Host "Publish with:" -ForegroundColor Cyan
$assets = ($made | ForEach-Object { "`"$_`"" }) -join ' '
Write-Host "  gh release create v$Version $assets --title `"Hear Her Story $Version`" --notes-file release\notes-$Version.md"
