# Privacy Policy

**DustBuster for Windows** — last updated 29 October 2026

DustBuster is a free, open-source Windows application developed by Murat Onurcu. This policy describes what the application does with data on your PC.

## Summary

DustBuster does not collect, store, transmit or share any personal data. It has no user accounts, no analytics, no advertising, no crash reporting service and no network access at runtime.

## Data stored on your PC

- The undo history of the last 50 removed items (original path, location in the Recycle Bin or the exported `.reg` file, size) in the application's settings folder under `%LOCALAPPDATA%\DustBuster`, so that they can be restored from the app. `scripts\uninstall.ps1` removes it.
- Scan results (file paths, registry keys, sizes, dates) are held in memory while the app runs. Reports and JSON test cases are written only when you export them, to the location you choose; file paths in them refer to your own PC, so review them before sharing.
- Diagnostic logs stay on the device. The optional export in Settings sanitizes usernames and home-folder paths.

All of this stays on your PC. Nothing is uploaded anywhere.

## Data that leaves your PC

None. DustBuster never opens a network connection. It does not check for updates and never downloads rule data; all rules ship inside the app.

## Permissions

- **Administrator rights** — not required for everyday use. The app offers to restart elevated only when you choose the system section of the cleanup or machine-wide leftovers; it never elevates silently.

No location, camera, microphone, contacts or calendar access is used.

## Third-party components

Third-party data embedded in the app, if any, is listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) with its license. It is data, not code, and it is never updated over the network at runtime.

No analytics SDKs, no advertising SDKs, no crash reporters.

## Children's privacy

DustBuster does not collect data from anyone, including children.

## Changes to this policy

Changes are recorded in this file and in `CHANGELOG.md`. The version history of this document is available in the repository.

## Contact

Murat Onurcu — muratonurcu@outlook.com
