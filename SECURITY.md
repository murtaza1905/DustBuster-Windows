# Security Policy

## Supported versions

Only the latest release on the [Releases](https://github.com/murtaza1905/DustBuster-Windows/releases/latest) page receives fixes. Please update before reporting a problem.

## Reporting a vulnerability

Please do **not** open a public issue for security problems.

Email **muratonurcu@outlook.com** with:

- a description of the issue and its impact,
- steps to reproduce or a proof of concept,
- the DustBuster version and Windows version you tested on.

You will receive an acknowledgement within 7 days. Once the issue is confirmed, a fix is prepared and published as a new release, and the report is credited in `CHANGELOG.md` unless you prefer to stay anonymous.

## Security model

- DustBuster never deletes a file outright: everything it removes goes to the Recycle Bin through the shell API, and registry keys are exported to a `.reg` file before they are deleted, so both can be restored from the app.
- Protected user data (Documents, Desktop, Pictures, credentials and crypto stores, browser history, cookies, passwords, bookmarks) and Windows' own folders and keys (`Microsoft\Windows`, `Software\Microsoft`, `Classes`, `WindowsApps`, `Common Files`) are excluded by rules that apply whatever a matching layer proposes; see `rules.json` and `conditions.json` in `src\DustBuster.Core\Resources\`.
- The app runs without administrator rights. It asks to restart elevated only for the system section of the cleanup and for machine-wide leftovers (`HKLM`, `Program Files`, services, scheduled tasks), and says so before doing it.
- The app has no network code; all rules are embedded.
- Logs stay on the device, are sanitized of usernames and home paths, and are exported only when you click the button in Settings.
- Releases are built by GitHub Actions from the tagged commit; the workflow is in `.github/workflows/release.yml` and every build log is public.

## Verifying downloads

Every release ships a `SHA256SUMS.txt` file. To verify a download in PowerShell:

```powershell
cd $env:USERPROFILE\Downloads
Get-FileHash .\DustBuster-Setup-*.exe -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```
