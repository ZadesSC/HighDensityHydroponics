using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using RimworldTestHarness.Mod;
using Verse;

namespace HighDensityHydroponics.Tests;

public sealed class HighDensityHydroSuiteProvider : IHarnessSuiteProvider
{
    public string SuiteName => "HighDensityHydroponics";

    public IEnumerable<IHarnessTestCase> Create()
    {
        return new IHarnessTestCase[]
        {
            new HdhSpawnSmokeTest(),
            new HdhQuantumConfigTest(),
            new HdhQuantumScalingTest(),
            new HdhPlantSelectionResetTest(),
            new HdhSowingIgnoresIncompletePlantTest(),
            new HdhSowingToGrowingPhaseTest(),
            new HdhGrowingToHarvestPhaseTest(),
            new HdhHarvestSpawnsStoredPlantsTest(),
            new HdhHarvestToSowingPhaseTest(),
            new HdhHarvestToGrowingRegrowthPhaseTest(),
            new HdhGrowingStageEmptyResetTest(),
            new HdhNoPowerDamageTest(),
            new HdhPowerGrowthGateTest(),
            new HdhSaveLoadPersistenceTest(),
            new HdhDeSpawnResetTest(),
            new HdhDbhCompatibilityTest(),
        };
    }
}

internal sealed class HdhSpawnSmokeTest : IHarnessTestCase
{
    private Thing building;

    public string Name => "hdh.spawn-smoke";

    public void Start(HarnessTestContext context)
    {
        HdhReflection.ClearAll(context.Map);
        building = HdhReflection.SpawnBuilding("HDH_Hydroponics_Quantum", context.Map);
    }

    public HarnessTestStatus Tick(HarnessTestContext context, out string details, out string snapshotPath)
    {
        if (building == null)
        {
            details = "Failed to spawn HDH_Hydroponics_Quantum.";
            snapshotPath = null;
            return HarnessTestStatus.Failed;
        }

        details = "Spawned " + building.def.defName + " at " + building.Position;
        snapshotPath = context.WriteSnapshot("hdh-spawn-smoke", HdhReflection.SnapshotValues(building));
        return HarnessTestStatus.Passed;
    }
}

internal sealed class HdhQuantumScalingTest : IHarnessTestCase
{
    private Thing building;
    private int baseCapacity;
    private float basePower;

    public string Name => "hdh.quantum-scaling";

    public void Start(HarnessTestContext context)
    {
        HdhReflection.ClearAll(context.Map);
        building = HdhReflection.SpawnBuilding("HDH_Hydroponics_Quantum", context.Map);
        HdhReflection.PrimePowerComp(building);
        baseCapacity = HdhReflection.MaxPlantCapacity(building);
        basePower = HdhReflection.CurrentPowerCost(building);
        HdhReflection.AdjustCapacity(building, 3);
    }

    public HarnessTestStatus Tick(HarnessTestContext context, out string details, out string snapshotPath)
    {
        var nextCapacity = HdhReflection.MaxPlantCapacity(building);
        var nextPower = HdhReflection.CurrentPowerCost(building);
        snapshotPath = context.WriteSnapshot("hdh-quantum-scaling", HdhReflection.SnapshotValues(building));
        if (nextCapacity <= baseCapacity || nextPower <= basePower)
        {
            details = "Scaling did not increase both capacity and power cost.";
            return HarnessTestStatus.Failed;
        }

        details = "Capacity " + baseCapacity + " -> " + nextCapacity + ", power " + basePower.ToString("F1") + " -> " + nextPower.ToString("F1");
        return HarnessTestStatus.Passed;
    }
}

internal sealed class HdhQuantumConfigTest : IHarnessTestCase
{
    private Thing building;

    public string Name => "hdh.quantum-config";

    public void Start(HarnessTestContext context)
    {
        HdhReflection.ClearAll(context.Map);
        building = HdhReflection.SpawnBuilding("HDH_Hydroponics_Quantum", context.Map);
        HdhReflection.PrimePowerComp(building);
    }

    public HarnessTestStatus Tick(HarnessTestContext context, out string details, out string snapshotPath)
    {
        snapshotPath = context.WriteSnapshot("hdh-quantum-config", HdhReflection.SnapshotValues(building));
        var requiresLight = HdhReflection.RequiresLightCheck(building);
        var requiresTemperature = HdhReflection.RequiresTemperatureCheck(building);
        var requiresAtmosphere = HdhReflection.RequiresAtmosphereCheck(building);
        var powerScales = HdhReflection.PowerScalesCapacity(building);
        var plantsPerLayer = HdhReflection.PlantsPerLayer(building);
        var capacity = HdhReflection.MaxPlantCapacity(building);

        if (requiresLight || requiresTemperature || requiresAtmosphere || !powerScales || plantsPerLayer != 4 || capacity != 4)
        {
            details = "Quantum config flags/capacity do not match branch expectations.";
            return HarnessTestStatus.Failed;
        }

        details = "Quantum config flags and base capacity matched expected values.";
        return HarnessTestStatus.Passed;
    }
}

internal sealed class HdhPlantSelectionResetTest : IHarnessTestCase
{
    private Thing building;

    public string Name => "hdh.plant-selection-reset";

    public void Start(HarnessTestContext context)
    {
        HdhReflection.ClearAll(context.Map);
        building = HdhReflection.SpawnBuilding("HDH_Hydroponics_Quantum", context.Map);

        var rice = ThingDef.Named("Plant_Rice");
        HdhReflection.SeedState(
            building,
            rice,
            "Sowing",
            powered: true,
            storedPlants: 2,
            growth: 0f,
            age: 0,
            health: rice.BaseMaxHitPoints,
            storedPlantsBuffer: 0,
            averageHarvestGrowth: 0f);
        HdhReflection.SpawnPlantOnBuilding(building, rice, 0.5f, 500);
        HdhReflection.SetPlantDefToGrow(building, ThingDef.Named("Plant_Strawberry"));
    }

    public HarnessTestStatus Tick(HarnessTestContext context, out string details, out string snapshotPath)
    {
        HdhReflection.TickRare(building);
        snapshotPath = context.WriteSnapshot("hdh-plant-selection-reset", HdhReflection.SnapshotValues(building));

        var stage = HdhReflection.BayStageName(building);
        var currentPlant = HdhReflection.CurrentPlantDefName(building);
        var storedPlants = HdhReflection.StoredPlantCount(building);
        var spawnedPlantCount = HdhReflection.SpawnedPlantCount(building);

        if (!string.Equals(stage, "Sowing", StringComparison.Ordinal) ||
            !string.Equals(currentPlant, "Plant_Strawberry", StringComparison.Ordinal) ||
            storedPlants != 0 ||
            spawnedPlantCount != 0)
        {
            details = "Changing selected plant during sowing did not fully reset the HDH state.";
            return HarnessTestStatus.Failed;
        }

        details = "Changing selected plant during sowing reset stored state and cleared spawned plants.";
        return HarnessTestStatus.Passed;
    }
}

internal sealed class HdhSowingToGrowingPhaseTest : IHarnessTestCase
{
    private Thing building;

    public string Name => "hdh.phase-sowing-growing";

    public void Start(HarnessTestContext context)
    {
        HdhReflection.ClearAll(context.Map);
        building = HdhReflection.SpawnBuilding("HDH_Hydroponics_Quantum", context.Map);
        HdhReflection.PrimePowerComp(building);

        var plantDef = ThingDef.Named("Plant_Rice");
        HdhReflection.SeedState(
            building,
            plantDef,
            "Sowing",
            powered: true,
            storedPlants: HdhReflection.MaxPlantCapacity(building),
            growth: 0f,
            age: 0,
            health: plantDef.BaseMaxHitPoints,
            storedPlantsBuffer: 0,
            averageHarvestGrowth: 0f);
    }

    public HarnessTestStatus Tick(HarnessTestContext context, out string details, out string snapshotPath)
    {
        HdhReflection.TickRare(building);
        var stage = HdhReflection.BayStageName(building);
        snapshotPath = context.WriteSnapshot("hdh-phase-sowing-growing", HdhReflection.SnapshotValues(building));
        if (!string.Equals(stage, "Growing", StringComparison.Ordinal))
        {
            details = "Expected Sowing to transition to Growing, but stage is " + stage + ".";
            return HarnessTestStatus.Failed;
        }

        details = "Stage transitioned from Sowing to " + stage + ".";
        return HarnessTestStatus.Passed;
    }
}

internal sealed class HdhSowingIgnoresIncompletePlantTest : IHarnessTestCase
{
    private Thing building;

    public string Name => "hdh.sowing-ignore-incomplete";

    public void Start(HarnessTestContext context)
    {
        HdhReflection.ClearAll(context.Map);
        building = HdhReflection.SpawnBuilding("HDH_Hydroponics_Quantum", context.Map);
        var plantDef = ThingDef.Named("Plant_Rice");
        HdhReflection.SeedState(
            building,
            plantDef,
            "Sowing",
            powered: true,
            storedPlants: 0,
            growth: 0f,
            age: 0,
            health: plantDef.BaseMaxHitPoints,
            storedPlantsBuffer: 0,
            averageHarvestGrowth: 0f);
        HdhReflection.SpawnPlantOnBuilding(building, plantDef, 0f, 0);
    }

    public HarnessTestStatus Tick(HarnessTestContext context, out string details, out string snapshotPath)
    {
        HdhReflection.TickRare(building);
        snapshotPath = context.WriteSnapshot("hdh-sowing-ignore-incomplete", HdhReflection.SnapshotValues(building));

        if (HdhReflection.StoredPlantCount(building) != 0 || HdhReflection.SpawnedPlantCount(building) != 1)
        {
            details = "Incomplete sowing plant should remain spawned and should not be counted as stored.";
            return HarnessTestStatus.Failed;
        }

        details = "Incomplete sowing plant was ignored until it reached the growing life stage.";
        return HarnessTestStatus.Passed;
    }
}

internal sealed class HdhGrowingToHarvestPhaseTest : IHarnessTestCase
{
    private Thing building;

    public string Name => "hdh.phase-growing-harvest";

    public void Start(HarnessTestContext context)
    {
        HdhReflection.ClearAll(context.Map);
        building = HdhReflection.SpawnBuilding("HDH_Hydroponics_Quantum", context.Map);
        HdhReflection.SeedGrowingState(building, ThingDef.Named("Plant_Rice"), true, 4, 0.99f, 1000);
    }

    public HarnessTestStatus Tick(HarnessTestContext context, out string details, out string snapshotPath)
    {
        snapshotPath = null;
        var dayPct = GenLocalDate.DayPercent(building);
        if (dayPct < 0.25f || dayPct > 0.8f)
        {
            details = "Waiting for daytime window; current day pct " + dayPct.ToString("F2");
            return HarnessTestStatus.Running;
        }

        HdhReflection.TickLong(building);
        var stage = HdhReflection.BayStageName(building);
        snapshotPath = context.WriteSnapshot("hdh-phase-growing-harvest", HdhReflection.SnapshotValues(building));
        if (!string.Equals(stage, "Harvest", StringComparison.Ordinal))
        {
            details = "Expected Growing to transition to Harvest, but stage is " + stage + ".";
            return HarnessTestStatus.Failed;
        }

        details = "Stage transitioned from Growing to " + stage + " at growth " + HdhReflection.PlantGrowth(building).ToString("F3") + ".";
        return HarnessTestStatus.Passed;
    }
}

internal sealed class HdhHarvestToSowingPhaseTest : IHarnessTestCase
{
    private Thing building;

    public string Name => "hdh.phase-harvest-sowing";

    public void Start(HarnessTestContext context)
    {
        HdhReflection.ClearAll(context.Map);
        building = HdhReflection.SpawnBuilding("HDH_Hydroponics_Quantum", context.Map);
        var plantDef = ThingDef.Named("Plant_Rice");
        HdhReflection.SeedState(
            building,
            plantDef,
            "Harvest",
            powered: true,
            storedPlants: 0,
            growth: 1f,
            age: 2500,
            health: plantDef.BaseMaxHitPoints,
            storedPlantsBuffer: 0,
            averageHarvestGrowth: 0f);
    }

    public HarnessTestStatus Tick(HarnessTestContext context, out string details, out string snapshotPath)
    {
        HdhReflection.TickRare(building);
        var stage = HdhReflection.BayStageName(building);
        snapshotPath = context.WriteSnapshot("hdh-phase-harvest-sowing", HdhReflection.SnapshotValues(building));
        if (!string.Equals(stage, "Sowing", StringComparison.Ordinal))
        {
            details = "Expected Harvest to transition to Sowing for single-harvest plants, but stage is " + stage + ".";
            return HarnessTestStatus.Failed;
        }

        details = "Single-harvest phase reset to " + stage + ".";
        return HarnessTestStatus.Passed;
    }
}

internal sealed class HdhHarvestSpawnsStoredPlantsTest : IHarnessTestCase
{
    private Thing building;

    public string Name => "hdh.harvest-spawn-stored";

    public void Start(HarnessTestContext context)
    {
        HdhReflection.ClearAll(context.Map);
        building = HdhReflection.SpawnBuilding("HDH_Hydroponics_Quantum", context.Map);
        var plantDef = ThingDef.Named("Plant_Rice");
        HdhReflection.SeedState(
            building,
            plantDef,
            "Harvest",
            powered: true,
            storedPlants: 2,
            growth: 1f,
            age: 2500,
            health: plantDef.BaseMaxHitPoints,
            storedPlantsBuffer: 0,
            averageHarvestGrowth: 0f);
    }

    public HarnessTestStatus Tick(HarnessTestContext context, out string details, out string snapshotPath)
    {
        HdhReflection.TickRare(building);
        snapshotPath = context.WriteSnapshot("hdh-harvest-spawn-stored", HdhReflection.SnapshotValues(building));

        if (HdhReflection.StoredPlantCount(building) != 0 || HdhReflection.SpawnedPlantCount(building) != 2)
        {
            details = "Harvest phase did not spawn the stored plants into available cells.";
            return HarnessTestStatus.Failed;
        }

        details = "Harvest phase spawned stored plants into the hydro footprint before waiting for collection.";
        return HarnessTestStatus.Passed;
    }
}

internal sealed class HdhHarvestToGrowingRegrowthPhaseTest : IHarnessTestCase
{
    private Thing building;
    private bool harvestCollected;

    public string Name => "hdh.phase-harvest-regrow";

    public void Start(HarnessTestContext context)
    {
        HdhReflection.ClearAll(context.Map);
        building = HdhReflection.SpawnBuilding("HDH_Hydroponics_Quantum", context.Map);
        var plantDef = ThingDef.Named("Plant_Ambrosia");
        HdhReflection.SeedState(
            building,
            plantDef,
            "Harvest",
            powered: true,
            storedPlants: 0,
            growth: 1f,
            age: 2500,
            health: plantDef.BaseMaxHitPoints,
            storedPlantsBuffer: 0,
            averageHarvestGrowth: 0f);
        HdhReflection.SpawnPlantOnBuilding(building, plantDef, 0.45f, 2500);
    }

    public HarnessTestStatus Tick(HarnessTestContext context, out string details, out string snapshotPath)
    {
        HdhReflection.TickRare(building);
        var stage = HdhReflection.BayStageName(building);

        if (!harvestCollected)
        {
            harvestCollected = true;
            snapshotPath = context.WriteSnapshot("hdh-phase-harvest-regrow-buffered", HdhReflection.SnapshotValues(building));
            if (!string.Equals(stage, "Harvest", StringComparison.Ordinal) || HdhReflection.StoredPlantBufferCount(building) <= 0)
            {
                details = "Expected Harvest to buffer regrowth plants before transitioning; stage is " + stage + ".";
                return HarnessTestStatus.Failed;
            }

            details = "Buffered harvested regrowth plants; waiting for phase transition.";
            return HarnessTestStatus.Running;
        }

        snapshotPath = context.WriteSnapshot("hdh-phase-harvest-regrow", HdhReflection.SnapshotValues(building));
        if (!string.Equals(stage, "Growing", StringComparison.Ordinal))
        {
            details = "Expected Harvest to transition back to Growing for regrowable plants, but stage is " + stage + ".";
            return HarnessTestStatus.Failed;
        }

        details = "Regrowable harvest transitioned back to Growing with stored plants " + HdhReflection.StoredPlantCount(building) + ".";
        return HarnessTestStatus.Passed;
    }
}

internal sealed class HdhGrowingStageEmptyResetTest : IHarnessTestCase
{
    private Thing building;

    public string Name => "hdh.growing-empty-reset";

    public void Start(HarnessTestContext context)
    {
        HdhReflection.ClearAll(context.Map);
        building = HdhReflection.SpawnBuilding("HDH_Hydroponics_Quantum", context.Map);
        var plantDef = ThingDef.Named("Plant_Rice");
        HdhReflection.SeedState(
            building,
            plantDef,
            "Growing",
            powered: true,
            storedPlants: 0,
            growth: 0.55f,
            age: 1500,
            health: plantDef.BaseMaxHitPoints,
            storedPlantsBuffer: 0,
            averageHarvestGrowth: 0f);
    }

    public HarnessTestStatus Tick(HarnessTestContext context, out string details, out string snapshotPath)
    {
        HdhReflection.TickLong(building);
        snapshotPath = context.WriteSnapshot("hdh-growing-empty-reset", HdhReflection.SnapshotValues(building));

        if (!string.Equals(HdhReflection.BayStageName(building), "Sowing", StringComparison.Ordinal) ||
            HdhReflection.StoredPlantCount(building) != 0 ||
            Math.Abs(HdhReflection.PlantGrowth(building)) > 0.0001f)
        {
            details = "Growing state with zero stored plants did not reset back to sowing.";
            return HarnessTestStatus.Failed;
        }

        details = "Growing state with zero stored plants reset back to sowing.";
        return HarnessTestStatus.Passed;
    }
}

internal sealed class HdhNoPowerDamageTest : IHarnessTestCase
{
    private Thing building;
    private float baselineHealth;
    private bool originalSetting;

    public string Name => "hdh.no-power-damage";

    public void Start(HarnessTestContext context)
    {
        HdhReflection.ClearAll(context.Map);
        building = HdhReflection.SpawnBuilding("HDH_Hydroponics_Quantum", context.Map);
        var plantDef = ThingDef.Named("Plant_Rice");
        HdhReflection.SeedState(
            building,
            plantDef,
            "Growing",
            powered: false,
            storedPlants: 4,
            growth: 0.30f,
            age: 1000,
            health: 5f,
            storedPlantsBuffer: 0,
            averageHarvestGrowth: 0f);
        originalSetting = HdhReflection.KillPlantsOnNoPowerSetting();
        HdhReflection.SetKillPlantsOnNoPowerSetting(true);
        baselineHealth = HdhReflection.PlantHealth(building);
    }

    public HarnessTestStatus Tick(HarnessTestContext context, out string details, out string snapshotPath)
    {
        HdhReflection.TickLong(building);
        var currentHealth = HdhReflection.PlantHealth(building);
        snapshotPath = context.WriteSnapshot("hdh-no-power-damage", HdhReflection.SnapshotValues(building));
        HdhReflection.SetKillPlantsOnNoPowerSetting(originalSetting);

        if (!(currentHealth < baselineHealth) || !string.Equals(HdhReflection.BayStageName(building), "Growing", StringComparison.Ordinal))
        {
            details = "Expected powered-off growing plants to take damage without immediately leaving growing stage.";
            return HarnessTestStatus.Failed;
        }

        details = "Powered-off growing plants took damage: " + baselineHealth.ToString("F1") + " -> " + currentHealth.ToString("F1");
        return HarnessTestStatus.Passed;
    }
}

internal sealed class HdhPowerGrowthGateTest : IHarnessTestCase
{
    private Thing building;
    private float baselineGrowth;
    private bool powerOffVerified;

    public string Name => "hdh.power-growth-gate";

    public void Start(HarnessTestContext context)
    {
        HdhReflection.ClearAll(context.Map);
        building = HdhReflection.SpawnBuilding("HDH_Hydroponics_Quantum", context.Map);
        HdhReflection.SeedGrowingState(building, ThingDef.Named("Plant_Rice"), false, 4, 0.20f, 1000);
        baselineGrowth = HdhReflection.PlantGrowth(building);
    }

    public HarnessTestStatus Tick(HarnessTestContext context, out string details, out string snapshotPath)
    {
        snapshotPath = null;

        if (!powerOffVerified)
        {
            HdhReflection.TickLong(building);
            var afterNoPower = HdhReflection.PlantGrowth(building);
            if (Math.Abs(afterNoPower - baselineGrowth) > 0.0001f)
            {
                details = "Growth changed while power was off.";
                snapshotPath = context.WriteSnapshot("hdh-power-growth-fail-off", HdhReflection.SnapshotValues(building));
                return HarnessTestStatus.Failed;
            }

            building.TryGetComp<CompPowerTrader>().PowerOn = true;
            powerOffVerified = true;
            details = "Verified no growth while unpowered; waiting for valid growth window.";
            return HarnessTestStatus.Running;
        }

        var dayPct = GenLocalDate.DayPercent(building);
        if (dayPct < 0.25f || dayPct > 0.8f)
        {
            details = "Waiting for daytime window; current day pct " + dayPct.ToString("F2");
            return HarnessTestStatus.Running;
        }

        HdhReflection.TickLong(building);
        var afterPower = HdhReflection.PlantGrowth(building);
        snapshotPath = context.WriteSnapshot("hdh-power-growth-pass", HdhReflection.SnapshotValues(building));
        if (afterPower <= baselineGrowth)
        {
            details = "Growth did not advance after power was restored.";
            return HarnessTestStatus.Failed;
        }

        details = "Growth advanced from " + baselineGrowth.ToString("F3") + " to " + afterPower.ToString("F3");
        return HarnessTestStatus.Passed;
    }
}

internal sealed class HdhSaveLoadPersistenceTest : IHarnessTestCase
{
    private const string SaveName = "zrth_hdh_roundtrip";
    private const string CheckpointFile = "hdh-save-load-checkpoint.json";
    private bool postLoadPhase;

    public string Name => "hdh.save-load-roundtrip";

    public void Start(HarnessTestContext context)
    {
        var checkpointPath = Path.Combine(context.Manifest.StateDirectory, CheckpointFile);
        postLoadPhase = File.Exists(checkpointPath);
        if (postLoadPhase)
        {
            return;
        }

        HdhReflection.ClearAll(context.Map);
        var building = HdhReflection.SpawnBuilding("HDH_Hydroponics_Quantum", context.Map);
        HdhReflection.SeedGrowingState(building, ThingDef.Named("Plant_Rice"), true, 3, 0.42f, 2500);
        HdhReflection.SeedState(
            building,
            ThingDef.Named("Plant_Rice"),
            "Growing",
            powered: true,
            storedPlants: 3,
            growth: 0.42f,
            age: 2500,
            health: 55f,
            storedPlantsBuffer: 0,
            averageHarvestGrowth: 0f);
        HdhReflection.AdjustCapacity(building, 2);
        JsonFile.Save(checkpointPath, new StateSnapshot
        {
            Name = "hdh-save-load-before",
            CapturedAtUtc = DateTime.UtcNow,
            Values = HdhReflection.SnapshotValues(building),
        });
        GameDataSaveLoader.SaveGame(SaveName);
        GameDataSaveLoader.LoadGame(SaveName);
    }

    public HarnessTestStatus Tick(HarnessTestContext context, out string details, out string snapshotPath)
    {
        var checkpointPath = Path.Combine(context.Manifest.StateDirectory, CheckpointFile);
        snapshotPath = null;

        if (!postLoadPhase)
        {
            details = "Waiting for save/load cycle.";
            return HarnessTestStatus.Running;
        }

        var building = HdhReflection.FindFirstSpawnedBuilding(context.Map);
        if (building == null)
        {
            details = "Could not find a spawned HDH building after load.";
            return HarnessTestStatus.Failed;
        }

        var growth = HdhReflection.PlantGrowth(building);
        var storedPlants = HdhReflection.StoredPlantCount(building);
        var health = HdhReflection.PlantHealth(building);
        var scalingLevel = HdhReflection.CurrentPowerScalingLevel(building);
        var capacity = HdhReflection.MaxPlantCapacity(building);
        snapshotPath = context.WriteSnapshot("hdh-save-load-after", HdhReflection.SnapshotValues(building));
        File.Delete(checkpointPath);

        if (Math.Abs(growth - 0.42f) > 0.05f || storedPlants != 3 || Math.Abs(health - 55f) > 0.05f || scalingLevel != 2 || capacity != 12)
        {
            details = "State did not survive save/load as expected.";
            return HarnessTestStatus.Failed;
        }

        details = "Growth " + growth.ToString("F3") + ", stored plants " + storedPlants + ", health " + health.ToString("F1") + ", scaling " + scalingLevel + " survived save/load.";
        return HarnessTestStatus.Passed;
    }
}

internal sealed class HdhDbhCompatibilityTest : IHarnessTestCase
{
    public string Name => "hdh.dbh-compatibility";

    public void Start(HarnessTestContext context)
    {
    }

    public HarnessTestStatus Tick(HarnessTestContext context, out string details, out string snapshotPath)
    {
        snapshotPath = null;
        var dbhActive = LoadedModManager.RunningModsListForReading.Any(mod =>
            mod.PackageId.IndexOf("dubwise.dubsbadhygiene", StringComparison.OrdinalIgnoreCase) >= 0);

        var def = DefDatabase<ThingDef>.GetNamedSilentFail("HDH_Hydroponics");
        if (def == null)
        {
            details = "HDH_Hydroponics def not found.";
            return HarnessTestStatus.Failed;
        }

        if (!dbhActive)
        {
            details = "DBH is not active; compatibility path skipped.";
            return HarnessTestStatus.Passed;
        }

        var hasInjectedComp = def.comps != null && def.comps.Any(comp =>
            comp.compClass != null &&
            (comp.compClass.Name.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0 ||
             comp.compClass.Name.IndexOf("Refuel", StringComparison.OrdinalIgnoreCase) >= 0));

        details = hasInjectedComp
            ? "DBH active and HDH defs include injected plumbing/refuel behavior."
            : "DBH active but expected injected comps were not found.";
        return hasInjectedComp ? HarnessTestStatus.Passed : HarnessTestStatus.Failed;
    }
}

internal sealed class HdhDeSpawnResetTest : IHarnessTestCase
{
    private Thing building;

    public string Name => "hdh.despawn-reset";

    public void Start(HarnessTestContext context)
    {
        HdhReflection.ClearAll(context.Map);
        building = HdhReflection.SpawnBuilding("HDH_Hydroponics_Quantum", context.Map);
        var plantDef = ThingDef.Named("Plant_Rice");
        HdhReflection.SeedState(
            building,
            plantDef,
            "Growing",
            powered: true,
            storedPlants: 3,
            growth: 0.75f,
            age: 3200,
            health: 25f,
            storedPlantsBuffer: 1,
            averageHarvestGrowth: 0.2f);
        HdhReflection.SpawnPlantOnBuilding(building, plantDef, 0.4f, 1000);
        HdhReflection.DeSpawn(building);
    }

    public HarnessTestStatus Tick(HarnessTestContext context, out string details, out string snapshotPath)
    {
        snapshotPath = context.WriteSnapshot("hdh-despawn-reset", HdhReflection.SnapshotValues(building));

        if (HdhReflection.StoredPlantCount(building) != 0 ||
            Math.Abs(HdhReflection.PlantGrowth(building)) > 0.0001f ||
            !string.Equals(HdhReflection.BayStageName(building), "Sowing", StringComparison.Ordinal))
        {
            details = "Vanish de-spawn should clear stored state and reset the hydro back to sowing.";
            return HarnessTestStatus.Failed;
        }

        details = "Vanish de-spawn cleared internal state before the building left the map.";
        return HarnessTestStatus.Passed;
    }
}
