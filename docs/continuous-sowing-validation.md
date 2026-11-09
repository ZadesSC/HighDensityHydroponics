# Issue #14: continuous sowing validation

The candidate appends one instant transfer toil to HDH's vanilla sowing job.
The ordinary work giver and sowing labor remain vanilla. Non-HDH sowing retains
its original toil sequence. No global job-cleanup hook or faster polling is used.

Surface plants, including unfinished plants, count as claimed capacity. A check
before vanilla spawns a sowing plant rejects stale jobs that no longer have room.
Completed plants transfer once, and filling capacity immediately starts Growing.
Rare-tick recovery skips reserved plants. A serialized marker restores the same
HDH toil sequence when RimWorld rebuilds jobs before loaded pawns have a map.

## Executed checks, 2026-11-08

Both projects built through the existing shared packaging recipe with zero build
warnings/errors. The committed adapter at `Tests/Rwtf/ContinuousSowing.cs` ran
through the installed RimWorldTestFramework public CLI against the standalone
1.6 and 1.7 engines. Both runs passed their full assertions, checkpoint reload,
host validation and cleanup audit with zero unexpected typed game errors.

- 1.6: `sow16-i`, eight repeated same-tile sowings in 145 ticks.
- 1.7: `sow17-a`, eight repeated same-tile sowings in 144 ticks.
- Each completion incremented storage in its completion tick and freed the tile.
- Vanilla Plants experience and Plants Sown records increased.
- Interruption, unfinished-plant refusal, duplicate transfer, crop change,
  rare recovery and final-slot competition passed.
- Ordinary hydroponics had no extra toil and retained its surface plant.
- An unfinished job survived checkpoint reload with the extra toil and stored
  exactly one completed plant afterward.

These timings establish removal of the 250-tick collection bottleneck in the
declared fast-worker fixture; they are not a performance benchmark. Power was
forced on, PlantWorkSpeed was temporarily increased, and human needs remained
enabled. Full mod compatibility, normal power-network behavior and automatic
preservation of arbitrary third-party completion callbacks are not claimed.

The standalone installations already expose a Workshop HDH package. Existing
entries were preserved; the candidate was staged with temporary package ID
`local.hdh.issue14.candidate`. Its runtime content matched the branch except for
that About.xml package ID. The loaded assembly hashes matched the branch:

```text
1.6 98b006477108736718582be4ddc8b19dd837ca7df50d616d85218461c5cf8e41
1.7 561cb1eaa92cb484dd9d0bcfec92dffb0bc10c5864f79c448bbab7f56b31defc
```

Evidence is retained outside Git under
`/Users/darren/codex_workspace/hdh-issue14/`, including the configs, installed
framework, build logs, candidate inventories and `runtime/runs/sow16-i` and
`runtime/runs/sow17-a`. Earlier failed adapter/setup attempts and the failing
pre-fix active-job reload run are retained separately. Both accepted runs show
positive Steam initialization failure evidence, unchanged inputs/Mods, stopped
owned game/supervisor processes and successful cleanup.

## Independent review and recording

A separate read-only agent reviewed gameplay code at
`d76f835b95a9fbf07af79ce7c85431c8e6907570`, checked the relevant vanilla code,
and independently reran public framework validation for both retained runs.
It found no blocking defects; both runs have 59 passing checks and no failures.
The GitHub package check also passed for that commit. This is an agent review,
not a formal GitHub approval.

[Continuous sowing GIF](media/continuous-sowing.gif) contains 32 actual game-rendered
frames captured every five simulation ticks in a separate standalone 1.6 live
diagnostic session (`sow-demo16-e`). A grower with Growing enabled found its own
vanilla jobs and filled the bay from 0 to 16 stored plants before leaving when
full. The first and final captured simulation ticks were 2 and 157. Plant work
was accelerated and power forced on; the GIF playback is slowed for readability.
It is a visual demonstration, not a scored or batch-certified live run. The two
clean batch cases above supply the regression verdict. The recording session
had no runtime error callbacks and passed its owned-process/input/Mods cleanup
audit. Its original PNGs, command journal, observer results and recording adapter
remain in the external evidence directory.
