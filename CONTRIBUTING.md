# Contributing

Thanks for taking the time. DustBuster for Windows is a small project maintained by one person; clear reports and small, focused pull requests are the most helpful contributions.

## Reporting a wrong match or a miss

The uninstaller and the cleanup are driven by data and verified by test cases. The most useful report is the exported report or JSON test case from the app (both sanitize your user name and home folder):

1. Run the scan that produced the wrong result.
2. Export the **report** (`.txt`) or the **test case** (`.json`).
3. Open an issue with the file attached and say which paths are wrong (listed but should not be, selected but should not be, or missing).

A confirmed case becomes a fixture under `tests\DustBuster.Core.Tests\Fixtures\` and stays there forever, so the same mistake cannot come back.

## Proposing a change

- Open an issue first for anything bigger than a typo, so the approach can be agreed before the work is done.
- Describe the problem you are trying to solve before the solution you have in mind.

## Development setup

.NET 8 SDK and Git; Visual Studio is optional. `scripts\bootstrap.ps1` installs what is missing with `winget`.

```powershell
git clone https://github.com/murtaza1905/DustBuster-Windows.git
cd DustBuster-Windows
.\scripts\bootstrap.ps1
.\scripts\build.ps1 -Test
dotnet test
```

Matching rules are data: `conditions.json` (per-app include/exclude patterns) and `rules.json` (cleanup policy) under `src\DustBuster.Core\Resources\`. New real-world cases go into `tests\DustBuster.Core.Tests\Fixtures\` as JSON test cases rather than into code. Keep the clone as a permanent hub; once the application exists, `scripts\install.ps1` pulls, builds, archives every build to `releases\` and reinstalls on top of the previous one.

## Pull request checklist

- The solution builds and `dotnet test` passes on Windows.
- New or changed matching behaviour comes with a JSON test case.
- `CHANGELOG.md` has an entry under **Unreleased**.
- No new third-party dependencies without discussion; data files carry their license in `Licenses\` and `THIRD_PARTY_NOTICES.md`.
- Commit messages explain *why*, not just *what*.

## Code style

- C# 12, nullable enabled, `.editorconfig` settings; four-space indentation.
- Paths in the core are normalized (`\` separators, no trailing separator) and compared case-insensitively; registry keys are written as `HKCU:\…` / `HKLM:\…`.
- User-facing strings in the app are Turkish; code comments may be Turkish or English.

## Releasing (maintainer)

Bump `Version`/`FileVersion` in `Directory.Build.props`, move the **Unreleased** section of `CHANGELOG.md` under the new version, commit, then either push a tag `vX.Y.Z` or run the **Release** workflow from the Actions tab with the version number. The workflow builds the installer and the portable zip, writes `SHA256SUMS.txt` and publishes the GitHub Release with the changelog section.
