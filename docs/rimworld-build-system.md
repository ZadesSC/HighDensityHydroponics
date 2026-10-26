# Shared RimWorld build and release system

Implemented on 2026-10-26 for ZadesSC's mod repositories. This is the setup and maintenance reference for future agents. A versioned copy is kept in [HighDensityHydroponics](https://github.com/ZadesSC/HighDensityHydroponics/blob/master/docs/rimworld-build-system.md).

## Architecture and source of truth

Shared implementation lives in **ZadesSC/HighDensityHydroponics**:

- `.github/workflows/rimworld-ci.yml`: reusable validation, build, packaging, artifact upload, and run summary.
- `.github/workflows/rimworld-release.yml`: invokes the same package workflow, downloads its artifact, checks the checksums, and publishes those exact bytes.
- `tools/ci/rimworld_build.py`: standard-library Python implementation of version selection, assembly validation, packaging, and metadata.
- `tools/ci/test_rimworld_build.py`: contract/regression tests, run in each build.

The deployed shared implementation is pinned to **`25c60977b5dbd240e3624ea963e734699139cb43`**. Both the reusable-workflow `uses: ...@SHA` and its `tooling-ref: SHA` input must contain this same reviewed full commit SHA. The release workflow's local reference to the build workflow resolves at the shared commit automatically. The helper checkout retrieves only `tools/ci` from that commit.

Each consumer repository has:

- `.github/workflows/dev-build.yml`, displayed as **Build dev package**.
- `.github/workflows/release.yml`, displayed as **Create release**.
- `.github/rimworld-build.json`, describing that project's runtime files, assembly mode, and build commands.
- `.github/assembly-fingerprints.json` when packaging committed DLLs.
- Ignore rules for `/dist/`, `/.rimworld-ci/`, `/release-notes.txt`, and Python bytecode.

Use a shared implementation instead of copying the packaging logic into every mod. Project-specific compilation, dependencies, validators, and package content stay in the consumer config. Old `package.yml`/`prerelease.yml` workflows were replaced so tag pushes do not publish duplicate releases. Existing standalone build scripts remain available; their older naming rules are not the Actions contract.

## Repository coverage

| Repository | Default branch | Existing dev branch updated | Assembly mode |
| --- | --- | --- | --- |
| Demigryphs | main | yes | Committed DLL fingerprints |
| HighDensityBeds | main | yes | Compile both framework targets |
| Zades-Maidnoid-Combat-Extended-Patch | main | yes | Fingerprints plus existing runtime-build.json verification and static validators |
| ayameduki_races_ce_patch | master | yes | Committed DLL fingerprints |
| HighDensityAnimals | main | yes | Compile; preserve existing Python validator tests |
| ZadesMaidnoidExtensions | main | no dev branch exists | Compile |
| HighDensityHydroponics | master | no dev branch exists | Compile 1.6 and 1.7 projects |

Only build infrastructure was installed on those branches. Default/dev game content was not merged. Other feature branches were not rewritten; they receive the new workflows when rebased or merged. When merging dev into default, resolve build-config and fingerprint conflicts against the resulting content and actual supported versions.

## Naming and channels

ZIP: `<ModSlug>-<BuildVersion>.zip`. Actions artifact: the same stem without `.zip`.

Slugs are `Demigryphs`, `HighDensityBeds`, `ZadesMaidnoidCE`, `AyamedukiRacesCEPatch`, `HighDensityAnimals`, `ZadesMaidnoidExtensions`, and `HighDensityHydroponics`.

| Channel | Example | Destination |
| --- | --- | --- |
| Stable | `Demigryphs-v2026.10.26.zip` | GitHub Release, explicitly Latest |
| Additional stable release that day | `Demigryphs-v2026.10.26.1.zip` | New tag and release; never replace the first |
| Prerelease | `Demigryphs-v2026.10.26-beta.1.zip` | GitHub prerelease, explicitly not Latest |
| Development | `Demigryphs-dev.2026.10.26.7.1.gd16f8f910287.zip` | Actions artifact, 30-day retention |

Development identity contains Los Angeles date, workflow run number, run attempt, and the first 12 characters of the checked-out commit. On a PR this is normally the tested merge commit; metadata also records the PR's head SHA and number.

Stable/prerelease tags use `vYYYY.MM.DD[.N]`, with `-alpha.N`, `-beta.N`, or `-rc.N` for prereleases. Dates are validated. Blank manual versions use the current date in `America/Los_Angeles`, allocating the next unused stable suffix or beta number from existing tags. Explicit versions reject an existing tag; a tag-triggered run accepts its already-existing tag only at the packaged commit. Release publication is serialized within the repository. Concurrent pending release requests can be replaced by GitHub's concurrency scheduling; check the run rather than assuming every queued request publishes.

Mod release version and supported RimWorld versions are separate. Normally distribute all supported game versions together and describe them in release notes and the Actions build summary. Do not put `1.6-1.7` in filenames or change package IDs/DLL names to match the slug.

Existing semantic versions in `About.xml` and project files are preserved as internal mod/assembly versions. Calendar tags identify the distribution; they do not replace compiler/NuGet versions. The internal build summary includes `modVersion` separately. Existing historical releases/tags retain their original names.

## Triggers and release policy

Dev workflow: pushes to the default branch or `dev`, PRs targeting either, and manual dispatch. Superseded runs for the same branch/PR are cancelled. This workflow has `contents: read` and cannot publish.

Release workflow: manual dispatch with optional `version` and `channel: stable|prerelease`, or pushed `v*` tags. Stable manual releases require the configured default branch. Manual prereleases can also use `dev`. Tag-triggered stable commits must be ancestors of the default branch; prerelease tags can also be on dev. The selected date suffix must match the selected channel. The caller grants write permission only to its release job; the shared publisher alone uses `contents: write`.

The manual release workflow builds and publishes in the same run. It does not depend on creating a tag to trigger another workflow: events caused by `GITHUB_TOKEN` normally do not start a new workflow run. Do not introduce a PAT just to work around that behavior. The standard system requires no additional repository secrets.

Run examples:

```sh
gh workflow run dev-build.yml --repo ZadesSC/Demigryphs --ref dev
gh workflow run release.yml --repo ZadesSC/Demigryphs --ref main -f channel=stable
gh workflow run release.yml --repo ZadesSC/Demigryphs --ref dev -f channel=prerelease
```

The release commands publish actual GitHub releases. Keep that side effect in scope when choosing a smoke test. The GitHub UI provides the same controls under Actions → Create release → Run workflow.

## Package contract

```text
dist/
  ModSlug-vYYYY.MM.DD.zip
  SHA256SUMS

Inside the ZIP:
  ModSlug/
    About/About.xml
    LoadFolders.xml       # if the project uses it
    <runtime folders appropriate to this mod>
    <license/readme/credits files where present>
```

The extracted folder is always `ModSlug/`, independent of version/channel. Players should replace the previous folder before installing so removed files do not linger. Source archives automatically supplied by GitHub are not the supported installable download.

Packaging uses a project allowlist. It excludes `Source`, historical misspelling `Souce`, `packages`, `bin`, `obj`, `.git`, Python caches, symbols, and `.deps.json`. No third-party DLL can be included unless listed as a project's own runtime assembly. Keep that list restricted to the mod's own assemblies. Tests, evidence, workflow tooling, reference DLLs, and development docs are outside the allowlist. Required license/credit notices remain included. If separate source distributions become necessary, add them explicitly rather than expanding the runtime ZIP to the entire repository.

Every included XML file must parse. `About.xml` supported versions must match the config. If present, `LoadFolders.xml` must map exactly those versions, and every selected content folder must exist in the package, including conditional integrations. Required runtime DLLs must exist and be nonempty. The ZIP is reopened to verify CRC, entry names, and file bytes. Fixed timestamps, sorted entries, and fixed file permissions make the archive reproducible for identical runtime content. Run metadata is outside the ZIP, so it does not change the ZIP hash.

**Do not distribute `BUILD_INFO.json`.** Neither mod ZIPs, downloadable Actions artifacts, nor GitHub release assets contain it. Legacy copies under allowlisted content folders are explicitly excluded. Demigryphs' standalone dev packager follows the same policy.

The helper temporarily writes `.rimworld-ci/build-info.json` for the Actions job summary only. It records commit, ref, channel, supported game versions, internal mod version, assembly provenance/hashes, reference-package versions, tooling commit, run identity/URL, PR head, and final ZIP hash/file count. This internal file is not uploaded. `SHA256SUMS` covers only the mod ZIP.

The Actions artifact contains the mod ZIP, `SHA256SUMS`, and `release-notes.txt`, preserving the `dist/` directory. The publisher downloads it, verifies the checksum, and uploads only the ZIP and checksums. It obtains the mod slug from a workflow output, so it does not depend on a downloadable metadata file. It does not check out source or rebuild. Notes state compatibility, installation, commit, dependency names, and assembly mode, followed by generated GitHub release notes.

## Adding another mod

1. Read its AGENTS.md, inspect its current runtime layout, supported versions, actual DLL dependencies, and existing validators. Work in a clean checkout; leave unrelated changes and older historical game content intact.
2. Copy the two **consumer** workflows from an already migrated repository's current default branch. Use `dev-build.yml` and `release.yml`, not the reusable shared-workflow files. Adjust default-branch triggers. Keep `uses` and `tooling-ref` pinned to the same reviewed shared SHA. A dev branch is optional.
3. Create `.github/rimworld-build.json`. Start with a configuration like:

```json
{
  "schemaVersion": 1,
  "slug": "ExampleMod",
  "defaultBranch": "main",
  "supportedVersions": ["1.7"],
  "assemblyMode": "compile",
  "needsDotnet": true,
  "assemblies": ["1.7/Assemblies/ExampleMod.dll"],
  "include": ["About", "1.7", "LoadFolders.xml", "LICENSE", "README.md"],
  "commands": [["dotnet", "build", "Source/ExampleMod.csproj", "-c", "Release"]]
}
```

4. Adjust `include` to files/folders that actually exist and are required at runtime. Every allowlisted top-level path is required; remove absent optional files rather than silently accepting missing runtime inputs. Preserve the original mod's license obligations.
5. Prefer compile mode when references are available. Commands are argument arrays, executed without a shell from the consumer root. SDK-style NuGet projects can build directly, as Beds does. For projects using explicit reference paths, configure `references` entries with exact `package`, `version`, ZIP-entry `prefix`, and temporary `destination`, then pass properties containing `{references}`. See Animals/Extensions/Hydroponics current configs. References are downloaded to a temporary directory and never packaged. `needsDotnet` installs SDK **10.0.402**, pinned to the validated toolchain.
6. Compile mode deletes expected output DLLs before commands, preventing accidental reuse of a tracked DLL if a recipe does not produce it. For third-party references that cannot be supplied in CI, use committed mode as described below. Keep existing meaningful static validators and tests as commands.
7. Add ignore rules and remove/replace overlapping legacy publishing workflows. Install the workflow on the default branch so GitHub exposes manual dispatch, and also on existing dev if builds there should use it. Do not create a dev branch solely for this system.
8. Validate locally in a disposable checkout, lint Actions YAML, commit/push the infrastructure, and inspect GitHub's actual result. Download and inspect at least one artifact. Run a release only within the user's authorized publication scope.

If the shared host becomes private, first configure GitHub Actions access for reusable workflows and ensure the helper checkout can access it. The existing public host works without cross-repository secrets; do not assume a private move is transparent.

## Committed DLL mode

Set `assemblyMode: committed`, list all packaged own assemblies, and set `sourceGlobs` to the compilation inputs, including project files. Example:

```json
"sourceGlobs": ["Source/**/*.cs", "Source/**/*.csproj"]
```

The fingerprint manifest stores hashes of those sources/projects and runtime DLLs. Any source/DLL change fails validation until the maintainer rebuilds and validates the DLL, then refreshes the manifest. New/deleted matched files also change the source map. Add other compiler inputs to the patterns if used.

**A baseline is not proof that the initial DLL was compiled from the initial source.** It detects subsequent drift. Demigryphs and Ayameduki adoption snapshots were recorded from the existing committed state; CI does not claim to rebuild them. Maidnoid CE additionally retains its existing `runtime-build.json` verification. Metadata and release notes expose committed versus compile mode.

After actually rebuilding and validating, refresh with the shared helper:

```sh
python3 /path/to/shared/tools/ci/rimworld_build.py fingerprint --root /path/to/mod
```

Review the source and assembly changes together, and commit the refreshed manifest. Never use `fingerprint` simply to silence an unexplained failure. CI never refreshes it automatically.

## Local validation and maintenance

Use a checkout of the shared host at the pinned SHA and a disposable checkout of the mod:

```sh
python3 -m unittest discover -s /path/to/shared/tools/ci -p 'test_*.py'
python3 /path/to/shared/tools/ci/rimworld_build.py build --root /path/to/mod
actionlint .github/workflows/dev-build.yml .github/workflows/release.yml
```

The local build replaces configured compile-mode DLLs and regenerates `dist/`; it does not publish. Keep generated compile outputs out of infrastructure-only commits. Release version selection in Actions relies on a full checkout with tags and remote branches; do not replace it with a shallow checkout.

Use `RIMWORLD_BUILD_VERSION` for the download label. **Do not export `VERSION` into compiler commands:** MSBuild imports environment variables as properties, so `VERSION=dev.…` can become an invalid NuGet `Version` on projects without an explicit version. The helper strips case-insensitive `version` environment entries before running project commands, and a regression test covers this. The initial hosted failure appeared as MSB4181; the underlying NU1105 message was in `obj/project.assets.json`/`project.nuget.cache`, not normal console output.

To update shared tooling: change/test the helper and reusable workflows in Hydroponics, commit/push that implementation, then update **both** pin locations in consumer workflows to the new full SHA. Test a representative compiled mod and committed-DLL mod before completing the rollout. Run contract tests and actionlint, then inspect actual Actions output. Do not use a moving branch ref for shared production tooling.

This is build/package validation, not a claim of in-game testing. If a change requires laptop runtime tests, use `/Users/darren/Games/RimWorld-ModTesting/Game` and `python3 /Users/darren/codex_workspace/rimworld-standalone/launch.py` with a disposable absolute `-savedatafolder`. Keep Steam uninitialized and follow the workspace AGENTS.md. No game or Steam installation is needed for these Actions workflows.

## Build metadata removal verification

On 2026-10-26, all seven default branches and all five existing dev branches were repinned to shared commit `25c60977b5dbd240e3624ea963e734699139cb43`. All 12 hosted builds passed. Every downloaded Actions artifact contained only the mod ZIP, `SHA256SUMS`, and release notes; neither the artifact nor its mod ZIP contained build-info JSON. Every checksum and ZIP CRC verified. The 14 shared regression tests and actionlint passed, including checks that old metadata inside an allowlisted folder is excluded and that `dist/` contains only the ZIP/checksum.

| Repository | Branch | Verified build |
| --- | --- | --- |
| Demigryphs | main | [40401691229](https://github.com/ZadesSC/Demigryphs/actions/runs/40401691229) |
| HighDensityBeds | main | [40401694741](https://github.com/ZadesSC/HighDensityBeds/actions/runs/40401694741) |
| ZadesMaidnoidExtensions | main | [40401697216](https://github.com/ZadesSC/ZadesMaidnoidExtensions/actions/runs/40401697216) |
| HighDensityHydroponics | master | [40401700321](https://github.com/ZadesSC/HighDensityHydroponics/actions/runs/40401700321) |
| ayameduki_races_ce_patch | master | [40401704475](https://github.com/ZadesSC/ayameduki_races_ce_patch/actions/runs/40401704475) |
| HighDensityAnimals | main | [40401708915](https://github.com/ZadesSC/HighDensityAnimals/actions/runs/40401708915) |
| Zades-Maidnoid-Combat-Extended-Patch | main | [40401711953](https://github.com/ZadesSC/Zades-Maidnoid-Combat-Extended-Patch/actions/runs/40401711953) |
| Demigryphs | dev | [40401717086](https://github.com/ZadesSC/Demigryphs/actions/runs/40401717086) |
| HighDensityBeds | dev | [40401718455](https://github.com/ZadesSC/HighDensityBeds/actions/runs/40401718455) |
| ayameduki_races_ce_patch | dev | [40401722914](https://github.com/ZadesSC/ayameduki_races_ce_patch/actions/runs/40401722914) |
| HighDensityAnimals | dev | [40401725280](https://github.com/ZadesSC/HighDensityAnimals/actions/runs/40401725280) |
| Zades-Maidnoid-Combat-Extended-Patch | dev | [40401730170](https://github.com/ZadesSC/Zades-Maidnoid-Combat-Extended-Patch/actions/runs/40401730170) |

[Demigryphs v2026.10.26.3](https://github.com/ZadesSC/Demigryphs/releases/tag/v2026.10.26.3) was published by [release run 40401798480](https://github.com/ZadesSC/Demigryphs/actions/runs/40401798480) from merged main commit `34e8442f1726e822c65bf6a276781869cb3430be`, supporting RimWorld 1.6 and 1.7. Its only assets are `Demigryphs-v2026.10.26.3.zip` and `SHA256SUMS`. The ZIP contains 837 files and no build-info JSON. Its SHA256 is `4aaacc99259a9c98bed53495b2430e2f83f4c86fe6bcda57dfcfa3dd7b943783`; the published ZIP exactly matches the validated release artifact. Demigryphs' standalone packager was also checked to keep its metadata internal.

The original rollout evidence below predates this policy change and describes historical releases; those archives were not rewritten.

## Original Demigryphs end-to-end verification (historical)

The original manual runs completed successfully on 2026-10-26 using shared tooling `b1568f2acc098c2c3b89cdacea96a2b18ebe929c`:

- [Dev workflow, run 40399414457](https://github.com/ZadesSC/Demigryphs/actions/runs/40399414457), commit `b2b8b86b817b463a97779189f44eedd5d6f83cb6`, supports RimWorld 1.6 and 1.7.
- Dev ZIP: `Demigryphs-dev.2026.10.26.10.1.gb2b8b86b817b.zip`.
- Dev ZIP SHA256: `6933c5dfac3b07b33c5ad88c6e2eababb6a4210d0eaf8b88ce6a296660e62aa2`.
- [Stable release workflow, run 40399419296](https://github.com/ZadesSC/Demigryphs/actions/runs/40399419296), commit `7a8e3ca74414ff93e0cb17a41126ca710e56b004`, supports RimWorld 1.6, matching main.
- [Published release v2026.10.26.1](https://github.com/ZadesSC/Demigryphs/releases/tag/v2026.10.26.1), asset `Demigryphs-v2026.10.26.1.zip`.
- Release ZIP SHA256: `af814ded0f84fd9529203ccf599a5add174933dcc6b804196f93583ec83508fb`.

Downloaded the dev artifact, release artifact, and published release assets. Verified both SHA256SUMS entries, ZIP CRC, stable extracted root, excluded source/dependency folders, metadata commit/tooling pin, About.xml compatibility, and every packaged DLL hash. The published ZIP hash exactly matches the pre-publication Actions artifact. Each package contains 838 files including build info. The `.1` release was automatically allocated after the earlier `v2026.10.26` smoke release; the earlier tag/assets were preserved.

### Rollout checks

At the original rollout, all 12 default/dev branch configurations passed local build/package validation, and all final hosted branch builds passed. The original 13 shared contract/regression tests passed, including calendar/channel validation, existing-tag collisions, branch restrictions, fingerprint drift, unexpected DLLs, load-folder mismatches, symlinks, stale compile output, MSBuild environment isolation, reproducibility, and metadata/checksums. All workflow files passed actionlint 1.7.12.

| Repository | Branch | Successful final rollout run |
| --- | --- | --- |
| Demigryphs | main | [40399105467](https://github.com/ZadesSC/Demigryphs/actions/runs/40399105467) |
| HighDensityBeds | main | [40399109155](https://github.com/ZadesSC/HighDensityBeds/actions/runs/40399109155) |
| ZadesMaidnoidExtensions | main | [40399110890](https://github.com/ZadesSC/ZadesMaidnoidExtensions/actions/runs/40399110890) |
| HighDensityHydroponics | master | [40399113506](https://github.com/ZadesSC/HighDensityHydroponics/actions/runs/40399113506) |
| ayameduki_races_ce_patch | master | [40399118508](https://github.com/ZadesSC/ayameduki_races_ce_patch/actions/runs/40399118508) |
| HighDensityAnimals | main | [40399122633](https://github.com/ZadesSC/HighDensityAnimals/actions/runs/40399122633) |
| Zades-Maidnoid-Combat-Extended-Patch | main | [40399125152](https://github.com/ZadesSC/Zades-Maidnoid-Combat-Extended-Patch/actions/runs/40399125152) |
| Demigryphs | dev | [40399129341](https://github.com/ZadesSC/Demigryphs/actions/runs/40399129341) |
| HighDensityBeds | dev | [40399132033](https://github.com/ZadesSC/HighDensityBeds/actions/runs/40399132033) |
| ayameduki_races_ce_patch | dev | [40399135505](https://github.com/ZadesSC/ayameduki_races_ce_patch/actions/runs/40399135505) |
| HighDensityAnimals | dev | [40399138756](https://github.com/ZadesSC/HighDensityAnimals/actions/runs/40399138756) |
| Zades-Maidnoid-Combat-Extended-Patch | dev | [40399142228](https://github.com/ZadesSC/Zades-Maidnoid-Combat-Extended-Patch/actions/runs/40399142228) |

These are build/package checks; no RimWorld runtime was launched. The production workflow recipes were validated on GitHub-hosted Ubuntu 24.04, including both Hydroponics and Beds framework targets.

The workspace copy lives under `docs/` because `/Users/darren/codex_workspace` itself is not a Git repository. The matching Hydroponics copy makes this reference available through GitHub.

## GitHub reference documentation

- [Reusable workflows](https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows).
- [Workflow artifact access and expiration](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts).
- [Workflow triggering and GITHUB_TOKEN](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow).
- [Managing releases](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository).
