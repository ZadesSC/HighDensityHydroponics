using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HighDensityHydro
{
    [ExcludeFromCodeCoverage]
    [HarmonyPatch(typeof(WorkGiver_GrowerSow), nameof(WorkGiver_GrowerSow.JobOnCell))]
    internal static class GrowerSowPatch
    {
        public static void Prefix(Pawn pawn, IntVec3 c, ref Building_HighDensityHydro __state)
        {
            var map = pawn.Map;
            var settable = c.GetPlantToGrowSettable(map);
            if (!(settable is Building_HighDensityHydro grower) || grower.RequiresTemperatureCheck)
            {
                __state = null;
                return;
            }

            __state = SowTemperatureBypassContext.CurrentGrower;
            SowTemperatureBypassContext.CurrentGrower = grower;
        }

        public static void Finalizer(Building_HighDensityHydro __state)
        {
            SowTemperatureBypassContext.CurrentGrower = __state;
        }
    }

    [ExcludeFromCodeCoverage]
    [HarmonyPatch(typeof(PlantUtility), nameof(PlantUtility.GrowthSeasonNow))]
    [HarmonyPatch(new[] { typeof(IntVec3), typeof(Map), typeof(ThingDef) })]
    internal static class PlantUtility_GrowthSeasonNow_Patch
    {
        public static void Postfix(IntVec3 c, Map map, ref bool __result)
        {
            if (__result || !SowTemperatureBypassContext.ShouldIgnoreTemperatureAt(c, map))
            {
                return;
            }

            __result = true;
        }
    }

    internal static class SowTemperatureBypassContext
    {
        // Track the HDH grower currently being evaluated by vanilla WorkGiver_GrowerSow.JobOnCell().
        // The JobOnCell prefix stores that grower before vanilla runs, and the finalizer restores the
        // previous value afterward. The GrowthSeasonNow postfix then only bypasses the seasonal gate
        // when vanilla is checking a cell that belongs to this active grower.
        internal static Building_HighDensityHydro CurrentGrower { get; set; }

        internal static bool ShouldIgnoreTemperatureAt(IntVec3 c, Map map)
        {
            var grower = CurrentGrower;
            if (grower == null || map != grower.Map || grower.RequiresTemperatureCheck)
            {
                return false;
            }

            return grower.OccupiedRect().Contains(c);
        }
    }
}
