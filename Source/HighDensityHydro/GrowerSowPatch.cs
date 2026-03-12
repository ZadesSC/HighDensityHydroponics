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
        public static bool Prefix(Pawn pawn, IntVec3 c, bool forced, ref Job __result)
        {
            var map = pawn.Map;
            var settable = c.GetPlantToGrowSettable(map);
            if (!(settable is Building_HighDensityHydro grower))
            {
                return true;
            }

            var ignoreTemperature = !grower.RequiresTemperatureCheck;
            var ignoreAtmosphere = !grower.RequiresAtmosphereCheck;
            if (!ignoreTemperature && !ignoreAtmosphere)
            {
                return true;
            }

            if (!grower.CanAcceptSowNow())
            {
                __result = null;
                return false;
            }

            var wantedPlantDef = WorkGiver_Grower.CalculateWantedPlantDef(c, map);
            if (wantedPlantDef == null)
            {
                __result = null;
                return false;
            }

            if (!ignoreAtmosphere && c.GetVacuum(map) >= 0.5f)
            {
                __result = null;
                return false;
            }

            if (!ignoreTemperature && !PlantUtility.GrowthSeasonNow(c, map, wantedPlantDef))
            {
                __result = null;
                return false;
            }

            var thingList = c.GetThingList(map);
            var growZone = c.GetZone(map) as Zone_Growing;
            var hasBlueprintOrFrame = false;
            for (var i = 0; i < thingList.Count; i++)
            {
                var thing = thingList[i];
                if (thing.def == wantedPlantDef)
                {
                    __result = null;
                    return false;
                }

                if ((thing is Blueprint || thing is Frame) && thing.Faction == pawn.Faction)
                {
                    hasBlueprintOrFrame = true;
                }
            }

            if (hasBlueprintOrFrame)
            {
                var edifice = c.GetEdifice(map);
                if (edifice == null || edifice.def.fertility < 0f)
                {
                    __result = null;
                    return false;
                }
            }

            if (wantedPlantDef.plant.diesToLight)
            {
                if (!c.Roofed(map) && !map.GameConditionManager.IsAlwaysDarkOutside)
                {
                    JobFailReason.Is("CantSowCavePlantBecauseUnroofed".Translate());
                    __result = null;
                    return false;
                }

                if (map.glowGrid.GroundGlowAt(c, ignoreCavePlants: true) > 0f)
                {
                    JobFailReason.Is("CantSowCavePlantBecauseOfLight".Translate());
                    __result = null;
                    return false;
                }
            }

            if (wantedPlantDef.plant.interferesWithRoof && c.Roofed(map))
            {
                __result = null;
                return false;
            }

            var plant = c.GetPlant(map);
            if (plant != null && plant.def.plant.blockAdjacentSow)
            {
                if (!pawn.CanReserve(plant, 1, -1, null, forced) || plant.IsForbidden(pawn))
                {
                    __result = null;
                    return false;
                }

                if (growZone != null && !growZone.allowCut)
                {
                    __result = null;
                    return false;
                }

                if (!forced && plant.TryGetComp<CompPlantPreventCutting>(out var preventCuttingComp) && preventCuttingComp.PreventCutting)
                {
                    __result = null;
                    return false;
                }

                if (!PlantUtility.PawnWillingToCutPlant_Job(plant, pawn))
                {
                    __result = null;
                    return false;
                }

                __result = JobMaker.MakeJob(JobDefOf.CutPlant, plant);
                return false;
            }

            var adjacentBlocker = PlantUtility.AdjacentSowBlocker(wantedPlantDef, c, map);
            if (adjacentBlocker != null)
            {
                if (adjacentBlocker is Plant blockerPlant &&
                    pawn.CanReserveAndReach(blockerPlant, PathEndMode.Touch, Danger.Deadly, 1, -1, null, forced) &&
                    !blockerPlant.IsForbidden(pawn))
                {
                    var blockerSettable = blockerPlant.Position.GetPlantToGrowSettable(blockerPlant.Map);
                    if (blockerSettable == null || blockerSettable.GetPlantDefToGrow() != blockerPlant.def)
                    {
                        var blockerZone = blockerPlant.Position.GetZone(map) as Zone_Growing;
                        if ((growZone != null && !growZone.allowCut) || (blockerZone != null && !blockerZone.allowCut && blockerPlant.def == blockerZone.GetPlantDefToGrow()))
                        {
                            __result = null;
                            return false;
                        }

                        if (!forced && adjacentBlocker.TryGetComp(out CompPlantPreventCutting blockerPreventCuttingComp) && blockerPreventCuttingComp.PreventCutting)
                        {
                            __result = null;
                            return false;
                        }

                        if (PlantUtility.TreeMarkedForExtraction(blockerPlant) || !PlantUtility.PawnWillingToCutPlant_Job(blockerPlant, pawn))
                        {
                            __result = null;
                            return false;
                        }

                        __result = JobMaker.MakeJob(JobDefOf.CutPlant, blockerPlant);
                        return false;
                    }
                }

                __result = null;
                return false;
            }

            if (wantedPlantDef.plant.sowMinSkill > 0 &&
                ((pawn.skills != null && pawn.skills.GetSkill(SkillDefOf.Plants).Level < wantedPlantDef.plant.sowMinSkill) ||
                 (pawn.IsColonyMech && pawn.RaceProps.mechFixedSkillLevel < wantedPlantDef.plant.sowMinSkill)))
            {
                JobFailReason.Is("UnderAllowedSkill".Translate(wantedPlantDef.plant.sowMinSkill), "sowing".Translate());
                __result = null;
                return false;
            }

            for (var i = 0; i < thingList.Count; i++)
            {
                var thing = thingList[i];
                if (!thing.def.BlocksPlanting())
                {
                    continue;
                }

                if (!pawn.CanReserve(thing, 1, -1, null, forced))
                {
                    __result = null;
                    return false;
                }

                if (thing.def.category == ThingCategory.Plant)
                {
                    if (thing.IsForbidden(pawn))
                    {
                        __result = null;
                        return false;
                    }

                    if (growZone != null && !growZone.allowCut)
                    {
                        __result = null;
                        return false;
                    }

                    if (!forced && plant != null && plant.TryGetComp<CompPlantPreventCutting>(out var comp) && comp.PreventCutting)
                    {
                        __result = null;
                        return false;
                    }

                    if (!PlantUtility.PawnWillingToCutPlant_Job(thing, pawn) || PlantUtility.TreeMarkedForExtraction(thing))
                    {
                        __result = null;
                        return false;
                    }

                    __result = JobMaker.MakeJob(JobDefOf.CutPlant, thing);
                    return false;
                }

                if (thing.def.EverHaulable)
                {
                    __result = HaulAIUtility.HaulAsideJobFor(pawn, thing);
                    return false;
                }

                __result = null;
                return false;
            }

            if (!wantedPlantDef.CanNowPlantAt(c, map) || !pawn.CanReserve(c, 1, -1, null, forced))
            {
                __result = null;
                return false;
            }

            __result = JobMaker.MakeJob(JobDefOf.Sow, c);
            __result.plantDefToSow = wantedPlantDef;
            return false;
        }
    }
}
