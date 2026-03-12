param(
    [ValidateSet("run-unit", "coverage", "run-all")]
    [string]$Command = "run-all"
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$unitProject = Join-Path $repoRoot "Tests\HighDensityHydro.UnitTests\HighDensityHydro.UnitTests.csproj"
$coverageRoot = Join-Path $repoRoot "Tests\artifacts\coverage"
$coverageFile = Join-Path $coverageRoot "coverage.cobertura.xml"
$coverageHtml = Join-Path $coverageRoot "html"

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$WorkingDirectory = $repoRoot
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed: $FilePath $($Arguments -join ' ')"
    }
}

switch ($Command)
{
    "run-unit"
    {
        Invoke-Step "dotnet" @("test", $unitProject, "-c", "Release")
    }
    "coverage"
    {
        New-Item -ItemType Directory -Force -Path $coverageRoot | Out-Null
        Remove-Item $coverageFile -ErrorAction SilentlyContinue
        Remove-Item $coverageHtml -Recurse -Force -ErrorAction SilentlyContinue

        Invoke-Step "dotnet" @("tool", "restore")
        Invoke-Step "dotnet" @(
            "test",
            $unitProject,
            "-c", "Release",
            "/p:CollectCoverage=true",
            "/p:CoverletOutputFormat=cobertura",
            "/p:CoverletOutput=$coverageFile",
            "/p:Include=[HighDensityHydro]*",
            "/p:ExcludeByAttribute=ExcludeFromCodeCoverageAttribute"
        )
        Invoke-Step "dotnet" @(
            "tool",
            "run",
            "reportgenerator",
            "-reports:$coverageFile",
            "-targetdir:$coverageHtml",
            "-reporttypes:Html;TextSummary"
        )
        Get-Content (Join-Path $coverageHtml "Summary.txt")
    }
    "run-all"
    {
        Invoke-Step "powershell" @("-ExecutionPolicy", "Bypass", "-File", (Join-Path $repoRoot "Tests\run-tests.ps1"), "run-unit")
        Invoke-Step "powershell" @("-ExecutionPolicy", "Bypass", "-File", (Join-Path $repoRoot "Tests\run-harness-tests.ps1"), "run-all")
    }
}
