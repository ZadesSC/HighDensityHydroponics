using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace HighDensityHydroponics.Tests;

internal static class HdhReflection
{
    private static Type buildingType;
    private static FieldInfo bayStageField;
    private static FieldInfo currentPlantDefField;
    private static FieldInfo storedPlantsField;
    private static FieldInfo storedPlantsBufferField;
    private static FieldInfo plantHealthField;
    private static FieldInfo plantGrowthField;
    private static FieldInfo plantAgeField;
    private static FieldInfo averageHarvestGrowthField;
    private static FieldInfo powerCompCachedField;
    private static MethodInfo tickLongMethod;
    private static MethodInfo tickRareMethod;
    private static MethodInfo adjustCapacityMethod;
    private static MethodInfo calculateCurrentPowerCostMethod;
    private static MethodInfo setPlantDefToGrowMethod;
    private static PropertyInfo maxPlantCapacityProperty;
    private static PropertyInfo plantGrowthProperty;
    private static PropertyInfo plantHealthProperty;
    private static PropertyInfo storedPlantCountProperty;
    private static PropertyInfo requiresLightCheckProperty;
    private static PropertyInfo requiresTemperatureCheckProperty;
    private static PropertyInfo requiresAtmosphereCheckProperty;
    private static PropertyInfo powerScalesCapacityProperty;
    private static PropertyInfo plantsPerLayerProperty;
    private static PropertyInfo currentPowerScalingLevelProperty;
    private static FieldInfo settingsField;
    private static FieldInfo killPlantsOnNoPowerField;

    public static Thing SpawnBuilding(string defName, Map map)
    {
        EnsureInitialized();
        var def = DefDatabase<ThingDef>.GetNamed(defName, false);
        var cell = FindPlacementCell(map, def);
        var thing = ThingMaker.MakeThing(def);
        return GenSpawn.Spawn(thing, cell, map, Rot4.North, WipeMode.Vanish);
    }

    public static void SeedGrowingState(Thing building, ThingDef plantDef, bool powered, int storedPlants, float growth, int age)
    {
        SeedState(building, plantDef, "Growing", powered, storedPlants, growth, age, plantDef.BaseMaxHitPoints, 0, 0f);
    }

    public static void SeedState(
        Thing building,
        ThingDef plantDef,
        string stageName,
        bool powered,
        int storedPlants,
        float growth,
        int age,
        float health,
        int storedPlantsBuffer,
        float averageHarvestGrowth)
    {
        EnsureInitialized();
        if (plantDef != null)
        {
            SetPlantDefToGrow(building, plantDef);
        }

        SetField(currentPlantDefField, building, plantDef);
        SetField(storedPlantsField, building, storedPlants);
        SetField(storedPlantsBufferField, building, storedPlantsBuffer);
        SetField(plantGrowthField, building, growth);
        SetField(plantAgeField, building, age);
        SetField(plantHealthField, building, health);
        SetField(averageHarvestGrowthField, building, averageHarvestGrowth);
        SetBayStage(building, stageName);

        var powerComp = building.TryGetComp<CompPowerTrader>();
        if (powerComp != null)
        {
            PrimePowerComp(building, powerComp);
            powerComp.PowerOn = powered;
        }
    }

    public static void PrimePowerComp(Thing building)
    {
        EnsureInitialized();
        var powerComp = building.TryGetComp<CompPowerTrader>();
        if (powerComp != null)
        {
            PrimePowerComp(building, powerComp);
        }
    }

    public static void ClearAll(Map map)
    {
        EnsureInitialized();
        foreach (var thing in map.listerThings.AllThings.Where(thing => buildingType.IsInstanceOfType(thing)).ToList())
        {
            thing.Destroy(DestroyMode.Vanish);
        }
    }

    public static void TickLong(Thing building)
    {
        EnsureInitialized();
        tickLongMethod.Invoke(building, null);
    }

    public static void TickRare(Thing building)
    {
        EnsureInitialized();
        tickRareMethod.Invoke(building, null);
    }

    public static void AdjustCapacity(Thing building, int offset)
    {
        EnsureInitialized();
        adjustCapacityMethod.Invoke(building, new object[] { offset });
    }

    public static int MaxPlantCapacity(Thing building)
    {
        EnsureInitialized();
        return (int)maxPlantCapacityProperty.GetValue(building, null);
    }

    public static float PlantGrowth(Thing building)
    {
        EnsureInitialized();
        return (float)plantGrowthProperty.GetValue(building, null);
    }

    public static float PlantHealth(Thing building)
    {
        EnsureInitialized();
        return (float)plantHealthProperty.GetValue(building, null);
    }

    public static int StoredPlantCount(Thing building)
    {
        EnsureInitialized();
        return (int)storedPlantCountProperty.GetValue(building, null);
    }

    public static int StoredPlantBufferCount(Thing building)
    {
        EnsureInitialized();
        return (int)storedPlantsBufferField.GetValue(building);
    }

    public static string BayStageName(Thing building)
    {
        EnsureInitialized();
        return bayStageField.GetValue(building)?.ToString() ?? "<null>";
    }

    public static string CurrentPlantDefName(Thing building)
    {
        EnsureInitialized();
        return ((ThingDef)currentPlantDefField.GetValue(building))?.defName ?? "<null>";
    }

    public static float AverageHarvestGrowth(Thing building)
    {
        EnsureInitialized();
        return (float)averageHarvestGrowthField.GetValue(building);
    }

    public static float CurrentPowerCost(Thing building)
    {
        EnsureInitialized();
        return Convert.ToSingle(calculateCurrentPowerCostMethod.Invoke(building, null));
    }

    public static bool RequiresLightCheck(Thing building)
    {
        EnsureInitialized();
        return (bool)requiresLightCheckProperty.GetValue(building, null);
    }

    public static bool RequiresTemperatureCheck(Thing building)
    {
        EnsureInitialized();
        return (bool)requiresTemperatureCheckProperty.GetValue(building, null);
    }

    public static bool RequiresAtmosphereCheck(Thing building)
    {
        EnsureInitialized();
        return (bool)requiresAtmosphereCheckProperty.GetValue(building, null);
    }

    public static bool PowerScalesCapacity(Thing building)
    {
        EnsureInitialized();
        return (bool)powerScalesCapacityProperty.GetValue(building, null);
    }

    public static int PlantsPerLayer(Thing building)
    {
        EnsureInitialized();
        return (int)plantsPerLayerProperty.GetValue(building, null);
    }

    public static int CurrentPowerScalingLevel(Thing building)
    {
        EnsureInitialized();
        return (int)currentPowerScalingLevelProperty.GetValue(building, null);
    }

    public static void SetPlantDefToGrow(Thing building, ThingDef plantDef)
    {
        EnsureInitialized();
        setPlantDefToGrowMethod.Invoke(building, new object[] { plantDef });
    }

    public static Plant SpawnPlantOnBuilding(Thing building, ThingDef plantDef, float growth, int age = 0)
    {
        EnsureInitialized();
        var map = building.Map;
        foreach (var cell in building.OccupiedRect().Cells)
        {
            var existingPlant = map.thingGrid.ThingsListAt(cell).OfType<Plant>().FirstOrDefault();
            if (existingPlant != null)
            {
                continue;
            }

            var plant = (Plant)ThingMaker.MakeThing(plantDef);
            var spawned = (Plant)GenSpawn.Spawn(plant, cell, map, WipeMode.Vanish);
            spawned.Growth = growth;
            spawned.Age = age;
            return spawned;
        }

        return null;
    }

    public static int SpawnedPlantCount(Thing building)
    {
        EnsureInitialized();
        var map = building.Map;
        return building.OccupiedRect().Cells
            .SelectMany(cell => map.thingGrid.ThingsListAt(cell))
            .OfType<Plant>()
            .Count();
    }

    public static bool KillPlantsOnNoPowerSetting()
    {
        EnsureInitialized();
        var settings = settingsField?.GetValue(null);
        if (settings == null || killPlantsOnNoPowerField == null)
        {
            return false;
        }

        return (bool)killPlantsOnNoPowerField.GetValue(settings);
    }

    public static void SetKillPlantsOnNoPowerSetting(bool value)
    {
        EnsureInitialized();
        var settings = settingsField?.GetValue(null);
        killPlantsOnNoPowerField?.SetValue(settings, value);
    }

    public static Dictionary<string, string> SnapshotValues(Thing building)
    {
        return new Dictionary<string, string>
        {
            { "thingId", building.ThingID },
            { "defName", building.def.defName },
            { "position", building.Position.ToString() },
            { "bayStage", BayStageName(building) },
            { "plantDef", CurrentPlantDefName(building) },
            { "growth", PlantGrowth(building).ToString("F3") },
            { "health", PlantHealth(building).ToString("F3") },
            { "storedPlants", StoredPlantCount(building).ToString() },
            { "storedPlantsBuffer", StoredPlantBufferCount(building).ToString() },
            { "averageHarvestGrowth", AverageHarvestGrowth(building).ToString("F3") },
            { "maxCapacity", MaxPlantCapacity(building).ToString() },
            { "powerCost", CurrentPowerCost(building).ToString("F3") },
        };
    }

    public static Thing FindFirstSpawnedBuilding(Map map)
    {
        EnsureInitialized();
        return map.listerThings.AllThings.FirstOrDefault(thing => buildingType.IsInstanceOfType(thing));
    }

    private static void EnsureInitialized()
    {
        if (buildingType != null)
        {
            return;
        }

        buildingType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("HighDensityHydro.Building_HighDensityHydro", false))
            .FirstOrDefault(type => type != null);

        if (buildingType == null)
        {
            return;
        }

        bayStageField = buildingType.GetField("_bayStage", BindingFlags.Instance | BindingFlags.NonPublic);
        currentPlantDefField = buildingType.GetField("_currentPlantDefToGrow", BindingFlags.Instance | BindingFlags.NonPublic);
        storedPlantsField = buildingType.GetField("_numStoredPlants", BindingFlags.Instance | BindingFlags.NonPublic);
        storedPlantsBufferField = buildingType.GetField("_numStoredPlantsBuffer", BindingFlags.Instance | BindingFlags.NonPublic);
        plantHealthField = buildingType.GetField("_plantHealth", BindingFlags.Instance | BindingFlags.NonPublic);
        plantGrowthField = buildingType.GetField("_curGrowth", BindingFlags.Instance | BindingFlags.NonPublic);
        plantAgeField = buildingType.GetField("_plantAge", BindingFlags.Instance | BindingFlags.NonPublic);
        averageHarvestGrowthField = buildingType.GetField("_averageHarvestGrowth", BindingFlags.Instance | BindingFlags.NonPublic);
        powerCompCachedField = buildingType.GetField("_powerCompCached", BindingFlags.Instance | BindingFlags.NonPublic);
        tickLongMethod = buildingType.GetMethod("TickLong", BindingFlags.Instance | BindingFlags.Public);
        tickRareMethod = buildingType.GetMethod("TickRare", BindingFlags.Instance | BindingFlags.Public);
        adjustCapacityMethod = buildingType.GetMethod("AdjustCapacity", BindingFlags.Instance | BindingFlags.Public);
        calculateCurrentPowerCostMethod = buildingType.GetMethod("CalculateCurrentPowerCost", BindingFlags.Instance | BindingFlags.Public);
        setPlantDefToGrowMethod = buildingType.GetMethod("SetPlantDefToGrow", BindingFlags.Instance | BindingFlags.Public);
        maxPlantCapacityProperty = buildingType.GetProperty("MaxPlantCapacity", BindingFlags.Instance | BindingFlags.Public);
        plantGrowthProperty = buildingType.GetProperty("PlantGrowth", BindingFlags.Instance | BindingFlags.Public);
        plantHealthProperty = buildingType.GetProperty("PlantHealth", BindingFlags.Instance | BindingFlags.Public);
        storedPlantCountProperty = buildingType.GetProperty("StoredPlantCount", BindingFlags.Instance | BindingFlags.Public);
        requiresLightCheckProperty = buildingType.GetProperty("RequiresLightCheck", BindingFlags.Instance | BindingFlags.Public);
        requiresTemperatureCheckProperty = buildingType.GetProperty("RequiresTemperatureCheck", BindingFlags.Instance | BindingFlags.Public);
        requiresAtmosphereCheckProperty = buildingType.GetProperty("RequiresAtmosphereCheck", BindingFlags.Instance | BindingFlags.Public);
        powerScalesCapacityProperty = buildingType.GetProperty("PowerScalesCapacity", BindingFlags.Instance | BindingFlags.Public);
        plantsPerLayerProperty = buildingType.GetProperty("PlantsPerLayer", BindingFlags.Instance | BindingFlags.Public);
        currentPowerScalingLevelProperty = buildingType.GetProperty("CurrentPowerScalingLevel", BindingFlags.Instance | BindingFlags.Public);

        var modType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("HighDensityHydro.HDH_Mod", false))
            .FirstOrDefault(type => type != null);
        var settingsType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("HighDensityHydro.HDH_Settings", false))
            .FirstOrDefault(type => type != null);
        settingsField = modType?.GetField("settings", BindingFlags.Static | BindingFlags.Public);
        killPlantsOnNoPowerField = settingsType?.GetField("killPlantsOnNoPower", BindingFlags.Instance | BindingFlags.Public);
    }

    private static void SetBayStage(object building, string name)
    {
        var value = Enum.Parse(bayStageField.FieldType, name);
        bayStageField.SetValue(building, value);
    }

    private static void PrimePowerComp(Thing building, CompPowerTrader powerComp)
    {
        if (powerCompCachedField != null)
        {
            powerCompCachedField.SetValue(building, powerComp);
        }
    }

    private static void SetField(FieldInfo field, object instance, object value)
    {
        field.SetValue(instance, value);
    }

    private static IntVec3 FindPlacementCell(Map map, ThingDef def)
    {
        var center = map.Center;
        for (var radius = 0; radius < 30; radius++)
        {
            for (var x = center.x - radius; x <= center.x + radius; x++)
            {
                for (var z = center.z - radius; z <= center.z + radius; z++)
                {
                    var cell = new IntVec3(x, 0, z);
                    if (CanPlace(def, map, cell))
                    {
                        return cell;
                    }
                }
            }
        }

        return center;
    }

    private static bool CanPlace(ThingDef def, Map map, IntVec3 cell)
    {
        if (!cell.InBounds(map))
        {
            return false;
        }

        foreach (var occupied in GenAdj.OccupiedRect(cell, Rot4.North, def.size))
        {
            if (!occupied.InBounds(map) || !occupied.Standable(map) || occupied.GetEdifice(map) != null)
            {
                return false;
            }
        }

        return true;
    }
}
