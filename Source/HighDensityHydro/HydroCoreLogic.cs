using System;
using UnityEngine;

namespace HighDensityHydro
{
    internal enum HarvestCompletionAction
    {
        ResetToSowing,
        ReturnToGrowing
    }

    internal sealed class HarvestCompletionResult
    {
        public HarvestCompletionResult(HarvestCompletionAction action, int storedPlants, int storedPlantsBuffer, float growth, float averageHarvestGrowth)
        {
            Action = action;
            StoredPlants = storedPlants;
            StoredPlantsBuffer = storedPlantsBuffer;
            Growth = growth;
            AverageHarvestGrowth = averageHarvestGrowth;
        }

        public HarvestCompletionAction Action { get; }

        public int StoredPlants { get; }

        public int StoredPlantsBuffer { get; }

        public float Growth { get; }

        public float AverageHarvestGrowth { get; }
    }

    internal static class HydroCoreLogic
    {
        private const int NoSunlightDamageThresholdTicks = 450000;

        public static int SanitizePlantsPerLayer(int plantsPerLayer)
        {
            return Math.Max(1, plantsPerLayer);
        }

        public static int ClampScalingLevel(int currentLevel, int offset, int maxLevel, int minLevel = 0)
        {
            var nextLevel = currentLevel + offset;
            if (nextLevel < minLevel)
            {
                return minLevel;
            }

            if (nextLevel > maxLevel)
            {
                return maxLevel;
            }

            return nextLevel;
        }

        public static int CalculateCapacity(int baseCapacity, int scalingLevel, int plantsPerLayer)
        {
            return baseCapacity + scalingLevel * plantsPerLayer;
        }

        public static float CalculatePowerCost(float baseConsumption, float basePowerIncrease, float capacityExponent, int scalingLevel)
        {
            return baseConsumption + (basePowerIncrease * Mathf.Pow(capacityExponent, scalingLevel));
        }

        public static float CalculateGlowGrowthRate(float averageGlow, float minGlow, float optimalGlow)
        {
            if (averageGlow < minGlow)
            {
                return 0f;
            }

            if (optimalGlow <= minGlow)
            {
                return 1f;
            }

            return Mathf.Clamp01((averageGlow - minGlow) / (optimalGlow - minGlow));
        }

        public static float CalculateGrowthDelta(float fertilitySensitivity, float fertility, float growthRateFromGlow, float growDays)
        {
            if (growDays <= 0f)
            {
                return 0f;
            }

            var baseGrowthPerLongTick = (1f / (60000f * growDays)) * 2000f;
            return CalculateFertilityGrowthRateFactor(fertilitySensitivity, fertility) * growthRateFromGlow * baseGrowthPerLongTick;
        }

        public static float CalculateFertilityGrowthRateFactor(float fertilitySensitivity, float fertility)
        {
            return fertility * fertilitySensitivity + (1f - fertilitySensitivity);
        }

        public static int UpdateUnlitTicks(bool lightRequirementEnabled, bool requiresLightCheck, float growthRateFromGlow, int currentUnlitTicks, int tickInterval)
        {
            if (!lightRequirementEnabled || !requiresLightCheck || growthRateFromGlow > 0f)
            {
                return 0;
            }

            return currentUnlitTicks + Math.Max(0, tickInterval);
        }

        public static float CalculateDyingDamagePerTick(
            bool limitedLifespan,
            int plantAge,
            int lifespanTicks,
            bool diesWithoutSunlight,
            int unlitTicks,
            bool requiresAtmosphereCheck,
            bool exposedToVacuum,
            float vacuumAmount)
        {
            var damage = 0f;
            if (limitedLifespan && plantAge > lifespanTicks)
            {
                damage = Mathf.Max(damage, 0.005f);
            }

            if (diesWithoutSunlight && unlitTicks > NoSunlightDamageThresholdTicks)
            {
                damage = Mathf.Max(damage, 0.005f);
            }

            if (requiresAtmosphereCheck && exposedToVacuum)
            {
                damage = Mathf.Max(damage, vacuumAmount);
            }

            return damage;
        }

        public static HarvestCompletionResult ResolveHarvestCompletion(
            float harvestAfterGrowth,
            int storedPlantsBuffer,
            float averageHarvestGrowth,
            float fertility,
            float fertilitySensitivity,
            float growDays,
            int plantAge,
            int lifespanTicks)
        {
            if (harvestAfterGrowth == 0f || storedPlantsBuffer <= 0)
            {
                return new HarvestCompletionResult(HarvestCompletionAction.ResetToSowing, 0, 0, 0f, 0f);
            }

            averageHarvestGrowth /= storedPlantsBuffer;

            var growthRemaining = 1f - averageHarvestGrowth;
            var growthPerTick = CalculateFertilityGrowthRateFactor(fertilitySensitivity, fertility) * (2000f / (60000f * growDays));
            if (growthPerTick <= 0f)
            {
                return new HarvestCompletionResult(HarvestCompletionAction.ResetToSowing, 0, 0, 0f, 0f);
            }

            var estimatedTicksToGrow = Mathf.Max(0f, (growthRemaining / growthPerTick) * 2000f * 1.05f);
            var ageAfterNextGrow = plantAge + (int)estimatedTicksToGrow;
            var willDieBeforeNextHarvest = lifespanTicks > 0 && ageAfterNextGrow > lifespanTicks;
            if (willDieBeforeNextHarvest)
            {
                return new HarvestCompletionResult(HarvestCompletionAction.ResetToSowing, 0, 0, 0f, 0f);
            }

            return new HarvestCompletionResult(
                HarvestCompletionAction.ReturnToGrowing,
                storedPlantsBuffer,
                0,
                averageHarvestGrowth,
                0f);
        }

        public static bool ShouldConsumeDbhFuel(bool hasPipeNet, bool anyRecyclers)
        {
            return !hasPipeNet || !anyRecyclers;
        }

        public static float CalculateDbhFuelUse(int storedPlantCount)
        {
            return 0.05f * Math.Max(0, storedPlantCount);
        }

        public static float CalculateVanillaPowerLossDamage(int tickInterval, int rareTickInterval = 250)
        {
            if (tickInterval <= 0 || rareTickInterval <= 0)
            {
                return 0f;
            }

            return tickInterval / rareTickInterval;
        }
    }
}
