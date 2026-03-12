# AGENTS.md

## Environment Notes

- If you are running inside WSL and need `dotnet`, invoke it through Windows PowerShell, for example: `powershell.exe -NoProfile -Command "dotnet test Tests/HighDensityHydro.UnitTests/HighDensityHydro.UnitTests.csproj"`.
- If you add or change in-game text, use localization keys instead of hardcoded player-facing strings, and update the translation files under `Languages/` for English, Chinese Simplified, and Chinese Traditional to keep them in sync.
