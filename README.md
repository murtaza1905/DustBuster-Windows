<p align="center">
  <img src="docs/icon.png" width="128" height="128" alt="DustBuster icon">
</p>

<h1 align="center">DustBuster for Windows</h1>

<p align="center">
  Privacy-first system cleaner and app uninstaller for Windows. Browser and app caches, leftover files and registry keys, large files, running apps — everything stays on your PC and goes to the Recycle Bin, never deleted outright.
</p>

<p align="center">
  <a href="https://github.com/murtaza1905/DustBuster-Windows/actions/workflows/ci.yml"><img src="https://github.com/murtaza1905/DustBuster-Windows/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://github.com/murtaza1905/DustBuster-Windows/releases/latest"><img src="https://img.shields.io/github/v/release/murtaza1905/DustBuster-Windows?display_name=tag" alt="Latest release"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT"></a>
  <img src="https://img.shields.io/badge/Windows-10%2022H2%20%7C%2011-0078D4" alt="Windows 10 22H2 / 11">
  <img src="https://img.shields.io/badge/.NET-8-512BD4" alt=".NET 8">
</p>

> **Status:** early development. The matching engine (`DustBuster.Core`), its rule files and the test corpus are in place; the WinUI 3 application, installer and release pipeline follow in the next rounds. The macOS version lives at [DustBuster-macOS](https://github.com/murtaza1905/DustBuster-macOS).

<!-- Screenshots -->

## Features

- **System Cleanup** — temporary files, crash dumps and error reports, GPU shader caches, thumbnail and icon caches, per-package caches of installed apps, and the caches of every Chromium-based browser found on the PC (per profile), plus the Firefox family. Windows' own caches (Windows Update downloads, `Windows\Temp`, system error reports) are listed in a separate, unselected section that needs administrator rights.
- **App Uninstaller** — runs the program's own uninstaller first, then finds what it left behind: `AppData` folders, `ProgramData`, MSIX package data, registry keys under `HKCU`/`HKLM\Software`, startup entries, Start Menu and desktop shortcuts, scheduled tasks and services whose executables lived inside the install folder. Items that might be shared are listed under *Check* and left unselected.
- **Large Files**, **Running Apps** and a **Dashboard** for memory, disk and CPU.
- **Undo** — files go to the Recycle Bin, registry keys are exported before removal, and the last cleanup can be reverted from the app.
- **Report and test case export** — every scan can be exported as a plain-text report or as a JSON test case that replays in the test suite.

## Requirements

- Windows 10 22H2 or Windows 11, 64-bit.
- No administrator rights for everyday use. The app asks to restart elevated only for the system section of the cleanup and for machine-wide leftovers.
- The app is unsigned for now; SmartScreen will show a warning on first launch.

## Install

### Download

1. Get `DustBuster-Setup-<version>.exe` or the portable `DustBuster-<version>-win-x64.zip` from the [latest release](https://github.com/murtaza1905/DustBuster-Windows/releases/latest).
2. Run the installer, or unzip the portable build anywhere and start `DustBuster.exe`.

### Build from source

Requires the .NET 8 SDK and Git. `scripts\bootstrap.ps1` installs anything missing with `winget`.

```powershell
cd $env:USERPROFILE\Desktop
git clone https://github.com/murtaza1905/DustBuster-Windows.git
cd $env:USERPROFILE\Desktop\DustBuster-Windows
.\scripts\bootstrap.ps1
.\scripts\build.ps1 -Test
```

Keep the cloned folder as a permanent hub. Once the application exists, `scripts\install.ps1` pulls the latest code, builds a self-contained Release, archives the build to `releases\`, reinstalls it to `%LOCALAPPDATA%\Programs\DustBuster` and launches it; a failed build never touches the installed app.

```powershell
cd $env:USERPROFILE\Desktop\DustBuster-Windows
.\scripts\build.ps1                 # build the solution
.\scripts\build.ps1 -Test           # build and run the Core test suite
```

## How the uninstaller finds leftovers

Matching lives in a UI-independent library (`src\DustBuster.Core`) and runs in layers:

1. **Deterministic signals** — the `Uninstall` registry key, the MSI product code, the MSIX package family name, and every shortcut, startup entry, scheduled task and service whose executable lives inside the install folder. Preselected.
2. **Exact paths** — `AppData\Roaming|Local|LocalLow`, `ProgramData`, `HKCU`/`HKLM\Software` and the Start Menu looked up by the program's display name, product name, executable name and `Vendor\App` combination. Exact folder-name lookups, never substring searches.
3. **Directory and registry scan** — whole-word name matching inside the same roots. Name-only hits are listed under *Check*, unselected.
4. **Per-app conditions** (`src\DustBuster.Core\Resources\conditions.json`) — explicit `include` / `exclude` patterns for programs whose leftovers follow no convention. Adding a program is a data change, not a code change.
5. **Safety rules** — other programs from the same vendor, channel variants (Beta, Canary, PTB), Windows' own folders and keys (`Microsoft\Windows`, `Software\Microsoft`, `Classes`, credentials and crypto stores), WindowsApps and `Common Files`, and user data (Documents, Desktop, Pictures) are never preselected or never listed, whatever layer proposed them.

System Cleanup is driven by data as well (`src\DustBuster.Core\Resources\rules.json`): scanned roots and their listing mode, the app-cache folder names, forbidden roots, sensitive keywords, the vendor folder list and the browser definitions.

### Regression tests

Every real-world scan can be exported from the app as a JSON test case and dropped into `tests\DustBuster.Core.Tests\Fixtures\`; the test suite replays it against a virtual disk and registry and checks what must be selected, what must be listed unselected and what must never appear. CI runs the suite on Windows and Linux on every push.

```powershell
cd $env:USERPROFILE\Desktop\DustBuster-Windows
dotnet test
```

## Privacy

- No network access. All rules are embedded in the app; nothing is downloaded at runtime.
- Nothing is deleted outright: files go to the Recycle Bin, registry keys are exported to a `.reg` file before removal.
- Logs stay on the device and are sanitized of usernames and home paths; export is manual only.

See [PRIVACY.md](PRIVACY.md).

## Project layout

```
DustBuster.sln
Directory.Build.props            version, author, build settings shared by all projects
src/DustBuster.Core/             matching engine, cleanup policy, fixtures model (no UI)
  Resources/conditions.json      per-app include/exclude rules
  Resources/rules.json           cleanup roots, browser definitions, safety lists
tests/DustBuster.Core.Tests/     xUnit tests + Fixtures/*.json test cases
scripts/                         bootstrap.ps1, build.ps1 (install.ps1, make-installer.ps1, uninstall.ps1 follow with the app)
docs/                            icon and screenshots
design/                          icon master (1024 px)
```

## Development

- `.NET 8 SDK`; Visual Studio is optional. `dotnet build` and `dotnet test` work from the command line.
- Matching rules are data: `conditions.json` and `rules.json`. New real-world cases go into `tests\DustBuster.Core.Tests\Fixtures\` as JSON test cases rather than into code.
- CI (`.github/workflows/ci.yml`) builds the solution and runs the tests on `windows-latest` and `ubuntu-latest`; build errors surface as annotations on the commit.

## Contributing and support

- Bugs and feature ideas: [Issues](https://github.com/murtaza1905/DustBuster-Windows/issues) (templates are provided). For wrong matches or misses in the uninstaller, attach the exported `.txt` report or JSON test case.
- Security problems: see [SECURITY.md](SECURITY.md) — please do not open a public issue.
- Pull requests: see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE) — Copyright © 2026 Murat Onurcu. Third-party material is listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Author

**Murat Onurcu** — muratonurcu@outlook.com
