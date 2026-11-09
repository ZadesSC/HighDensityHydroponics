# Continuous sowing runtime regression

`ContinuousSowing.cs` is a trusted external adapter for
[RimWorldTestFramework](https://github.com/ZadesSC/RimWorldTestFramework).
It runs actual vanilla work-giver queries and sowing jobs in the game on both
supported engines. It is not included in the distributed mod.

Use the framework's installed `docs/extensions.md` and `docs/fixtures.md` to
configure legal standalone game references, the authorized launcher, a disposable
workspace, Harmony and the candidate HDH mod in that order. Set an
`extensionReferences` entry to the candidate engine's `HighDensityHydro.dll`.
Use `fixtureMode: "scenario"` and copy the `HdhSowingFixture.Contract` declaration
into `fixtureContract` (JSON uses camelCase property names).

If an existing Mods entry already exposes another HDH installation, preserve it.
Stage the candidate's runtime files under a distinct temporary package ID and
set `parameters.package` to that ID. Record the metadata change and verify that
the staged DLL hashes match the candidate. Do not enable the old and candidate
HDH packages together.

```sh
rwtf --config /absolute/local.json doctor --prepare-catalog-policy
rwtf --config /absolute/local.json build --version 1.6 --adapter /absolute/ContinuousSowing.cs
rwtf --config /absolute/local.json run --version 1.6 --case hdh.sowing --ticks 600 --timeout 300 --tag sow16
rwtf --config /absolute/local.json validate /absolute/workspace/runs/sow16
```

Repeat with 1.7 and its matching assembly reference. Each run includes checkpoint
save/reload during an unfinished sowing job. Preserve original logs, events,
integrity/cleanup records and failed attempts outside Git.

Checks cover:

- Eight successive jobs on the same surface tile within 250 ticks, with storage
  updated in the completion tick and vanilla Plants experience/records retained.
- An incomplete plant cannot be stored; interrupting its job gives no credit.
- Two pre-created jobs competing for the final slot cannot produce excess plants
  or extra completed-sowing credit; Growing begins immediately.
- Rare-tick recovery and duplicate-transfer rejection.
- Crop changes while sowing.
- Ordinary hydroponics receives no HDH completion toil and keeps its physical plant.
- An unfinished saved job restores the extra toil and completes exactly once.

The fixture explicitly forces bay power and temporarily accelerates human
PlantWorkSpeed, restoring the original stat definition in `finally`. Human needs
remain enabled. These are focused assigned-job checks, not power-network tests,
a compatibility matrix, normal-duration labor measurements or a performance benchmark.

## Power readout regression

`PowerReadout.cs` provides case `hdh.power-readout` with
`PowerReadoutFixture.Contract` (`hdh-power-readout`). Use the same standalone
configuration procedure above, replacing the fixture contract and adapter path.
It uses a charged native battery, conduits, a Quantum bay, a disconnected Quantum
bay and a vanilla standing-lamp control. PowerOn is never forced.

The case checks that the inspector retains exactly one vanilla `PowerNeeded`
line, including while off, without substituting `PowerConsumptionMode`. It also
checks the translated active-power requirement label, pending/completed flicks,
measured battery drain, density and built-in sunlamp changes, disconnection and
checkpoint reload. The native completed flick action is invoked directly in a
zero-pawn fixture; this does not test a colonist walking to the switch. Repeat on
1.6 and 1.7 with their matching assembly references.
