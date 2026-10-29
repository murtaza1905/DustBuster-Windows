# Changelog

All notable changes to DustBuster for Windows are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- `DustBuster.Core` matching engine: search terms with whole-word name matching and channel detection, Windows path classifier (AppData, ProgramData, MSIX packages, Start Menu, registry hives, startup entries, scheduled tasks, services), leftover scanner with deterministic sources (uninstall key, product code, package family name, shortcuts, Run entries, tasks and services pointing into the install folder), exact-path lookups, directory and registry scan, per-app `conditions.json`, vendor-folder downgrade and empty-folder handling.
- `rules.json` cleanup policy: temporary files, crash dumps and error reports, GPU shader caches, thumbnail and icon caches, per-package caches, app cache folders under AppData, Chromium (including split profile/cache roots) and Firefox-family browser caches, and a separate system section (Windows Update downloads, `Windows\Temp`, system error reports).
- JSON test-case format replayed against a virtual disk and registry; first five fixtures; xUnit suite.
- Repository standard: README, MIT license, CHANGELOG, CONTRIBUTING, SECURITY, PRIVACY, THIRD_PARTY_NOTICES, issue and pull request templates, CI on Windows and Linux.
- `scripts\bootstrap.ps1` (installs the .NET SDK, Git and Inno Setup with winget) and `scripts\build.ps1`.

[Unreleased]: https://github.com/murtaza1905/DustBuster-Windows/commits/main
