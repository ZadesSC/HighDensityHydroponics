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
public sealed class PowerReadoutFixture : FixtureProvider
{
    public override FixtureContract Contract => new FixtureContract
    {
        Id = "hdh-power-readout",
        Intent = "real battery network and completed power-switch action",
        HorizonTicks = 20000,
        Capabilities = new[] { "generated-map", "named-subjects", "save-reload" },
        Interventions = new[] { "storyteller-disabled", "random-diseases-disabled",
            "random-mental-states-disabled", "fixed-weather-temperature", "ambient-wildlife-disabled",
            "setup-pawn-world-retirement", "charged-battery", "direct-completed-flick-action" },
        Invariants = new Dictionary<string, double> { { "population", 0 }, { "mapSize", 100 }, { "outdoorC", 21 } }
    };

    public override void Setup(Context c)
    {
        new MapRecipe { Seed = 43210, Size = 100, Biome = "Desert", Weather = "Clear", Temperature = 21 }.Generate(c);
        c.ConfigureEnvironment(new EnvironmentPolicy { Weather = "Clear", OutdoorC = 21, Storyteller = false,
            RandomDiseases = false, RandomMentalStates = false, SuppressWildlife = true });
        foreach (Pawn pawn in Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).ToList())
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
    }

    public override void Validate(Context c, string stage)
    {
        bool passed = c.Map.Size.x == 100 && c.Map.Size.z == 100 && c.Actors.Count == 0 &&
            c.Actors.All(p => !p.Dead && !p.Downed) && Math.Abs(c.Map.mapTemperature.OutdoorTemp - 21) < .01;
        c.Check("fixture invariants " + stage, passed);
        c.Event("fixture-validation", new { stage, supported = true, passed, invariants = Contract.Invariants });
    }
}

public sealed class PowerReadout : Rwtf.Scenario
{
    public override string Id => "hdh.power-readout";
    public override string Intent => "distinguish pending switch requests, displayed demand and actual network/battery consumption";
    public override string[] RequiredPackages => new[] { Environment.GetEnvironmentVariable("RWTF_PARAMETER_package") ?? "mapleapple.highdensityhydroponics.fixed.zouhb.zades" };
    public override FixtureProvider Fixture => new PowerReadoutFixture();
    static void Press(ThingWithComps thing)
    {
        thing.GetComp<CompFlickable>().CompGetGizmosExtra().OfType<Command_Toggle>().Single().toggleAction();
    }
    static void Complete(ThingWithComps thing)
    {
        var flick = thing.GetComp<CompFlickable>();
        if (!flick.WantsFlick()) throw new Unsupported("No pending flick action");
        flick.DoFlick();
        FlickUtility.UpdateFlickDesignation(thing);
    }
    static void SetSwitch(ThingWithComps thing, bool on)
    {
        var flick = thing.GetComp<CompFlickable>();
        if (flick.SwitchIsOn == on) return;
        Press(thing); Complete(thing);
    }
    static float GridWatts(CompPowerTrader power) => power.PowerNet.CurrentEnergyGainRate() / CompPower.WattsToWattDaysPerTick;
    static void Step(Context c, int ticks) { for (int i = 0; i < ticks; i++) c.Step(); }
    void State(Context c, string stage)
    {
        var q = c.Subject<Building_HighDensityHydro>("quantum");
        var p = q.GetComp<CompPowerTrader>();
        var f = q.GetComp<CompFlickable>();
        string inspection = q.GetInspectString();
        string[] lines = inspection.Split('\n');
        string neededPrefix = "PowerNeeded".Translate() + ": ";
        c.Check("one vanilla power-needed line " + stage, lines.Count(line => line.StartsWith(neededPrefix, StringComparison.Ordinal)) == 1);
        c.Check("vanilla component readout retained " + stage, lines.Contains(p.CompInspectStringExtra().Split('\n')[0]));
        c.Check("no misleading current-use inspector line " + stage, !lines.Any(line => line.StartsWith("PowerConsumptionMode".Translate() + ": ", StringComparison.Ordinal)));
        c.Event("power-observation", new { stage, tick=c.Tick, switchOn=f.SwitchIsOn, wantsFlick=f.WantsFlick(), powerOn=p.PowerOn,
            nominalWatts=q.CalculateCurrentPowerCost(), componentOutput=p.PowerOutput,
            gridWatts=GridWatts(p), battery=c.Subject<Building>("battery").GetComp<CompPowerBattery>().StoredEnergy,
            inspection=inspection, density=q.CurrentPowerScalingLevel,
            helperLights=c.Map.listerThings.AllThings.Count(t => t.def.defName=="HDH_BuiltInSunlampCellLight") });
    }
    public override void Setup(Context c)
    {
        c.Check("ordinary power simulation enabled", !DebugSettings.unlimitedPower);
        string requirement = "HDH_ITabRequiredPower".Translate("1614");
        c.Check("details active-demand label is translated", requirement.Contains("1614") && !requirement.Contains("HDH_ITabRequiredPower"));
        for (int x = -6; x <= 6; x++)
        {
            var wire = ThingMaker.MakeThing(c.Def<ThingDef>("PowerConduit"));
            wire.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(wire, c.Map.Center + new IntVec3(x, 0, 3), c.Map);
        }
        var batteryBuilding=(Building)c.Spawn("battery", "Battery", c.Map.Center+new IntVec3(-6,0,3), faction:Faction.OfPlayer);
        var battery=batteryBuilding.GetComp<CompPowerBattery>();
        battery.SetStoredEnergyPct(1f);
        var q=(Building_HighDensityHydro)c.Spawn("quantum", "HDH_Hydroponics_Quantum", c.Map.Center, faction:Faction.OfPlayer);
        var lamp=(Building)c.Spawn("control", "StandingLamp", c.Map.Center+new IntVec3(6,0,0), faction:Faction.OfPlayer);
        var disconnected=(Building_HighDensityHydro)c.Spawn("disconnected", "HDH_Hydroponics_Quantum", c.Map.Center+new IntVec3(0,0,-11), faction:Faction.OfPlayer);
        string disconnectedText=disconnected.GetInspectString();
        c.Check("disconnected bay retains vanilla requirement and connection status", disconnected.GetComp<CompPowerTrader>().PowerNet==null &&
            disconnectedText.Contains("PowerNeeded".Translate()+": ") && disconnectedText.Contains("PowerNotConnected".Translate()));
        var power=q.GetComp<CompPowerTrader>();
        var control=lamp.GetComp<CompPowerTrader>();
        // Scenario Setup runs inside one update; flush the normal topology builder
        // before stepping ticks so queued spawned connectors are available.
        c.Map.powerNetManager.UpdatePowerNetsAndConnections_First();
        c.Event("power-topology",new { quantumNet=power.PowerNet!=null, lampNet=control.PowerNet!=null,
            batteryNet=battery.PowerNet!=null, stored=battery.StoredEnergy,
            quantumConnector=power.connectParent?.parent.def.defName, lampConnector=control.connectParent?.parent.def.defName });
        c.Wait(() => power.PowerNet!=null && power.PowerNet==control.PowerNet && power.PowerNet==battery.PowerNet && power.PowerOn && control.PowerOn,
            1200,20,"native power network did not connect and activate both consumers");
        c.Check("native network shared by battery and loads", power.PowerNet==control.PowerNet && power.PowerNet==battery.PowerNet);
        SetSwitch(lamp,false); SetSwitch(q,false);
        float start=battery.StoredEnergy; Step(c,600); float baselineLoss=start-battery.StoredEnergy;
        c.Check("off baseline has no consumer demand", Math.Abs(GridWatts(power))<.01f);
        c.Event("battery-window",new{stage="both-off-baseline",ticks=600,loss=baselineLoss});
        State(c,"both-off-baseline");

        SetSwitch(q,true);
        c.Wait(()=>power.PowerOn,1200,20,"quantum did not naturally repower");
        State(c,"quantum-on");
        float activeRate=GridWatts(power);
        start=battery.StoredEnergy; Step(c,600);float onLoss=start-battery.StoredEnergy;
        c.Check("quantum contributes actual demand when on",activeRate < -1000 && onLoss > baselineLoss+5);
        c.Event("battery-window",new{stage="quantum-on",ticks=600,loss=onLoss});

        Press(q);
        c.Check("button press schedules flick without applying switch",q.GetComp<CompFlickable>().WantsFlick() && q.GetComp<CompFlickable>().SwitchIsOn && power.PowerOn);
        State(c,"off-button-pending");
        Complete(q);
        c.Check("completed flick turns power off",!power.PowerOn && !q.GetComp<CompFlickable>().SwitchIsOn);
        State(c,"off-flick-completed");
        start=battery.StoredEnergy;
        for(int i=0;i<600;i++){q.GetInspectString();c.Step();}
        float offLoss=start-battery.StoredEnergy;
        c.Check("off state survives rare ticks and repeated inspection",!power.PowerOn && Math.Abs(GridWatts(power))<.01f);
        c.Check("off battery drain matches self-discharge baseline",Math.Abs(offLoss-baselineLoss)<.01f,new{baselineLoss,offLoss,onLoss});
        c.Check("display still reports nonzero configured cost",q.CalculateCurrentPowerCost()>0 && power.PowerOutput<0 && q.GetInspectString().Contains(q.CalculateCurrentPowerCost().ToString("F0")+" W"));
        c.Event("battery-window",new{stage="quantum-off",ticks=600,loss=offLoss});
        State(c,"off-after-600-ticks");

        q.AdjustCapacity(5);
        Find.Selector.ClearSelection();
        Find.Selector.Select(q);
        var sunlamp=q.GetGizmos().OfType<Command_Toggle>().Single(g=>g.defaultLabel=="HDH_BuiltInSunlampToggle".Translate().ToString());
        if(!sunlamp.isActive())sunlamp.toggleAction();
        State(c,"off-density-and-sunlamp-changed");
        Step(c,300);
        c.Check("density and lamp settings do not reactivate off building",!power.PowerOn && Math.Abs(GridWatts(power))<.01f);
        c.Check("built-in lamp stays dark while off",!c.Map.listerThings.AllThings.Any(t=>t.def.defName=="HDH_BuiltInSunlampCellLight"));

        SetSwitch(q,true);
        c.Wait(()=>power.PowerOn,1200,20,"quantum did not repower after changed settings");
        State(c,"quantum-on-after-settings");
        c.Check("repowered quantum demand matches configured cost",Math.Abs(GridWatts(power)+q.CalculateCurrentPowerCost())<.1f);
        float withSunlamp=q.CalculateCurrentPowerCost();
        sunlamp.toggleAction();
        float withoutSunlamp=q.CalculateCurrentPowerCost();
        c.Check("sunlamp-only toggle reduces demand without switching chamber off",power.PowerOn &&
            Math.Abs(withSunlamp-withoutSunlamp-300f)<.1f && Math.Abs(GridWatts(power)+withoutSunlamp)<.1f);
        State(c,"sunlamp-off-building-still-on");
        SetSwitch(q,false);
        SetSwitch(lamp,true);
        c.Wait(()=>control.PowerOn,1200,20,"control lamp did not naturally repower");
        c.Check("network counter detects ordinary control load",Math.Abs(GridWatts(power)-control.PowerOutput)<.1f && control.PowerOutput<0);
        State(c,"quantum-off-control-on");
        SetSwitch(lamp,false);
        State(c,"both-off-final");
    }
    public override void Assert(Context c)
    {
        var q=c.Subject<Building_HighDensityHydro>("quantum");
        c.Check("quantum remains off before reload",!q.GetComp<CompFlickable>().SwitchIsOn && !q.GetComp<CompPowerTrader>().PowerOn);
        c.Check("no consumer demand before reload",Math.Abs(GridWatts(q.GetComp<CompPowerTrader>()))<.01f);
    }
    public override void AfterReload(Context c)
    {
        var q=c.Subject<Building_HighDensityHydro>("quantum");
        var power=q.GetComp<CompPowerTrader>();
        c.Wait(()=>power.PowerNet!=null,1200,20,"power network not restored");
        q.GetInspectString();Step(c,300);
        c.Check("off switch survives reload",!q.GetComp<CompFlickable>().SwitchIsOn && !power.PowerOn);
        c.Check("no consumer demand after reload",Math.Abs(GridWatts(power))<.01f);
        State(c,"off-after-reload");
    }
}
