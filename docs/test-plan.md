# Hybrid Test Expansion to 60% Coverage

## Summary
Current state after review: the repo already has a working RimWorld harness with good gameplay regression coverage, but there is no conventional unit-test project or numeric coverage pipeline, and the checked-in legacy mod project cannot build locally because its RimWorld/Harmony reference paths are stale. The plan is to keep the harness as the integration/regression layer, add a separate SDK-style coverage build target for the production source files, and extract a small set of deterministic rules from `Building_HighDensityHydro` and DBH glue so they can be covered with standard unit tests.

## Key Changes
- Add an additive SDK-style build project for the production mod source, targeting `net48` with portable PDBs, used only for unit tests and coverage.
- Resolve RimWorld/Harmony references from MSBuild properties `RimWorldDir` and `HarmonyDllPath`.
- Default those properties to the currently valid local paths from the harness manifest, but allow override via environment/MSBuild properties.
- Keep the existing legacy project/solution in place to avoid disrupting the current workflow.
- Add a conventional unit-test project using `xunit`, `Microsoft.NET.Test.Sdk`, and `coverlet` tooling, referencing the new SDK-style build target.
- Add a repo-level test runner script `Tests/run-tests.ps1`.
- `run-unit`: run the xUnit suite.
- `coverage`: run unit tests with Cobertura output and an HTML report.
- `run-all`: run unit tests first, then the existing harness `run-all`.
- Refactor production code only by extraction, not behavior change.
- Extract internal pure helpers for capacity/power math, growth math, regrowth estimation, scaling clamp behavior, glow-to-growth conversion, and DBH fuel-consumption decisions.
- Leave `Building_HighDensityHydro` responsible for RimWorld state access, ticking, spawn/despawn, and side effects.
- Add `InternalsVisibleTo` for the unit-test assembly.
- Exclude rendering and engine glue that is validated by harness tests from the numeric coverage target.
- Exclude `HDH_Graphics`.
- Exclude `ITab_HDHDetails`.
- Exclude `Building_HighDensityHydro.DrawAt`.
- Exclude `Building_HighDensityHydro.Print`.
- Update the legacy project’s compile include list if new production `.cs` files are added, so the harness-generated build continues to include them.

## Public APIs / Interfaces
- No user-facing mod behavior or content changes are planned.
- New repo-facing interface: `Tests/run-tests.ps1` with `run-unit`, `coverage`, and `run-all`.
- New internal helper types in the mod assembly, exposed to tests via `InternalsVisibleTo`; these are not public mod APIs.

## Test Plan
- Add unit tests for extracted pure logic.
- Capacity scaling, min/max clamp, current power cost, next power delta.
- Glow threshold handling, growth delta calculation, fertility/grow-days behavior, clamp-to-1 behavior.
- Regrowth timing estimate and "dies before next harvest" decision path.
- DBH water/fuel consumption decision logic, including recycler/no-recycler cases and zero/null-safe cases.
- Any extracted visible-count or simple deterministic helper that remains in coverage scope.
- Expand the harness suite for game-only behavior that should stay validated in RimWorld.
- Sowing stores only fully growing plants and ignores incomplete sowing.
- Harvest phase spawns plants into available occupied cells until internal storage is exhausted.
- Regrow harvest resets to sowing when the buffer is empty.
- Regrow harvest resets to sowing when lifespan would be exceeded before the next cycle.
- `DeSpawn(DestroyMode.Vanish)` clears internal plant state and spawned plants.
- `AdjustCapacity` clamps at zero and max and updates the live power component.
- Acceptance criteria:
- `.\Tests\run-tests.ps1 run-unit` passes.
- `.\Tests\run-tests.ps1 coverage` emits a Cobertura report plus HTML output and reports at least 60% line coverage for the `HighDensityHydro` assembly after the agreed exclusions.
- `.\Tests\run-harness-tests.ps1 run-all` remains green.

## Assumptions
- The 60% target applies to the mod assembly only, not `Tests` or `External`.
- Rendering and engine glue that require a live RimWorld runtime can be excluded from the numeric coverage target and remain validated by smoke and harness integration tests instead.
- No changes to the external harness submodule are planned unless the existing suite/reflection hooks prove insufficient; prefer repo-local test and runner changes first.
- The implementation should preserve current gameplay behavior and use extraction/refactoring only to improve testability.
