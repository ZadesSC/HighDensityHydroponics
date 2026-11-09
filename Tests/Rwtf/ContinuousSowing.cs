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
public sealed class HdhSowingFixture : FixtureProvider
{
    public override FixtureContract Contract => new FixtureContract
    {
        Id = "hdh-sowing",
        Intent = "vanilla sowing completion and capacity accounting",
        HorizonTicks = 20000,
        Capabilities = new[] { "generated-map", "named-subjects", "save-reload" },
        Interventions = new[] { "storyteller-disabled", "random-diseases-disabled",
            "random-mental-states-disabled", "fixed-weather-temperature", "ambient-wildlife-disabled",
            "setup-pawn-world-retirement", "forced-bay-power", "assigned-jobs", "accelerated-plant-work" },
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

        // A clear, heavy-supporting platform, with ordinary human needs left enabled.
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
            pawn.skills.GetSkill(SkillDefOf.Plants).Level = 15;
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

public sealed class ContinuousSowing : Rwtf.Scenario
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    int reloadRecords;
    public override string Id => "hdh.sowing";
    public override string Intent => "same-cell reuse, vanilla rewards, interruption, final-slot competition and active-job reload";
    public override string EvidenceLevel => "assigned-job";
    public override string[] RequiredPackages => new[] { Environment.GetEnvironmentVariable("RWTF_PARAMETER_package") ?? "mapleapple.highdensityhydroponics.fixed.zouhb.zades" };
    public override string[] RequiredDefs => new[] { "HDH_Hydroponics_mini", "Plant_Rice", "HydroponicsBasin" };
    public override FixtureProvider Fixture => new HdhSowingFixture();

    static void Set(Building_HighDensityHydro bay, string name, object value) =>
        typeof(Building_HighDensityHydro).GetField(name, Private).SetValue(bay, value);
    static void Reset(Building_HighDensityHydro bay) =>
        typeof(Building_HighDensityHydro).GetMethod("ResetPlantStateForSowing", Private).Invoke(bay, new object[] { true });
    static int Records(Pawn pawn) => (int)pawn.records.GetValue(RecordDefOf.PlantsSown);
    static bool Store(Building_HighDensityHydro bay, Plant plant) =>
        (bool)typeof(Building_HighDensityHydro).GetMethod("TryStoreSownPlant", Private).Invoke(bay, new object[] { plant });
    static int CompletionToils(Pawn pawn)
    {
        var toils = (List<Toil>)typeof(JobDriver).GetField("toils", Private).GetValue(pawn.jobs.curDriver);
        return toils.Count(t => t.debugName == "HDH_StoreCompletedSowing");
    }
    static void Power(Building_PlantGrower bay) => bay.GetComp<CompPowerTrader>().PowerOn = true;

    static void Assign(Context c, Pawn pawn, IntVec3 cell, bool stale = false)
    {
        // Normal grower discovery refreshes this vanilla cache for each grower.
        typeof(WorkGiver_Grower).GetField("wantedPlantDef", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).SetValue(null, null);
        var giver = new WorkGiver_GrowerSow { def = c.Def<WorkGiverDef>("GrowerSow") };
        Job job = stale ? JobMaker.MakeJob(JobDefOf.Sow, cell) : giver.JobOnCell(pawn, cell);
        if (job == null)
        {
            c.Event("sow-refused", new { tick = c.Tick, growthSeason = PlantUtility.GrowthSeasonNow(cell, c.Map, c.Def<ThingDef>("Plant_Rice")),
                canPlant = c.Def<ThingDef>("Plant_Rice").CanNowPlantAt(cell, c.Map),
                temperature = GenTemperature.GetTemperatureForCell(cell, c.Map),
                reserve = pawn.CanReserve(cell), crop = cell.GetPlantToGrowSettable(c.Map)?.GetPlantDefToGrow()?.defName });
            throw new Unsupported("Vanilla rejected sowing cell " + cell + ": ");
        }
        if (stale) job.plantDefToSow = c.Def<ThingDef>("Plant_Rice");
        job.playerForced = true;
        pawn.jobs.StartJob(job, JobCondition.InterruptForced);
        if (pawn.CurJob == job)
            pawn.jobs.curDriver.AddFinishAction(condition => c.Event("assigned-sow-ended", new { condition = condition.ToString(), tick = c.Tick }));
    }

    public override void Setup(Context c)
    {
        var bay = (Building_HighDensityHydro)c.Spawn("bay", "HDH_Hydroponics_mini", c.Map.Center, faction: Faction.OfPlayer);
        Power(bay);
        ((IPlantToGrowSettable)bay).SetPlantDefToGrow(c.Def<ThingDef>("Plant_Rice"));
        Pawn first = c.Subject<Pawn>("grower0"), second = c.Subject<Pawn>("grower1");
        IntVec3 cell = bay.OccupiedRect().Cells.First();
        c.Wait(() => PlantUtility.GrowthSeasonNow(cell, c.Map, c.Def<ThingDef>("Plant_Rice")), 250, 10,
            "sowing platform temperature/regions did not settle");

        // Force a fast-worker stress case without changing production sowing labor.
        // This race-stat intervention is scoped and restored even on a failed assertion.
        List<StatModifier> original = first.def.statBases;
        first.def.statBases = new List<StatModifier>(original ?? new List<StatModifier>());
        first.def.statBases.RemoveAll(s => s.stat == StatDefOf.PlantWorkSpeed);
        first.def.statBases.Add(new StatModifier { stat = StatDefOf.PlantWorkSpeed, value = 100f });
        try
        {
            int records = Records(first);
            float xp = first.skills.GetSkill(SkillDefOf.Plants).xpSinceLastLevel;
            int start = c.Tick;
            for (int i = 0; i < 8; i++)
            {
                int stored = bay.StoredPlantCount;
                Assign(c, first, cell);
                c.Check("one HDH completion toil", CompletionToils(first) == 1);
                c.Wait(() => {
                    if ((c.Tick - start) % 50 == 0)
                        c.Event("sow-progress", new { tick = c.Tick, job = first.CurJobDef?.defName,
                            position = first.Position.ToString(), target = first.CurJob?.targetA.ToString(),
                            speed = first.GetStatValue(StatDefOf.PlantWorkSpeed), power = bay.GetComp<CompPowerTrader>().PowerOn,
                            plants = bay.PlantsOnMe.Select(p => new { id = p.thingIDNumber, stage = p.LifeStage.ToString() }).ToArray() });
                    return Records(first) > records + i;
                }, 500, 15, "fast sow did not complete");
                c.Check("completed plant stored in same tick", bay.StoredPlantCount == stored + 1,
                    new { iteration = i, tick = c.Tick, stored = bay.StoredPlantCount });
                c.Check("surface tile immediately reusable", cell.GetPlant(c.Map) == null);
                c.Wait(() => first.CurJobDef != JobDefOf.Sow, 20, 5, "sow cleanup did not finish");
            }
            c.Check("eight plants completed before a rare interval", c.Tick - start < 250,
                new { elapsedTicks = c.Tick - start, completed = Records(first) - records });
            c.Check("vanilla Plants experience retained", first.skills.GetSkill(SkillDefOf.Plants).xpSinceLastLevel > xp);

            Reset(bay);
            Assign(c, first, cell);
            c.Wait(() => cell.GetPlant(c.Map) != null, 100, 10, "incomplete plant did not spawn");
            Plant incomplete = cell.GetPlant(c.Map);
            c.Check("incomplete plant cannot be stored", !Store(bay, incomplete) && bay.StoredPlantCount == 0);
            first.jobs.EndCurrentJob(JobCondition.InterruptForced);
            c.Check("interruption destroys incomplete plant without credit", incomplete.Destroyed && bay.StoredPlantCount == 0);

            // Two pre-created jobs compete for one remaining slot.
            Set(bay, "_numStoredPlants", bay.MaxPlantCapacity - 1);
            int before = Records(first) + Records(second);
            Assign(c, first, cell);
            Assign(c, second, bay.OccupiedRect().Cells.Last(), stale: true);
            c.Wait(() => bay.StoredPlantCount == bay.MaxPlantCapacity, 500, 15, "last slot never filled");
            c.Check("last slot credited exactly once", Records(first) + Records(second) == before + 1);
            c.Check("capacity transition immediate", typeof(Building_HighDensityHydro).GetField("_bayStage", Private).GetValue(bay).ToString() == "Growing");
            c.Check("no excess surface plants", !bay.PlantsOnMe.Any());
            first.jobs.StopAll();
            second.jobs.StopAll();

            Reset(bay);
            Plant recovered = (Plant)GenSpawn.Spawn(c.Def<ThingDef>("Plant_Rice"), cell, c.Map);
            recovered.Growth = .0001f;
            recovered.sown = true;
            typeof(Building_HighDensityHydro).GetMethod("HandleSowing", Private).Invoke(bay, null);
            c.Check("rare recovery stores completed unreserved plant", bay.StoredPlantCount == 1 && !recovered.Spawned);
            c.Check("duplicate transfer refused", !Store(bay, recovered) && bay.StoredPlantCount == 1);

            Reset(bay);
            Assign(c, first, cell);
            c.Wait(() => cell.GetPlant(c.Map) != null, 100, 10, "crop-change plant did not spawn");
            Plant oldCrop = cell.GetPlant(c.Map);
            ((IPlantToGrowSettable)bay).SetPlantDefToGrow(c.Def<ThingDef>("Plant_Potato"));
            c.Check("crop change discards old incomplete crop safely", oldCrop.Destroyed && bay.StoredPlantCount == 0);
            first.jobs.StopAll();
            ((IPlantToGrowSettable)bay).SetPlantDefToGrow(c.Def<ThingDef>("Plant_Rice"));

            var vanilla = (Building_PlantGrower)c.Spawn("vanilla", "HydroponicsBasin", c.Map.Center + new IntVec3(8, 0, 0), faction: Faction.OfPlayer);
            Power(vanilla);
            vanilla.SetPlantDefToGrow(c.Def<ThingDef>("Plant_Rice"));
            int ordinaryBefore = Records(first);
            IntVec3 ordinaryCell = vanilla.OccupiedRect().Cells.First();
            Assign(c, first, ordinaryCell);
            c.Check("ordinary sowing receives no HDH toil", CompletionToils(first) == 0);
            c.Wait(() => Records(first) > ordinaryBefore, 500, 15, "ordinary sowing did not complete");
            c.Check("ordinary completed plant remains physical", ordinaryCell.GetPlant(c.Map)?.LifeStage == PlantLifeStage.Growing);
            c.Check("ordinary sowing does not credit HDH", bay.StoredPlantCount == 0);
            first.jobs.StopAll();
        }
        finally
        {
            first.def.statBases = original;
        }
        c.Event("sowing-checks-complete", new { tick = c.Tick, scope = "actual vanilla jobs with accelerated work stat; no power-network or performance claim" });
    }

    public override void Assert(Context c)
    {
        var bay = c.Subject<Building_HighDensityHydro>("bay");
        Pawn pawn = c.Subject<Pawn>("grower0");
        Power(bay);
        Reset(bay);
        reloadRecords = Records(pawn);
        Assign(c, pawn, bay.OccupiedRect().Cells.First());
        c.Wait(() => bay.PlantsOnMe.Any(p => p.LifeStage == PlantLifeStage.Sowing), 100, 10, "active reload sow did not start");
        c.Check("checkpoint contains unfinished vanilla sow job", pawn.CurJobDef == JobDefOf.Sow && bay.StoredPlantCount == 0);
    }

    public override void AfterReload(Context c)
    {
        var bay = c.Subject<Building_HighDensityHydro>("bay");
        Pawn pawn = c.Subject<Pawn>("grower0");
        Power(bay);
        c.Event("restored-sow-state", new { tick = c.Tick, job = pawn.CurJobDef?.defName,
            stored = bay.StoredPlantCount, recordCount = Records(pawn), baseline = reloadRecords,
            toils = pawn.jobs.curDriver == null ? -1 : CompletionToils(pawn),
            plants = bay.PlantsOnMe.Select(p => new { id = p.thingIDNumber, stage = p.LifeStage.ToString() }).ToArray() });
        c.Check("completion toil restored on active job", pawn.CurJobDef == JobDefOf.Sow && CompletionToils(pawn) == 1);
        c.Wait(() => Records(pawn) > reloadRecords, 1000, 20, "restored sow job did not complete");
        c.Check("restored job stored exactly once", bay.StoredPlantCount == 1 && !bay.PlantsOnMe.Any());
    }
}
