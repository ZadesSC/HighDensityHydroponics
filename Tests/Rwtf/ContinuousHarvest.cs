using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HighDensityHydro;
using RimWorld;
using RimWorld.Planet;
using Rwtf;
using Verse;
using Verse.AI;
using Verse.AI.Group;

// Trusted external adapter for RimWorldTestFramework; never shipped as a gameplay mod.
public sealed class HdhHarvestFixture : FixtureProvider
{
    public override FixtureContract Contract => new FixtureContract
    {
        Id = "hdh-harvest",
        Intent = "vanilla harvest continuation and stored-plant accounting",
        HorizonTicks = 20000,
        Capabilities = new[] { "generated-map", "named-subjects", "save-reload" },
        Interventions = new[] { "storyteller-disabled", "random-diseases-disabled",
            "random-mental-states-disabled", "fixed-weather-temperature", "ambient-wildlife-disabled",
            "setup-pawn-world-retirement", "forced-bay-power", "forced-harvest-stage", "assigned-jobs",
            "accelerated-plant-work" },
        Invariants = new Dictionary<string, double> { { "population", 2 }, { "mapSize", 100 }, { "outdoorC", 21 } }
    };

    public override void Setup(Context c)
    {
        new MapRecipe { Seed = 43210, Size = 100, Biome = "Desert", Weather = "Clear", Temperature = 21 }.Generate(c);
        c.ConfigureEnvironment(new EnvironmentPolicy { Weather = "Clear", OutdoorC = 21, Storyteller = false,
            RandomDiseases = false, RandomMentalStates = false, SuppressWildlife = true });
        foreach (Pawn pawn in c.Map.mapPawns.AllPawnsSpawned.ToList())
        {
            pawn.GetLord()?.Notify_PawnLost(pawn, PawnLostCondition.ExitedMap);
            pawn.ownership?.UnclaimAll();
            pawn.jobs?.StopAll();
            pawn.DeSpawn();
            Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.KeepForever);
        }

        foreach (IntVec3 cell in CellRect.CenteredOn(c.Map.Center, 15))
        {
            foreach (Thing thing in cell.GetThingList(c.Map).ToList())
                if (thing is Plant || thing.def.category == ThingCategory.Building)
                {
                    if (thing.def.destroyable) thing.Destroy();
                    else thing.DeSpawn();
                }
            c.Map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
        }
        for (int i = 0; i < 2; i++)
        {
            Pawn pawn = c.SpawnPawn("grower" + i, "Colonist", c.Map.Center + new IntVec3(-3, 0, i), Faction.OfPlayer);
            pawn.skills.GetSkill(SkillDefOf.Plants).Level = 20;
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                pawn.workSettings.SetPriority(work, 0);
        }
    }

    public override void Validate(Context c, string stage)
    {
        bool passed = c.Map.Size.x == 100 && c.Map.Size.z == 100 && c.Actors.Count == 2 &&
            c.Actors.All(p => !p.Dead && !p.Downed) && Math.Abs(c.Map.mapTemperature.OutdoorTemp - 21) < .01;
        c.Check("fixture invariants " + stage, passed);
        c.Event("fixture-validation", new { stage, supported = true, passed, invariants = Contract.Invariants });
    }
}

public sealed class ContinuousHarvest : Rwtf.Scenario
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    int reloadTotal;
    public override string Id => "hdh.harvest";
    public override string Intent => "same-tile refill, job continuation within vanilla batch size, regrowth buffering, final wave, ordinary hydroponics and active-job reload";
    public override string EvidenceLevel => "assigned-job";
    public override string[] RequiredPackages => new[] { Environment.GetEnvironmentVariable("RWTF_PARAMETER_package") ?? "mapleapple.highdensityhydroponics.fixed.zouhb.zades" };
    public override string[] RequiredDefs => new[] { "HDH_Hydroponics_mini", "Plant_Rice", "Plant_Berry", "HydroponicsBasin" };
    public override FixtureProvider Fixture => new HdhHarvestFixture();

    static object Get(Building_HighDensityHydro bay, string name) =>
        typeof(Building_HighDensityHydro).GetField(name, Private).GetValue(bay);
    static void Call(Building_HighDensityHydro bay, string name, params object[] args) =>
        typeof(Building_HighDensityHydro).GetMethod(name, Private).Invoke(bay, args);
    static string Stage(Building_HighDensityHydro bay) => Get(bay, "_bayStage").ToString();
    static int Surface(Building_HighDensityHydro bay) => bay.PlantsOnMe.Count();
    static int Remaining(Building_HighDensityHydro bay) => bay.StoredPlantCount + Surface(bay);
    static void Power(Building_PlantGrower bay) => bay.GetComp<CompPowerTrader>().PowerOn = true;
    static int Items(Context c, ThingDef def) => c.Map.listerThings.ThingsOfDef(def).Sum(t => t.stackCount);

    // Fill storage with mature plants and spawn the first wave exactly as the rare tick does.
    static void Ready(Building_HighDensityHydro bay, ThingDef crop)
    {
        ((IPlantToGrowSettable)bay).SetPlantDefToGrow(crop);
        Call(bay, "ResetPlantStateForSowing", true);
        Call(bay, "ForceHarvestReadyForDev");
        Call(bay, "HandleHarvest");
    }

    // Jobs are pooled by vanilla, so completion is tracked through the driver's finish action.
    static bool ended;

    static Job Assign(Context c, Pawn pawn, IntVec3 cell)
    {
        typeof(WorkGiver_Grower).GetField("wantedPlantDef", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).SetValue(null, null);
        var giver = new WorkGiver_GrowerHarvest { def = c.Def<WorkGiverDef>("GrowerHarvest") };
        if (!giver.HasJobOnCell(pawn, cell))
            throw new Unsupported("Vanilla rejected harvest cell " + cell + ": plant=" + cell.GetPlant(c.Map));
        Job job = giver.JobOnCell(pawn, cell);
        job.playerForced = true;
        ended = false;
        pawn.jobs.StartJob(job, JobCondition.InterruptForced);
        if (pawn.CurJob == job)
            pawn.jobs.curDriver.AddFinishAction(condition =>
            {
                ended = true;
                c.Event("assigned-harvest-ended", new { condition = condition.ToString(), tick = c.Tick });
            });
        else
            ended = true;
        c.Event("assigned-harvest", new { tick = c.Tick, started = pawn.CurJob == job, queue = job.targetQueueA?.Count ?? 0,
            targetA = job.targetA.ToString(), pawnAt = pawn.Position.ToString(), reach = pawn.CanReach(cell, PathEndMode.Touch, Danger.Deadly) });
        return job;
    }

    // Runs one job to completion while asserting that every collected tile is refilled in the same tick.
    int RunJob(Context c, Building_HighDensityHydro bay, Pawn pawn, IntVec3 cell, string label)
    {
        int cells = bay.def.size.x * bay.def.size.z;
        int before = Remaining(bay), gaps = 0;
        Assign(c, pawn, cell);
        int start = c.Tick;
        c.Wait(() =>
        {
            if (Stage(bay) == "Harvest" && bay.StoredPlantCount > 0 && Surface(bay) < cells) gaps++;
            if ((c.Tick - start) % 25 == 0)
                c.Event("harvest-progress", new { label, tick = c.Tick, stored = bay.StoredPlantCount, surface = Surface(bay),
                    job = pawn.CurJobDef?.defName, queue = pawn.CurJob?.targetQueueA?.Count ?? -1,
                    speed = pawn.GetStatValue(StatDefOf.PlantWorkSpeed), pos = pawn.Position.ToString() });
            return ended;
        }, 2000, 60, label + " job did not finish");
        // Leaving Harvest means every remaining plant was collected (regrowing crops refill storage).
        int collected = before - (Stage(bay) == "Harvest" ? Remaining(bay) : 0);
        c.Check(label + ": tiles refilled in the collection tick", gaps == 0, new { gaps, tick = c.Tick });
        c.Event("harvest-job", new { label, collected, stored = bay.StoredPlantCount, surface = Surface(bay), stage = Stage(bay), tick = c.Tick });
        return collected;
    }

    public override void Setup(Context c)
    {
        var bay = (Building_HighDensityHydro)c.Spawn("bay", "HDH_Hydroponics_mini", c.Map.Center, faction: Faction.OfPlayer);
        Power(bay);
        ThingDef rice = c.Def<ThingDef>("Plant_Rice"), berry = c.Def<ThingDef>("Plant_Berry");
        Pawn first = c.Subject<Pawn>("grower0");
        IntVec3 cell = bay.OccupiedRect().Cells.First();

        List<StatModifier> original = first.def.statBases;
        first.def.statBases = new List<StatModifier>(original ?? new List<StatModifier>());
        first.def.statBases.RemoveAll(s => s.stat == StatDefOf.PlantWorkSpeed);
        first.def.statBases.Add(new StatModifier { stat = StatDefOf.PlantWorkSpeed, value = 100f });
        try
        {
            // Rice: 16 stored, 200 work each, so the first job takes twelve plants (vanilla's 2400 batch).
            Ready(bay, rice);
            int capacity = bay.MaxPlantCapacity;
            c.Check("rare tick spawns first wave", Surface(bay) == 4 && bay.StoredPlantCount == capacity - 4);
            int riceItems = Items(c, c.Def<ThingDef>("RawRice"));
            int firstJob = RunJob(c, bay, first, cell, "rice batch");
            c.Check("one job continues on refilled tiles up to the vanilla batch", firstJob == 12, new { firstJob });
            c.Check("rice yield placed", Items(c, c.Def<ThingDef>("RawRice")) > riceItems);
            int secondJob = RunJob(c, bay, first, bay.PlantsOnMe.First().Position, "rice remainder");
            c.Check("all stored rice collected exactly once", firstJob + secondJob == capacity, new { firstJob, secondJob });
            c.Check("single-harvest crop resets to sowing immediately", Stage(bay) == "Sowing" &&
                bay.StoredPlantCount == 0 && Surface(bay) == 0);

            // Regrowing crop: cut-back plants are buffered at once and the bay regrows them.
            Ready(bay, berry);
            int berries = 0, berryJobs = 0;
            for (int i = 0; i < 4 && Stage(bay) == "Harvest"; i++, berryJobs++)
                berries += RunJob(c, bay, first, bay.PlantsOnMe.First().Position, "berry " + i);
            c.Check("regrowing crop collected once per stored plant", berries == capacity, new { berries });
            c.Check("later jobs get their own batch despite vanilla job pooling", berryJobs == 2, new { berryJobs });
            c.Check("regrowing crop returns to growing with every plant", Stage(bay) == "Growing" &&
                bay.StoredPlantCount == capacity && (int)Get(bay, "_numStoredPlantsBuffer") == 0 && Surface(bay) == 0,
                new { stage = Stage(bay), stored = bay.StoredPlantCount });
            c.Check("regrowth starts from cut-back growth", Math.Abs((float)Get(bay, "_curGrowth") - berry.plant.harvestAfterGrowth) < .01f);

            // Rare-tick final wave: spawning the last stored plants must not end the harvest early.
            Ready(bay, rice);
            foreach (Plant plant in bay.PlantsOnMe.ToList()) plant.Destroy();
            typeof(Building_HighDensityHydro).GetField("_numStoredPlants", Private).SetValue(bay, 2);
            Call(bay, "HandleHarvest");
            c.Check("final rare-tick wave stays in harvest", Stage(bay) == "Harvest" && Surface(bay) == 2 && bay.StoredPlantCount == 0);
            RunJob(c, bay, first, bay.PlantsOnMe.First().Position, "final wave");
            c.Check("final wave completes harvest", Stage(bay) == "Sowing" && Surface(bay) == 0);

            // Ordinary hydroponics keeps vanilla harvesting.
            var vanilla = (Building_PlantGrower)c.Spawn("vanilla", "HydroponicsBasin", c.Map.Center + new IntVec3(8, 0, 0), faction: Faction.OfPlayer);
            Power(vanilla);
            vanilla.SetPlantDefToGrow(rice);
            IntVec3 ordinaryCell = vanilla.OccupiedRect().Cells.First();
            var ordinary = (Plant)GenSpawn.Spawn(rice, ordinaryCell, c.Map);
            ordinary.Growth = 1f;
            ordinary.sown = true;
            Assign(c, first, ordinaryCell);
            c.Wait(() => ended, 2000, 60, "ordinary harvest did not finish");
            c.Check("ordinary harvest leaves its tile empty", ordinary.Destroyed && ordinaryCell.GetPlant(c.Map) == null);
            c.Check("ordinary harvest does not touch HDH", bay.StoredPlantCount == 0 && Stage(bay) == "Sowing");
        }
        finally
        {
            first.def.statBases = original;
        }
        c.Event("harvest-checks-complete", new { tick = c.Tick, scope = "actual vanilla harvest jobs with accelerated work stat; no performance claim" });
    }

    public override void Assert(Context c)
    {
        var bay = c.Subject<Building_HighDensityHydro>("bay");
        Pawn pawn = c.Subject<Pawn>("grower0");
        Power(bay);
        Ready(bay, c.Def<ThingDef>("Plant_Rice"));
        reloadTotal = Remaining(bay);
        IntVec3 cell = bay.OccupiedRect().Cells.First();
        Assign(c, pawn, cell);
        Job job = pawn.CurJob;
        // Normal work speed: wait for one collection so the queue holds an HDH-added replacement.
        int start = c.Tick;
        c.Wait(() =>
        {
            if ((c.Tick - start) % 100 == 0)
                c.Event("reload-progress", new { tick = c.Tick, job = pawn.CurJobDef?.defName, toil = pawn.jobs.curDriver?.CurToilString,
                    pos = pawn.Position.ToString(), speed = pawn.GetStatValue(StatDefOf.PlantWorkSpeed), stored = bay.StoredPlantCount,
                    surface = Surface(bay), downed = pawn.Downed, food = pawn.needs?.food?.CurLevelPercentage, rest = pawn.needs?.rest?.CurLevelPercentage });
            return Remaining(bay) < reloadTotal;
        }, 4000, 90, "reload harvest did not collect");
        c.Check("checkpoint contains unfinished continued harvest job", pawn.CurJob == job && Surface(bay) == 4 &&
            Remaining(bay) == reloadTotal - 1);
        reloadTotal = Remaining(bay);
    }

    public override void AfterReload(Context c)
    {
        var bay = c.Subject<Building_HighDensityHydro>("bay");
        Pawn pawn = c.Subject<Pawn>("grower0");
        Power(bay);
        c.Event("restored-harvest-state", new { tick = c.Tick, job = pawn.CurJobDef?.defName, stored = bay.StoredPlantCount,
            surface = Surface(bay), stage = Stage(bay), queue = pawn.CurJob?.targetQueueA?.Count ?? -1 });
        c.Check("restored state accounts for every plant", pawn.CurJobDef == JobDefOf.Harvest && Remaining(bay) == reloadTotal);
        c.Wait(() => Remaining(bay) < reloadTotal, 3000, 60, "restored harvest job did not continue");
        c.Check("restored job keeps refilling", Surface(bay) == 4 || bay.StoredPlantCount == 0);
    }
}
