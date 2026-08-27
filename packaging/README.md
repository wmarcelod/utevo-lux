# Packaging

- `UtevoLux.iss` — Inno Setup script that wraps the `dotnet publish -c Release -r win-x64 --self-contained`
  output into `UtevoLux-Setup.exe` (per-user install, no admin; Start Menu shortcut + uninstaller).
  Edit `#define SrcDir` to your local publish folder, then compile with `ISCC UtevoLux.iss`.
- `../winget/manifests/...` — winget community-repo manifests (submit to microsoft/winget-pkgs for
  `winget install wmarcelod.UtevoLux`).

Release flow: publish → build installer (ISCC) → upload `UtevoLux-Setup.exe` + `UtevoLux-win-x64.zip`
to a GitHub Release → bump the winget manifest version + InstallerSha256.
