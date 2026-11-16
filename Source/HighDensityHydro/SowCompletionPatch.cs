using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HighDensityHydro
{
    [HarmonyPatch(typeof(JobDriver_PlantSow), "MakeNewToils")]
    internal static class SowCompletionPatch
    {
        private sealed class CompletionState { }
        private static readonly ConditionalWeakTable<JobDriver_PlantSow, CompletionState> Enabled =
            new ConditionalWeakTable<JobDriver_PlantSow, CompletionState>();

        internal static void ExposeCompletionState(JobDriver_PlantSow driver)
        {
            // Base ExposeData rebuilds toils before loaded pawns have a Map. Read this
            // marker first so a saved HDH job gets the same toil indices on reload.
            bool enabled = Enabled.TryGetValue(driver, out _);
            Scribe_Values.Look(ref enabled, "HDH_completionToil", false);
            if (enabled)
            {
                Enabled.GetValue(driver, _ => new CompletionState());
            }
        }

        private static Building_HighDensityHydro FindGrower(JobDriver_PlantSow driver)
        {
            var map = driver.pawn.Map;
            return map == null ? null : driver.job.targetA.Cell.GetPlantToGrowSettable(map) as Building_HighDensityHydro;
        }

        // Wrap other mods' returned toils so their completion toils normally run first.
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(JobDriver_PlantSow __instance, ref IEnumerable<Toil> __result)
        {
            var grower = FindGrower(__instance);
            if (grower == null && !Enabled.TryGetValue(__instance, out _))
            {
                return;
            }

            Enabled.GetValue(__instance, _ => new CompletionState());
            __result = WithCompletion(__result, __instance, grower);
        }

        private static IEnumerable<Toil> WithCompletion(IEnumerable<Toil> original,
            JobDriver_PlantSow driver, Building_HighDensityHydro grower)
        {
            foreach (var toil in original)
            {
                toil.AddPreInitAction(() =>
                {
                    // Vanilla changes target A from a cell to the plant when sowing starts.
                    // Recheck before spawning: other growers may have claimed the last slot
                    // while this pawn was walking. Physical plants persist these claims.
                    var currentGrower = grower ?? FindGrower(driver);
                    if (!driver.job.targetA.HasThing &&
                        (currentGrower == null || !currentGrower.TryBeginSowing(driver.job.plantDefToSow)))
                    {
                        driver.EndJobWith(JobCondition.Incompletable);
                    }
                });
                yield return toil;
            }

            var complete = ToilMaker.MakeToil("HDH_StoreCompletedSowing");
            complete.defaultCompleteMode = ToilCompleteMode.Instant;
            complete.initAction = () => (grower ?? FindGrower(driver))?.TryStoreSownPlant(driver.job.targetA.Thing as Plant);
            yield return complete;
        }
    }

    [HarmonyPatch(typeof(JobDriver_PlantSow), nameof(JobDriver_PlantSow.ExposeData))]
    internal static class SowCompletionSavePatch
    {
        public static void Prefix(JobDriver_PlantSow __instance) => SowCompletionPatch.ExposeCompletionState(__instance);
    }
}
