param(
    [ValidateSet("run-static", "build-mod", "prepare-profile", "start-game", "monitor-game", "stop-game", "run-game", "run-all", "watch")]
    [string]$Command = "run-all"
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$harnessRoot = Join-Path $repoRoot "External\\ZadesRimWorldTestHarness"
$manifestPath = Join-Path $PSScriptRoot "highdensityhydro.harness.json"
$cliProject = Join-Path $harnessRoot "src\\RimworldTestHarness.Cli\\RimworldTestHarness.Cli.csproj"

if (-not (Test-Path $cliProject))
{
    throw "Harness CLI project not found at $cliProject. Initialize the ZadesRimWorldTestHarness reference first."
}

dotnet run --project $cliProject --configuration Release -- $Command $manifestPath
