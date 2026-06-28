# Contributing to v2rayF

Thank you for your interest in improving v2rayF!

## How to contribute

1. **Fork** the repository on GitHub
2. **Clone** your fork locally
3. Create a **feature branch**: `git checkout -b feature/my-improvement`
4. Make your changes with clear commits
5. **Test** locally: `dotnet build` and manual smoke test
6. Open a **Pull Request** against `main` with a concise description

## Development setup

### Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or the version in `global.json`)
- PowerShell 7+ (for packaging scripts on Windows)

### Build

```bash
dotnet build
dotnet run --project src/v2rayF/v2rayF.csproj
```

### Package locally (optional)

Downloads Xray-core and builds all platform zips into `dist/`:

```powershell
pwsh -File scripts/package-all.ps1
```

## Code guidelines

- Match existing C# style and MVVM patterns (Avalonia + CommunityToolkit.Mvvm)
- Keep changes focused; avoid unrelated refactors in the same PR
- Update `CHANGELOG.md` under `[Unreleased]` for user-visible changes
- Do not commit Xray binaries, `dist/`, `bin/`, or `obj/`

## Pull request checklist

- [ ] Builds cleanly (`dotnet build`)
- [ ] No secrets or personal server links in commits
- [ ] README or docs updated if behavior changed
- [ ] CHANGELOG updated for notable changes

## Questions

Open a [Discussion](https://github.com/drmikecrypto/v2rayF/discussions) or an [Issue](https://github.com/drmikecrypto/v2rayF/issues) for questions before large refactors.
