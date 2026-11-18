using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace HighDensityHydro
{
    // Refills an HDH surface tile as soon as its plant is collected, then lets the same vanilla
    // harvest job continue with the replacement. The rare-tick refill remains as recovery.
    [HarmonyPatch(typeof(Plant), nameof(Plant.PlantCollected))]
    internal static class HarvestContinuationPatch
    {
        internal sealed class CollectedPlant
        {
            public Building_HighDensityHydro Grower;
            public IntVec3 Cell;
            public float HarvestWork;
        }

        // Harvest work collected per job, keyed by driver because vanilla pools Job objects.
        // It is not saved; after a reload a job can earn at most one more vanilla-sized batch.
        private static readonly ConditionalWeakTable<JobDriver, StrongBox<float>> CollectedWork =
            new ConditionalWeakTable<JobDriver, StrongBox<float>>();

        public static void Prefix(Plant __instance, out CollectedPlant __state)
        {
            __state = null;
            Map map = __instance.Map;
            if (map == null || !__instance.Spawned)
            {
                return;
            }

            // Vanilla destroys most harvested plants inside PlantCollected, so capture the tile first.
            if (__instance.Position.GetPlantToGrowSettable(map) is Building_HighDensityHydro grower && grower.IsHarvesting)
            {
                __state = new CollectedPlant
                {
                    Grower = grower,
                    Cell = __instance.Position,
                    HarvestWork = __instance.def.plant?.harvestWork ?? 0f
                };
            }
        }

        public static void Postfix(Pawn by, CollectedPlant __state)
        {
            if (__state == null)
            {
                return;
            }

            Plant replacement = __state.Grower.TryRestockHarvestedCell(__state.Cell);
            TryContinueHarvestJob(by, replacement, __state.HarvestWork);
        }

        private static void TryContinueHarvestJob(Pawn pawn, Plant plant, float collectedWork)
        {
            Job job = pawn?.CurJob;
            if (job == null || job.def != JobDefOf.Harvest || !(pawn.jobs.curDriver is JobDriver_PlantHarvest driver))
            {
                return;
            }

            StrongBox<float> collected = CollectedWork.GetValue(driver, _ => new StrongBox<float>(0f));
            collected.Value += collectedWork;

            if (plant == null || !plant.Spawned || !CanHarvest(pawn, plant))
            {
                return;
            }

            float queuedWork = 0f;
            List<LocalTargetInfo> queue = job.targetQueueA;
            if (queue != null)
            {
                foreach (LocalTargetInfo target in queue)
                {
                    queuedWork += (target.Thing as Plant)?.def.plant?.harvestWork ?? 0f;
                }
            }

            if (!HydroCoreLogic.ShouldContinueHarvestJob(collected.Value, queuedWork, plant.def.plant.harvestWork))
            {
                return;
            }

            // Vanilla's harvest loop rereads this queue after each plant; PlantCollected itself
            // appends to it for stumps and blighted neighbors.
            job.AddQueuedTarget(TargetIndex.A, plant);
            pawn.Reserve(plant, job, 1, -1, null, false);
        }

        // Mirrors the per-plant checks in WorkGiver_GrowerHarvest.HasJobOnCell.
        private static bool CanHarvest(Pawn pawn, Plant plant)
        {
            return plant.HarvestableNow && plant.LifeStage == PlantLifeStage.Mature && plant.CanYieldNow() &&
                plant.def.plant.autoHarvestable && !plant.IsForbidden(pawn) &&
                PlantUtility.PawnWillingToCutPlant_Job(plant, pawn) && pawn.CanReserve(plant);
        }
    }
}
