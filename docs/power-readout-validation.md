# Issue #15: vanilla-consistent power readouts

The Quantum inspector previously replaced vanilla's **Power needed** line with
**Current power use**, even when the bay was off. The details tab also described
configured demand as current usage. Runtime investigation found that completed
flicks already stopped grid consumption correctly.

The inspector now synchronizes configured output before obtaining the native
inspection text and retains vanilla's own power readout. The details tab says
**Power required when active** and uses the calculated active demand. English,
Simplified Chinese and Traditional Chinese keys were updated together. Power
formulas, switch behavior and grid accounting were not changed.

## Validation

- RimWorld 1.6: `readout16-a`, 72 passing checks, checkpoint reload and host validation passed.
- RimWorld 1.7: `readout17-a`, 72 passing checks, checkpoint reload and host validation passed.
- Both projects built through the existing package recipe with no warnings/errors.
- All three keyed translation files parse and have matching key sets; the renamed
  label retains its formatting placeholder.
- The committed `Tests/Rwtf/PowerReadout.cs` case checks one native PowerNeeded line,
  no substituted current-use line, disconnected status, pending/completed flicks,
  battery drain, density changes, the separate sunlamp toggle and save/load.
- PowerOn is never forced. A real charged battery and native conduits supply the
  network. The completed native flick action is invoked directly in a zero-pawn
  fixture; this does not test a colonist walking to the switch.
- Both clean runs retain unchanged inputs/Mods and successful owned-process cleanup,
  with positive evidence that Steam remained uninitialized. Live Control is absent
  from the clean regression runs.

The test candidate uses temporary package ID `local.hdh.issue15.candidate` to
preserve the pre-existing Workshop installation. Other runtime content matches
this branch. The tested gameplay DLL hashes are:

```text
1.6 34db9e04908d8500f925f96a3a563fdcd59c233a4fc3dcb7c82869a5cf338b1f
1.7 71c5baef31d8cdaf29032c296b68b2d28efa3bb61127b8f953481481f97e094d
```

## In-game comparison images

![Inspector comparison](https://github.com/user-attachments/assets/26cef362-3f1a-4b3d-95f4-63dd7927f2e0)

Left column: powered on. Right column: physically switched off. Rows show the
Quantum bay, vanilla hydroponics basin and vanilla standing lamp. All retain
**Power needed** while off; the isolated grid's consumption drops to zero.
The different wattages are their configured requirements, not changed power costs.

![HDH details while off](https://github.com/user-attachments/assets/f406bb75-4101-4423-9daa-7b61c7a91e39)

The HDH details tab explicitly identifies the active power requirement while
its inspector reports zero grid demand.

These are cropped original game-rendered PNGs from standalone 1.6 diagnostic
session `readout-images16-c`, captured at real end-of-frame boundaries. Every
retained crop was visually inspected. No UI was redrawn or generated. The normal
first-use flick tutorial was acknowledged in the disposable capture profile so
it did not cover the readouts. The session had no runtime error callbacks and
passed input/Mods/owned-process cleanup. Full-frame originals, capture receipts,
exact switch/grid states, logs and adapter source are retained outside Git at
`/Users/darren/codex_workspace/hdh-issue15-fix/`. Earlier blocked-image and adapter
attempts are retained separately and are not the published screenshots. Individual
crops are attached directly to [PR #17](https://github.com/ZadesSC/HighDensityHydroponics/pull/17) for full-resolution viewing; the PNGs are not stored in Git.

The images are diagnostic evidence; the two clean batch cases supply the
regression verdict. Wider mod-combination and native-memory behavior are outside
this wording change's tested scope. 
