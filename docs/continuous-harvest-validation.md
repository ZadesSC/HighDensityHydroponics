# Continuous harvest validation

The candidate refills an HDH surface tile as soon as vanilla `Plant.PlantCollected`
finishes, instead of waiting for the bay's next rare tick. If the collector is in a
vanilla harvest job, the replacement plant is added to that job's target queue,
the same queue vanilla itself appends stumps and blighted neighbours to. Work
discovery, harvest labor, yield, experience and records remain vanilla.

- Continuation stops at vanilla's 2400-work harvest batch, so pawns still end jobs
  regularly to eat, rest and re-evaluate work. The batch counter is keyed by job
  driver because vanilla pools `Job` objects. It is not saved; after a reload the
  restored job can collect at most one more batch.
- Regrowing crops are buffered immediately, the same way the rare tick did.
- The rare tick remains as recovery. It now also waits until every surface plant is
  collected before ending the harvest. Previously, when it spawned the last stored
  plants it could end the stage with that final wave still on the surface.

## Executed checks, 2026-11-17

Both projects built through the shared packaging recipe with zero warnings/errors.
`Tests/Rwtf/ContinuousHarvest.cs` ran through RimWorldTestFramework against the
standalone engines. Each accepted run passed 40 checks, checkpoint reload and
framework validation with zero failures.

- 1.6 (1.6.4871): `harv16-g`.
- 1.7 (1.7.4985): `harv17-d`, `harv17-e`.
- The existing `hdh.sowing` case also passed on 1.6 (`sow16-a`) with this build.

On 1.7, the Harmony Workshop update installed on 2026-11-16 fails at startup with
a `HarmonyMod.RuntimeIL` type-load error before any test code runs. The 1.7 runs
used the 2026-11-08 Harmony copy that matches the PR #21 1.7 runs. The test
install's Harmony link was pointed at it for those runs and then restored.

The fixture forces bay power and a Harvest stage and accelerates PlantWorkSpeed.
These are functional checks, not a performance benchmark or mod-compatibility
matrix. The unit test project builds, but its net48 runner needs Mono, which was
not installed on this machine.

## Recording

The PR recording is a live rendered 1.6 diagnostic session. One autonomous
colonist with only Growing enabled clears a harvest-ready mini bay: 16 stored
rice plants in two vanilla jobs (12 + 4) over about 450 ticks, with the bay
returning to Sowing at the end. Frames were captured every four ticks. The clock
was moved to midday and outdoor temperature pinned to 21 C for readability, and
plant work was accelerated. The recording is a visual demonstration, not a
scored run.

Evidence (configs, logs, runs, frames and the recording adapter) is kept outside
Git under `/Users/darren/codex_workspace/hdh-harvest-20261117/`.
