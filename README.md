# Ubisoft Connect cache importer for Playnite

An unofficial Playnite library extension that imports installed and uninstalled Ubisoft Connect games from the local client cache.

## What it does

- Reads the legacy plaintext `cache/configuration/configurations` cache.
- Reads the encrypted `%LOCALAPPDATA%\Ubisoft Game Launcher\cache\configuration\configurations.vdat` cache.
- Uses the per-user DPAPI key in `%LOCALAPPDATA%\Ubisoft Game Launcher\store` and Windows CNG AES-256-GCM to decrypt `.vdat` locally.
- Imports installed titles from Ubisoft's Windows install registry when available.
- Adds `uplay://launch/<id>` Play actions.

The extension makes no Ubisoft network requests and does not include account credentials, cache contents, or DPAPI keys. Each user needs Ubisoft Connect installed and must have run it while signed in so the local cache exists. Ubisoft's cache format is private and may change in future client versions.

## Install

1. Download `UbisoftConnectImporter.pext` from the repository's Releases page.
2. Open it with Playnite (or drag it onto the Playnite window) and restart Playnite.
3. Enable **Ubisoft Connect (Cache Compatible)** under **Add-ons → Extension settings → Libraries**.
4. Disable the built-in Ubisoft Connect integration to avoid duplicate imports, then run **Update Game Library**.

## Build from source

Requires the .NET SDK, Windows, and a Playnite installation. The build script uses the installed Playnite SDK and Windows .NET Framework reference assemblies:

```powershell
.\build.ps1 -PlayniteInstallDir 'D:\Games\Playnite'
```

The package is written to `dist/UbisoftConnectImporter.pext`.

## Development status

The addon was built against Playnite SDK 6.18 and confirmed to import the author's Ubisoft library. It is an independent community extension and is not affiliated with Ubisoft or Playnite.

## License

Source and the packaged extension are released under the MIT License; see [LICENSE.md](LICENSE.md).
