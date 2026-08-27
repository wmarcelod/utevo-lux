#requires -Version 5.1
<#
  packaging/release.ps1 — one-command release for Utevo Lux.

  Everyday releases (the auto-updater channel):
      pwsh packaging/release.ps1 -Version 0.1.2 -Notes "What changed"
  Also refresh the winget package (do this occasionally, not every release):
      pwsh packaging/release.ps1 -Version 0.2.0 -Notes "..." -Winget
  Build the installer + zip locally without releasing (to test the pipeline):
      pwsh packaging/release.ps1 -Version 0.1.2 -DryRun

  Does: bump <Version> -> publish self-contained -> build installer (Inno) -> zip ->
        GitHub release vX.Y.Z -> bump site version -> [optional winget PR] -> commit + push.
  Prereqs: .NET SDK, Inno Setup 6 (ISCC), gh (logged in); for -Winget also wingetcreate.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
  [string]$Notes = "",
  [switch]$Winget,
  [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$Repo   = 'wmarcelod/utevo-lux'
$Root   = Split-Path -Parent $PSScriptRoot
$Proj   = Join-Path $Root 'UtevoLux\UtevoLux.csproj'
$Iss    = Join-Path $Root 'packaging\UtevoLux.iss'
$Site   = Join-Path $Root 'docs\index.html'
$Pub    = Join-Path $env:TEMP 'UtevoLux-publish'
$OutDir = $env:TEMP
$Setup  = Join-Path $OutDir 'UtevoLux-Setup.exe'
$Zip    = Join-Path $OutDir 'UtevoLux-win-x64.zip'
$Tag    = "v$Version"
$Iscc   = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
            Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Iscc) { throw "ISCC (Inno Setup 6) not found. Install it: winget install JRSoftware.InnoSetup" }

function Step($m) { Write-Host "`n== $m ==" -ForegroundColor Cyan }

Step "Utevo Lux $Tag  (DryRun=$DryRun  Winget=$Winget)"

# 1) bump <Version> in the csproj (the updater compares this to the release tag)
Step "Bump version -> $Version"
(Get-Content $Proj -Raw) -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>" |
    Set-Content $Proj -NoNewline -Encoding UTF8

# 2) publish self-contained win-x64
Step "dotnet publish (Release, self-contained)"
Get-Process UtevoLux -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
if (Test-Path $Pub) { Remove-Item $Pub -Recurse -Force }
dotnet publish $Proj -c Release -r win-x64 --self-contained true -o $Pub -v quiet
if (-not (Test-Path (Join-Path $Pub 'UtevoLux.exe'))) { throw "publish produced no UtevoLux.exe" }

# 3) build installer (version + source folder passed on the ISCC command line)
Step "Inno Setup installer"
& $Iscc "/DMyAppVersion=$Version" "/DSrcDir=$Pub" "/O$OutDir" "/FUtevoLux-Setup" $Iss | Select-Object -Last 1
if (-not (Test-Path $Setup)) { throw "installer not produced" }

# 4) portable zip + hash
Step "Zip + hash"
Compress-Archive -Path (Join-Path $Pub '*') -DestinationPath $Zip -Force
$Sha = (Get-FileHash $Setup -Algorithm SHA256).Hash
Write-Host ("installer {0:N1} MB  sha256={1}" -f ((Get-Item $Setup).Length/1MB), $Sha)

# 5) bump the version string shown on the site
if (Test-Path $Site) {
    (Get-Content $Site -Raw) -replace 'v\d+\.\d+\.\d+ . instalador', "v$Version `u{00B7} instalador" |
        Set-Content $Site -NoNewline -Encoding UTF8
}

if ($DryRun) {
    Step "DryRun: skipping GitHub release, winget and git push"
    Write-Host "Built:`n  $Setup`n  $Zip"
    return
}

# 6) GitHub release (becomes 'latest' -> the in-app updater + site download point here)
Step "GitHub release $Tag"
if (-not $Notes) { $Notes = "Utevo Lux $Tag" }
gh release create $Tag $Setup $Zip -R $Repo --title "Utevo Lux $Tag" --notes $Notes

# 7) winget PR (optional; occasional — every PR is reviewed by Microsoft)
if ($Winget) {
    Step "winget manifest + submit"
    $WDir = Join-Path $Root "winget\manifests\w\wmarcelod\UtevoLux\$Version"
    New-Item -ItemType Directory -Force -Path $WDir | Out-Null
    $Url  = "https://github.com/wmarcelod/utevo-lux/releases/download/$Tag/UtevoLux-Setup.exe"
    $Date = Get-Date -Format 'yyyy-MM-dd'
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json
PackageIdentifier: wmarcelod.UtevoLux
PackageVersion: $Version
InstallerLocale: en-US
InstallerType: inno
Scope: user
InstallModes:
  - interactive
  - silent
  - silentWithProgress
ReleaseDate: $Date
Installers:
  - Architecture: x64
    InstallerUrl: $Url
    InstallerSha256: $Sha
ManifestType: installer
ManifestVersion: 1.6.0
"@ | Set-Content (Join-Path $WDir 'wmarcelod.UtevoLux.installer.yaml') -Encoding UTF8
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json
PackageIdentifier: wmarcelod.UtevoLux
PackageVersion: $Version
PackageLocale: en-US
Publisher: wmarcelod
PublisherUrl: https://github.com/wmarcelod
PublisherSupportUrl: https://github.com/wmarcelod/utevo-lux/issues
PackageName: Utevo Lux
PackageUrl: https://github.com/wmarcelod/utevo-lux
License: Freeware
Copyright: (c) wmarcelod
ShortDescription: Overlay and companion for Tibia - mirror parts of your client (cooldowns, items, buffs) in floating windows, plus a world map with spawns and loot.
Moniker: utevo-lux
Tags:
  - tibia
  - overlay
  - mirror
  - gaming
  - map
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@ | Set-Content (Join-Path $WDir 'wmarcelod.UtevoLux.locale.en-US.yaml') -Encoding UTF8
@"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json
PackageIdentifier: wmarcelod.UtevoLux
PackageVersion: $Version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
"@ | Set-Content (Join-Path $WDir 'wmarcelod.UtevoLux.yaml') -Encoding UTF8
    wingetcreate submit $WDir --token (gh auth token)
}

# 8) commit + push (site redeploys via GitHub Pages)
Step "Commit + push"
git -C $Root add UtevoLux/UtevoLux.csproj docs/index.html packaging/UtevoLux.iss winget 2>$null
git -C $Root commit -m "Release $Tag" | Out-Null
git -C $Root push | Out-Null

Write-Host "`nDone -> https://github.com/$Repo/releases/tag/$Tag" -ForegroundColor Green
