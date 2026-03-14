using System;
using HighDensityHydro;
using Xunit;

namespace HighDensityHydro.UnitTests
{
    public class HydroCoreLogicTests
    {
        [Fact]
        public void ClampScalingLevel_ClampsLowAndHighBounds()
        {
            Assert.Equal(0, HydroCoreLogic.ClampScalingLevel(2, -5, 10));
            Assert.Equal(10, HydroCoreLogic.ClampScalingLevel(9, 5, 10));
            Assert.Equal(5, HydroCoreLogic.ClampScalingLevel(3, 2, 10));
        }

        [Fact]
        public void ClampScalingLevel_RespectsMinimumLevelWhenProvided()
        {
            Assert.Equal(1, HydroCoreLogic.ClampScalingLevel(1, -5, 10, 1));
            Assert.Equal(1, HydroCoreLogic.ClampScalingLevel(0, 0, 10, 1));
        }

        [Fact]
        public void CalculateCapacity_UsesPlantsPerLayer()
        {
            Assert.Equal(4, HydroCoreLogic.CalculateCapacity(4, 0, 4));
            Assert.Equal(16, HydroCoreLogic.CalculateCapacity(4, 3, 4));
        }

        [Fact]
        public void CalculatePowerCost_GrowsExponentiallyWithScaling()
        {
            var baseCost = HydroCoreLogic.CalculatePowerCost(2800f, 50f, 1.2f, 0);
            var scaledCost = HydroCoreLogic.CalculatePowerCost(2800f, 50f, 1.2f, 3);

            Assert.Equal(2850f, baseCost, 3);
            Assert.True(scaledCost > baseCost);
        }

        [Fact]
        public void SanitizePlantsPerLayer_NeverReturnsLessThanOne()
        {
            Assert.Equal(1, HydroCoreLogic.SanitizePlantsPerLayer(0));
            Assert.Equal(1, HydroCoreLogic.SanitizePlantsPerLayer(-2));
            Assert.Equal(6, HydroCoreLogic.SanitizePlantsPerLayer(6));
        }

        [Fact]
        public void CalculateGlowGrowthRate_RespectsThresholds()
        {
            Assert.Equal(0f, HydroCoreLogic.CalculateGlowGrowthRate(0.2f, 0.3f, 0.7f), 3);
            Assert.Equal(0.5f, HydroCoreLogic.CalculateGlowGrowthRate(0.5f, 0.3f, 0.7f), 3);
            Assert.Equal(1f, HydroCoreLogic.CalculateGlowGrowthRate(0.8f, 0.3f, 0.7f), 3);
        }

        [Fact]
        public void CalculateGrowthDelta_ReturnsZeroWhenGrowDaysInvalid()
        {
            Assert.Equal(0f, HydroCoreLogic.CalculateGrowthDelta(1f, 2.8f, 1f, 0f), 3);
        }

        [Fact]
        public void CalculateGrowthDelta_UsesFertilityAndGlow()
        {
            var delta = HydroCoreLogic.CalculateGrowthDelta(0.4f, 2.8f, 1f, 10f);
            var expected = 1.72f * (2000f / (60000f * 10f));

            Assert.Equal(expected, delta, 4);
        }

        [Fact]
        public void CalculateGrowthDelta_KeepsFertilityInsensitivePlantsGrowing()
        {
            var delta = HydroCoreLogic.CalculateGrowthDelta(0f, 2.8f, 1f, 10f);
            var expected = 2000f / (60000f * 10f);

            Assert.Equal(expected, delta, 4);
        }

        [Fact]
        public void CalculateVanillaPowerLossDamage_MatchesRareTickCadence()
        {
            Assert.Equal(1f, HydroCoreLogic.CalculateVanillaPowerLossDamage(250), 3);
            Assert.Equal(8f, HydroCoreLogic.CalculateVanillaPowerLossDamage(2000), 3);
        }

        [Fact]
        public void CalculateDyingDamagePerTick_PicksWorstApplicableDamage()
        {
            var lifespanOnly = HydroCoreLogic.CalculateDyingDamagePerTick(true, 1001, 1000, false, 0, false, false, 0f);
            var vacuumOnly = HydroCoreLogic.CalculateDyingDamagePerTick(false, 0, 0, false, 0, true, true, 0.75f);
            var combined = HydroCoreLogic.CalculateDyingDamagePerTick(true, 1001, 1000, true, 450001, true, true, 0.75f);

            Assert.Equal(0.005f, lifespanOnly, 3);
            Assert.Equal(0.75f, vacuumOnly, 3);
            Assert.Equal(0.75f, combined, 3);
        }

        [Fact]
        public void CalculateDyingDamagePerTick_AddsNoSunlightDamageAfterThreshold()
        {
            var darknessOnly = HydroCoreLogic.CalculateDyingDamagePerTick(false, 0, 0, true, 450001, false, false, 0f);

            Assert.Equal(0.005f, darknessOnly, 3);
        }

        [Fact]
        public void UpdateUnlitTicks_AccumulatesOnlyWhenLightChecksAreActiveAndInsufficient()
        {
            Assert.Equal(2000, HydroCoreLogic.UpdateUnlitTicks(true, true, 0f, 0, 2000));
            Assert.Equal(0, HydroCoreLogic.UpdateUnlitTicks(true, true, 0.25f, 4000, 2000));
            Assert.Equal(0, HydroCoreLogic.UpdateUnlitTicks(false, true, 0f, 4000, 2000));
            Assert.Equal(0, HydroCoreLogic.UpdateUnlitTicks(true, false, 0f, 4000, 2000));
        }

        [Fact]
        public void ResolveHarvestCompletion_ResetsSingleHarvestPlants()
        {
            var result = HydroCoreLogic.ResolveHarvestCompletion(0f, 3, 1.2f, 2.8f, 1f, 4f, 2500, 0);

            Assert.Equal(HarvestCompletionAction.ResetToSowing, result.Action);
            Assert.Equal(0, result.StoredPlants);
            Assert.Equal(0f, result.Growth, 3);
        }

        [Fact]
        public void ResolveHarvestCompletion_ResetsWhenNoBufferedPlantsExist()
        {
            var result = HydroCoreLogic.ResolveHarvestCompletion(1f, 0, 0f, 2.8f, 1f, 4f, 2500, 0);
            Assert.Equal(HarvestCompletionAction.ResetToSowing, result.Action);
        }

        [Fact]
        public void ResolveHarvestCompletion_ResetsWhenPlantWouldDieBeforeRegrowth()
        {
            var result = HydroCoreLogic.ResolveHarvestCompletion(1f, 2, 0.4f, 2.8f, 1f, 30f, 10000, 10050);
            Assert.Equal(HarvestCompletionAction.ResetToSowing, result.Action);
        }

        [Fact]
        public void ResolveHarvestCompletion_ReturnsGrowingStateForHealthyRegrowth()
        {
            var result = HydroCoreLogic.ResolveHarvestCompletion(1f, 2, 0.8f, 2.8f, 1f, 2f, 2500, 0);

            Assert.Equal(HarvestCompletionAction.ReturnToGrowing, result.Action);
            Assert.Equal(2, result.StoredPlants);
            Assert.Equal(0, result.StoredPlantsBuffer);
            Assert.InRange(result.Growth, 0.39f, 0.41f);
        }

        [Fact]
        public void ShouldConsumeDbhFuel_RequiresRecyclerCoverage()
        {
            Assert.True(HydroCoreLogic.ShouldConsumeDbhFuel(false, false));
            Assert.True(HydroCoreLogic.ShouldConsumeDbhFuel(true, false));
            Assert.False(HydroCoreLogic.ShouldConsumeDbhFuel(true, true));
        }

        [Theory]
        [InlineData(-2, 0f)]
        [InlineData(0, 0f)]
        [InlineData(4, 0.2f)]
        public void CalculateDbhFuelUse_NeverUsesNegativePlantCounts(int storedPlants, float expected)
        {
            Assert.Equal(expected, HydroCoreLogic.CalculateDbhFuelUse(storedPlants), 3);
        }
    }
}
