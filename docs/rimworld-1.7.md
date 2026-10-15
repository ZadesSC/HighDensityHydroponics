# RimWorld 1.7 compatibility

Validated on macOS Apple Silicon against Steam unstable **1.7.4977 rev1248**, on October 13, 2026, with the installed Harmony **3.0.0.0** assembly.

## Changes

- Added `HighDensityHydro.1.7.csproj`, targeting .NET 10 and referencing the installed `Assembly-CSharp`, `UnityStubs`, `Waylith`, and Harmony assemblies. Default paths support the macOS Steam installation; MSBuild properties/environment variables support other locations.
- Added 1.7 to the mod metadata.
- Added version-specific assembly loading. The original 1.6 DLL is preserved byte-for-byte in `1.6/Assemblies`, and the new DLL is in `1.7/Assemblies`. Definitions, patches, textures, and translations remain shared.
- Redirected the existing 1.6 projects' output to `1.6/Assemblies` so future builds do not introduce a second DLL in the common root.

No gameplay C# changes were required. Inspection of the installed game assembly confirmed the patched sowing signatures and the `Thing.Print(SectionLayer)` drawing override. In 1.7, `SectionLayer` inherits from `MapDrawLayer`, which the mesh printer accepts. The Unity compatibility types support the existing drawing code.

## Validation

The Release build completed with zero warnings and zero errors. The existing test suite and external harness were neither changed nor run.

A temporary diagnostic mod outside the repository ran inside the actual game using an isolated save/config directory. The final run included Core and all five installed expansions, Harmony, HDH, and the temporary probe. It verified:

- The game loaded the DLL from `1.7/Assemblies`, and both sowing Harmony patches registered.
- All four basin types spawned in all four rotations, with usable gizmo enumeration and inspection text.
- All 16 basins executed plant mesh printing; real-time drawing also executed with crops in the growing phase.
- Built-in lamps produced full plant light across every occupied cell.
- Quantum capacity adjustment, mature-plant spawning at harvest, crop reset, storage of completed sown plants, and transition into the growing phase worked.
- Disabling the sunlamp removed its helper lights.
- Saving and reloading preserved crop counts, growth, and exactly one helper light per occupied cell.
- Despawning cleared stored crops and removed helper lights.

The final smoke run completed with no error entries in its captured game log. The temporary probe was removed from the game's mod directory afterward.

## Limits

The probe seeded crop states and invoked lifecycle methods; this was not a full pawn-driven growing season or a long-running colony test. Drawing callbacks were exercised, but screenshots could not be captured in this session, so visual appearance was not independently reviewed. Existing 1.6 colony saves, temperature-bypass behavior under extreme conditions, and Dubs Bad Hygiene/Farming Hysteresis integration still need gameplay verification. The locally installed DBH package declared support through 1.6 only and was not enabled for this run.

The preserved 1.6 binary was not rebuilt or runtime-tested. Future unstable builds may require another compatibility check.
