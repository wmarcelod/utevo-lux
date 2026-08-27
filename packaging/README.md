# Packaging & release

## Two loops — so you never pay the release cost on every change

**Development loop (fast, every change):**
- Edit → `dotnet build UtevoLux/UtevoLux.csproj` (or run the Debug build) → test with Tibia open.
- Commit to `master` as you go. **No** version bump, installer, or release for ordinary iteration.

**Release loop (occasional, batched):** when a batch is worth shipping, one command does everything:

```powershell
pwsh packaging/release.ps1 -Version 0.1.2 -Notes "What changed"
```

It bumps `<Version>`, publishes self-contained, builds the installer, zips, cuts the GitHub release
`v0.1.2`, bumps the version label on the site, commits and pushes. Everyone on 0.1.1+ then gets the
in-app update prompt on next launch (the app pulls the new installer from GitHub).

```powershell
# also refresh the winget package (occasional — each is a Microsoft-reviewed PR):
pwsh packaging/release.ps1 -Version 0.2.0 -Notes "..." -Winget

# dry run: build installer + zip locally, no release / no push (test the pipeline):
pwsh packaging/release.ps1 -Version 0.1.2 -DryRun
```

## What follows a release automatically vs. not
- **In-app updater** (`UtevoLux/Services/UpdateService.cs`) — the primary channel. Every GitHub
  release reaches every 0.1.1+ user on next launch. This is what makes frequent releases cheap.
- **Site** (utevo.marcelod.com.br) — the download button always points at `releases/latest`, so it
  needs nothing per release; the script only refreshes the version label text.
- **winget** — a separate, slower channel (Microsoft review + merge). Bump it for notable versions, not every one.
- **HTTPS / DNS** — one-time setup (Cloudflare CNAME + GitHub Pages cert). Nothing per release.

## Files
- `UtevoLux.iss` — Inno Setup script (per-user install, no admin; Start Menu + uninstaller).
  `MyAppVersion` and `SrcDir` are passed on the command line by `release.ps1`
  (`ISCC /DMyAppVersion=.. /DSrcDir=.. /O.. /F..`); standalone it falls back to the defaults at the top.
- `release.ps1` — the one-command release above.
- `../winget/manifests/w/wmarcelod/UtevoLux/<version>/` — winget community-repo manifests.

Prereqs: .NET SDK, Inno Setup 6 (`winget install JRSoftware.InnoSetup`), `gh` (logged in), and for
`-Winget` also `wingetcreate` (`winget install Microsoft.WingetCreate`).
