# AGENTS.md

## Environment Notes

- If you are running inside WSL and need `dotnet`, invoke it through Windows PowerShell, for example: `powershell.exe -NoProfile -Command "dotnet test Tests/HighDensityHydro.UnitTests/HighDensityHydro.UnitTests.csproj"`.
- If you add or change in-game text, use localization keys instead of hardcoded player-facing strings, and update the translation files under `Languages/` for English, Chinese Simplified, and Chinese Traditional to keep them in sync.

## Shared build and release automation

- This repository hosts the reusable build/release workflows and `tools/ci` implementation used by the RimWorld mods. Read `docs/rimworld-build-system.md` before changing it or onboarding another repository.
- Consumer workflows pin the shared workflow and helper checkout to the same full commit SHA. Publish and validate a shared implementation commit before updating consumer pins.
