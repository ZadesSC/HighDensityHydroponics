# Quantum transition and VEF crop validation

PR #21 gameplay source at `0260f1a5eb5f6f961ddc9cd5c90894527c55739c`
was built and tested on Darren's MacBook Air on 2026-11-15 (Pacific time).
Both 1.6 and 1.7 Release builds completed with zero warnings/errors. The
observed loaded HDH DLL source-file hashes matched the candidate builds.

The continuous-sowing runs `sow16-c` and `sow17-b` passed all 65 checks and
public framework validation with VEF absent. Coverage includes repeated
same-cell sowing, vanilla experience/records, interruption, final-slot
competition, rare recovery, duplicate rejection, crop changes, vanilla grower
isolation, and unfinished-job save/reload. The issue #20 case passed on both:
75 stored plants and two active sowing plants at capacity 80; reduction to 76;
one completion stored plant 76 and entered Growing; the excess job stopped,
with no surface plants or extra completed-sowing credit.

The exact committed adapter initially passed the density regression but
timed out on later travel. The accepted runs used an external copy with the
crop-change and active-reload start waits increased from 100 ticks/10 seconds
to 500 ticks/15 seconds. Those same bounded waits are now committed. Production
code was unchanged; original failed attempts are retained.

Compatibility run `crops16-f` passed all 105 checks and full validation.
Run `crops17-c` passed all 105 gameplay assertions, but framework identity
validation rejected VEF's `System.Buffers` binding to the game's runtime DLL
outside the VEF directory. The 1.7 compatibility result is therefore **not an
unqualified framework pass**. No binaries or validator rules were altered to
force acceptance. The installed framework requires an explicit supported
provenance adapter for this cross-root dependency binding.

Both compatibility runs covered the actual installed oxyalgae, taro,
watercress, lotus, water chestnut, melon, and lavender definitions. Real
assigned sowing jobs filled storage; internal growth reached maturity;
harvestable crops produced resources through real harvest jobs. Lavender has
no harvested product, so its mature surface presentation was checked instead.
Quantum's Ground and extra tags, unchanged other-grower eligibility, and
active-job reload were checked. Oxyalgae's existing eligibility in ordinary
hydroponics was preserved.

These are functional tests with forced power, accelerated plant work, and
completed research. Compatibility maturity was accelerated through repeated
direct calls to the shipped growth/harvest handlers, with explicit sky/glow
refreshes. Natural-duration growth, network power behavior, UI clicking,
water chestnut's custom heat bonus, and arbitrary custom periodic plant
effects are not established by these runs.

The runtime engines were 1.6.4871 (Version.txt rev595, loaded VersionControl
rev597; both retained) and 1.7.4985 rev1269. Builds used the repository's
configured reference packages 1.6.4871 and 1.7.4982-unstable. Installed VEF and
VGE used their version-specific folders; More Plants and Vanilla Plants
explicitly map 1.7 to their 1.6 content folders in their load-folder definitions.

All four final runs had zero unexpected typed game errors, positive
Steam-disabled evidence, and passing cleanup audits. All 14 attempts and
original failures remain outside Git. Player settings, existing Mods entries,
and the Mac's shared checkout were preserved. No PR was merged.

## Retained artifacts

Evidence root on the MacBook Air:
`/Users/darren/codex_workspace/hdh-pr21-0260f1a/`.
`report.txt`, `commands.txt`, `attempt-ledger.json`, `dependency-inventory.json`,
`final-cleanup.json`, and `SHA256SUMS-evidence` contain full identities,
interventions, exact commands, failures, and audits. Base runs are under
`runtime/runs/`; compatibility runs are under `compat-runtime/runs/`.

Installable package, containing both engines:
`source/dist/HighDensityHydroponics-dev.2026.11.15.21.1.g0260f1a5eb5f.zip`.

```text
Package SHA256 e92db6639b3dd53ad76bab5f9dc0f0f75308caeb721ee41d47abb1893096ffde
1.6 DLL SHA256 58222a05a1e3a317becc48da5be9856f3ce60d2c3d19f548fd9b6c63a90b05bf
1.7 DLL SHA256 ff4b23640b4956817ee52ed71f7d132c285d18f8982d1df7940dc03c832a5af8
```

The test staging changed only the package ID to avoid the pre-existing HDH
installation. Its DLLs matched the installable package. The original package
retains the normal HDH package ID. The package is retained locally on the Mac;
it has not been published as a release.
