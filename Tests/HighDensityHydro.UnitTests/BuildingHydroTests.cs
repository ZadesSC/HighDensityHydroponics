using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HighDensityHydro;
using RimWorld;
using UnityEngine;
using Verse;
using Xunit;

namespace HighDensityHydro.UnitTests
{
    public class BuildingHydroTests
    {
        [Fact]
        public void LoadConfig_AppliesModExtensionValues()
        {
            var building = new Building_HighDensityHydro();
            var def = CreateHydroDef();
            SetMember(def, "modExtensions", new List<DefModExtension>
            {
                new HydroStatsExtension
                {
                    capacity = 11,
                    fertility = 3.4f,
                    requiresLightCheck = false,
                    requiresTemperatureCheck = false,
                    requiresAtmosphereCheck = false,
                    powerScalesCapacity = true,
                    basePowerIncrease = 80f,
                    capacityExponent = 1.5f,
                    powerConsumptionWhenSunlampOff = 100f,
                    powerConsumptionWhenSunlampOn = 200f,
                    basePowerIncreaseWhenSunlampOff = 20f,
                    basePowerIncreaseWhenSunlampOn = 40f,
                    capacityExponentWhenSunlampOff = 1.1f,
                    capacityExponentWhenSunlampOn = 1.2f,
                    useThresholdPowerCurve = true,
                    quadraticPowerThreshold = 20,
                    quadraticPowerCoefficient = 3.3f,
                    cubicPowerThreshold = 40,
                    cubicPowerCoefficient = 0.35f,
                    plantsPerLayer = 0,
                    defaultPowerScalingLevel = 5,
                    maxPowerScalingLevel = 12,
                }
            });
            building.def = def;

            InvokeNonPublic(building, "LoadConfig");

            Assert.Equal(11, GetField<int>(building, "_plantCapacityFromDef"));
            Assert.Equal(3.4f, building.Fertility, 3);
            Assert.False(building.RequiresLightCheck);
            Assert.False(building.RequiresTemperatureCheck);
            Assert.False(building.RequiresAtmosphereCheck);
            Assert.True(building.PowerScalesCapacity);
            Assert.Equal(80f, building.BasePowerIncrease, 3);
            Assert.Equal(1.5f, building.CapacityExponent, 3);
            Assert.Equal(1, building.PlantsPerLayer);
            Assert.Equal(5, GetField<int>(building, "_defaultPowerScalingLevel"));
            Assert.Equal(12, GetField<int>(building, "_maxPowerScalingLevel"));
            Assert.Equal(100f, GetField<float>(building, "_powerConsumptionWhenSunlampOff"), 3);
            Assert.Equal(200f, GetField<float>(building, "_powerConsumptionWhenSunlampOn"), 3);
            Assert.Equal(20f, GetField<float>(building, "_basePowerIncreaseWhenSunlampOff"), 3);
            Assert.Equal(40f, GetField<float>(building, "_basePowerIncreaseWhenSunlampOn"), 3);
        }

        [Fact]
        public void CalculateCurrentPlantCapacity_UsesCurrentScalingLevel()
        {
            var building = new Building_HighDensityHydro();
            SetField(building, "_plantCapacityFromDef", 0);
            SetField(building, "_currentPowerScalingLevel", 3);
            SetField(building, "_plantsPerLayer", 4);

            Assert.Equal(12, building.CalculateCurrentPlantCapacity());
        }

        [Fact]
        public void AdjustCapacity_UpdatesCapacityEvenWithoutCachedPowerComp()
        {
            var building = new Building_HighDensityHydro();
            SetField(building, "_plantCapacityFromDef", 0);
            SetField(building, "_currentPowerScalingLevel", 3);
            SetField(building, "_plantsPerLayer", 4);
            SetField(building, "_maxPowerScalingLevel", 100);

            building.AdjustCapacity(2);

            Assert.Equal(5, building.CurrentPowerScalingLevel);
            Assert.Equal(20, building.MaxPlantCapacity);
        }

        [Fact]
        public void RefreshScaledCapacityAndPower_InitializesDefaultScalingLevelWhenRequested()
        {
            var building = new Building_HighDensityHydro();
            SetField(building, "_powerScalesCapacity", true);
            SetField(building, "_plantCapacityFromDef", 0);
            SetField(building, "_plantsPerLayer", 4);
            SetField(building, "_defaultPowerScalingLevel", 20);
            SetField(building, "_maxPowerScalingLevel", 100);

            InvokeNonPublic(building, "RefreshScaledCapacityAndPower", true);

            Assert.Equal(20, building.CurrentPowerScalingLevel);
            Assert.Equal(80, building.MaxPlantCapacity);
        }

        [Fact]
        public void RefreshScaledCapacityAndPower_ClampsQuantumDensityToMinimumOne()
        {
            var building = new Building_HighDensityHydro();
            SetField(building, "_powerScalesCapacity", true);
            SetField(building, "_plantCapacityFromDef", 0);
            SetField(building, "_plantsPerLayer", 4);
            SetField(building, "_currentPowerScalingLevel", 0);
            SetField(building, "_maxPowerScalingLevel", 100);

            InvokeNonPublic(building, "RefreshScaledCapacityAndPower", false);

            Assert.Equal(1, building.CurrentPowerScalingLevel);
            Assert.Equal(4, building.MaxPlantCapacity);
        }

        [Fact]
        public void RemoveInspectLineStartingWith_RemovesMatchingPrefix()
        {
            var input = "Power needed: 2800 W\nStored plants: 4";

            var result = (string)InvokeNonPublicStatic(
                typeof(Building_HighDensityHydro),
                "RemoveInspectLineStartingWith",
                input,
                "Power needed");

            Assert.Equal("Stored plants: 4", result);
        }

        [Fact]
        public void CalculatePowerCost_UsesConfiguredSunlampProfileWhenScalingActive()
        {
            var building = new Building_HighDensityHydro();
            building.def = CreateHydroDef();
            SetField(building, "_powerScalesCapacity", true);
            SetField(building, "_powerConsumptionWhenSunlampOff", 2800f);
            SetField(building, "_powerConsumptionWhenSunlampOn", 3000f);
            SetField(building, "_basePowerIncreaseWhenSunlampOff", 50f);
            SetField(building, "_basePowerIncreaseWhenSunlampOn", 60f);
            SetField(building, "_capacityExponentWhenSunlampOff", 1.2f);
            SetField(building, "_capacityExponentWhenSunlampOn", 1.3f);
            SetField(building, "_powerCompCached", (CompPowerTrader)FormatterServices.GetUninitializedObject(typeof(CompPowerTrader)));

            Assert.Equal(2850f, building.CalculatePowerCost(0), 3);
            Assert.Equal(2886.4f, building.CalculatePowerCost(3), 3);

            SetField(building, "_builtInSunlampEnabled", true);
            Assert.Equal(3060f, building.CalculatePowerCost(0), 3);
        }

        [Fact]
        public void CalculatePowerCost_UsesThresholdCurveWhenEnabled()
        {
            var building = new Building_HighDensityHydro();
            building.def = CreateHydroDef();
            SetField(building, "_powerScalesCapacity", true);
            SetField(building, "_useThresholdPowerCurve", true);
            SetField(building, "_powerConsumptionWhenSunlampOff", 1600f);
            SetField(building, "_powerConsumptionWhenSunlampOn", 1900f);
            SetField(building, "_basePowerIncreaseWhenSunlampOff", 8f);
            SetField(building, "_basePowerIncreaseWhenSunlampOn", 8f);
            SetField(building, "_capacityExponentWhenSunlampOff", 1.03f);
            SetField(building, "_capacityExponentWhenSunlampOn", 1.03f);
            SetField(building, "_quadraticPowerThreshold", 20);
            SetField(building, "_quadraticPowerCoefficient", 3.3f);
            SetField(building, "_cubicPowerThreshold", 40);
            SetField(building, "_cubicPowerCoefficient", 0.35f);
            SetField(building, "_powerCompCached", (CompPowerTrader)FormatterServices.GetUninitializedObject(typeof(CompPowerTrader)));

            Assert.Equal(1614.45f, building.CalculatePowerCost(20), 2);
            Assert.Equal(9727.13f, building.CalculatePowerCost(60), 2);

            SetField(building, "_builtInSunlampEnabled", true);
            Assert.Equal(1914.45f, building.CalculatePowerCost(20), 2);
        }

        [Fact]
        public void CalculatePowerCost_ReturnsFixedConsumptionWhenScalingDisabled()
        {
            var building = new Building_HighDensityHydro();
            building.def = CreateHydroDef();
            SetField(building, "_powerConsumptionWhenSunlampOff", 300f);
            SetField(building, "_powerConsumptionWhenSunlampOn", 425f);
            SetField(building, "_powerCompCached", (CompPowerTrader)FormatterServices.GetUninitializedObject(typeof(CompPowerTrader)));

            Assert.Equal(300f, building.CalculatePowerCost(0), 3);

            SetField(building, "_builtInSunlampEnabled", true);
            Assert.Equal(425f, building.CalculatePowerCost(0), 3);
        }

        [Fact]
        public void CalculateNextPowerCostIncrease_ComparesAdjacentScalingLevels()
        {
            var building = new Building_HighDensityHydro();
            building.def = CreateHydroDef();
            SetField(building, "_powerScalesCapacity", true);
            SetField(building, "_powerConsumptionWhenSunlampOff", 2800f);
            SetField(building, "_basePowerIncreaseWhenSunlampOff", 50f);
            SetField(building, "_capacityExponentWhenSunlampOff", 1.2f);
            SetField(building, "_currentPowerScalingLevel", 2);
            SetField(building, "_powerCompCached", (CompPowerTrader)FormatterServices.GetUninitializedObject(typeof(CompPowerTrader)));

            var expected = building.CalculatePowerCost(3) - building.CalculatePowerCost(2);
            Assert.Equal(expected, building.CalculateNextPowerCostIncrease(), 3);
        }

        [Fact]
        public void RequiresLightCheck_DoesNotChangeWhenBuiltInSunlampIsEnabled()
        {
            var building = new Building_HighDensityHydro();
            SetField(building, "_requiresLightCheck", true);

            Assert.True(building.RequiresLightCheck);

            SetField(building, "_builtInSunlampEnabled", true);
            Assert.True(building.RequiresLightCheck);
        }

        [Fact]
        public void GetWindExposure_UsesPlantTopWindExposure()
        {
            var plant = CreatePlantDef(0.5f);

            Assert.Equal((byte)127, Building_HighDensityHydro.GetWindExposure(plant));
        }

        [Fact]
        public void SetWindExposureColors_SetsExpectedChannels()
        {
            var colors = new Color32[4];
            var plant = CreatePlantDef(0.4f);

            Building_HighDensityHydro.SetWindExposureColors(colors, plant);

            Assert.Equal(0, colors[0].a);
            Assert.Equal(0, colors[3].a);
            Assert.Equal(colors[1].a, colors[2].a);
            Assert.Equal((byte)102, colors[1].a);
        }

        [Fact]
        public void PlantCurrentDyingDamagePerTick_ReturnsZeroWhenNotSpawned()
        {
            var building = new Building_HighDensityHydro();
            SetField(building, "_currentPlantDefToGrow", CreatePlantDef(0f));

            Assert.Equal(0f, building.PlantCurrentDyingDamagePerTick, 3);
        }

        [Fact]
        public void SelectedPlantDef_TracksUnderlyingGrowPlanImmediately()
        {
            var building = new Building_HighDensityHydro();

            var rice = CreatePlantDef(0f);
            var strawberry = CreatePlantDef(0f);
            SetField(building, "_currentPlantDefToGrow", rice);
            SetSelectedPlantDef(building, strawberry);

            Assert.Same(strawberry, building.SelectedPlantDef);
            Assert.Same(rice, building.CurrentPlantedDef);
        }

        [Fact]
        public void CanAcceptSowNow_IgnoresVanillaTemperatureGateWhenDisabled()
        {
            var building = new Building_HighDensityHydro();
            var plant = CreatePlantDef(0f);

            SetField(building, "_bayStage", Enum.Parse(typeof(Building_HighDensityHydro).GetNestedType("BayStage", BindingFlags.NonPublic), "Sowing"));
            SetField(building, "_numStoredPlants", 0);
            SetField(building, "_plantCapacity", 4);
            SetField(building, "_requiresTemperatureCheck", false);
            SetSelectedPlantDef(building, plant);

            Assert.True(((IPlantToGrowSettable)building).CanAcceptSowNow());
        }

        [Fact]
        public void CanAcceptSowNow_DelegatesToVanillaGateWhenTemperatureChecksRemainEnabled()
        {
            var building = new Building_HighDensityHydro();
            var plant = CreatePlantDef(0f);

            SetField(building, "_bayStage", Enum.Parse(typeof(Building_HighDensityHydro).GetNestedType("BayStage", BindingFlags.NonPublic), "Sowing"));
            SetField(building, "_numStoredPlants", 0);
            SetField(building, "_plantCapacity", 4);
            SetField(building, "_requiresTemperatureCheck", true);
            SetSelectedPlantDef(building, plant);

            Assert.Equal(((Building_PlantGrower)building).CanAcceptSowNow(), ((IPlantToGrowSettable)building).CanAcceptSowNow());
        }

        [Fact]
        public void SetPlantDefToGrow_ThroughInterface_ResetsSowingState()
        {
            var building = new Building_HighDensityHydro();
            var rice = CreatePlantDef(0f);

            SetField(building, "_bayStage", Enum.Parse(typeof(Building_HighDensityHydro).GetNestedType("BayStage", BindingFlags.NonPublic), "Sowing"));
            SetField(building, "_numStoredPlants", 3);
            SetField(building, "_numStoredPlantsBuffer", 2);
            SetField(building, "_plantAge", 6000);
            SetField(building, "_curGrowth", 0.25f);
            SetField(building, "_averageHarvestGrowth", 0.4f);
            SetField(building, "_currentPlantDefToGrow", rice);
            SetSelectedPlantDef(building, rice);

            ((IPlantToGrowSettable)building).SetPlantDefToGrow(null);

            Assert.Equal(0, building.StoredPlantCount);
            Assert.Equal(0, GetField<int>(building, "_numStoredPlantsBuffer"));
            Assert.Equal(0, building.PlantAge);
            Assert.Equal(0f, building.PlantGrowth, 3);
            Assert.Equal(0f, GetField<float>(building, "_averageHarvestGrowth"), 3);
            Assert.Equal("Sowing", GetField<object>(building, "_bayStage").ToString());
            Assert.Null(building.CurrentPlantedDef);
        }

        [Fact]
        public void ForceHarvestReadyForDev_SetsGrowthStoredPlantsAndHarvestStage()
        {
            var building = new Building_HighDensityHydro();
            var plant = CreatePlantDef(0f);

            SetField(building, "_plantCapacity", 4);
            SetField(building, "_unlitTicks", 12345);
            SetSelectedPlantDef(building, plant);

            InvokeNonPublic(building, "ForceHarvestReadyForDev");

            Assert.Equal(1f, building.PlantGrowth, 3);
            Assert.Equal(4, building.StoredPlantCount);
            Assert.Equal(12345, GetField<int>(building, "_unlitTicks"));
            Assert.Equal("Harvest", GetField<object>(building, "_bayStage").ToString());
            Assert.Same(plant, building.CurrentPlantedDef);
        }

        [Fact]
        public void KillAllPlantsAndReset_ClearsStoredState()
        {
            var building = new Building_HighDensityHydro();

            SetField(building, "_numStoredPlants", 3);
            SetField(building, "_numStoredPlantsBuffer", 2);
            SetField(building, "_plantAge", 6000);
            SetField(building, "_curGrowth", 0.75f);
            SetField(building, "_averageHarvestGrowth", 0.4f);
            SetField(building, "_unlitTicks", 4000);
            SetField(building, "_bayStage", Enum.Parse(typeof(Building_HighDensityHydro).GetNestedType("BayStage", BindingFlags.NonPublic), "Harvest"));

            InvokeNonPublic(building, "KillAllPlantsAndReset");

            Assert.Equal(0, building.StoredPlantCount);
            Assert.Equal(0, GetField<int>(building, "_numStoredPlantsBuffer"));
            Assert.Equal(0, building.PlantAge);
            Assert.Equal(0f, building.PlantGrowth, 3);
            Assert.Equal(0f, GetField<float>(building, "_averageHarvestGrowth"), 3);
            Assert.Equal(0, GetField<int>(building, "_unlitTicks"));
            Assert.Equal("Sowing", GetField<object>(building, "_bayStage").ToString());
            Assert.Null(building.CurrentPlantedDef);
        }

        [Theory]
        [InlineData("Sowing")]
        [InlineData("Harvest")]
        public void TickLong_DoesNotRunInternalLifecycleWithoutStoredPlantsInNonGrowingStages(string stageName)
        {
            var building = new Building_HighDensityHydro();
            var plant = CreatePlantDef(0f);

            SetField(building, "_bayStage", Enum.Parse(typeof(Building_HighDensityHydro).GetNestedType("BayStage", BindingFlags.NonPublic), stageName));
            SetField(building, "_numStoredPlants", 0);
            SetField(building, "_plantAge", 6000);
            SetField(building, "_unlitTicks", 4000);
            SetField(building, "_plantHealth", 55f);
            SetField(building, "_currentPlantDefToGrow", plant);

            building.TickLong();

            Assert.Equal(6000, building.PlantAge);
            Assert.Equal(4000, GetField<int>(building, "_unlitTicks"));
            Assert.Equal(55f, building.PlantHealth, 3);
            Assert.Equal(stageName, GetField<object>(building, "_bayStage").ToString());
        }

        private static ThingDef CreateHydroDef()
        {
            var power = new CompProperties_Power
            {
                compClass = typeof(CompPowerTrader)
            };
            typeof(CompProperties_Power)
                .GetField("basePowerConsumption", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(power, 2800f);

            var def = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
            SetMember(def, "size", new IntVec2(2, 2));
            SetMember(def, "comps", new List<CompProperties> { power });
            SetMember(def, "modExtensions", new List<DefModExtension>());
            return def;
        }

        private static ThingDef CreatePlantDef(float topWindExposure)
        {
            var def = (ThingDef)FormatterServices.GetUninitializedObject(typeof(ThingDef));
            SetMember(def, "plant", new PlantProperties
            {
                topWindExposure = topWindExposure
            });
            return def;
        }

        private static void SetSelectedPlantDef(Building_HighDensityHydro building, ThingDef plantDef)
        {
            var type = typeof(Building_HighDensityHydro).BaseType;
            while (type != null)
            {
                var field = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(candidate =>
                        candidate.FieldType == typeof(ThingDef) &&
                        candidate.Name.IndexOf("plant", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        candidate.Name.IndexOf("grow", StringComparison.OrdinalIgnoreCase) >= 0);
                if (field != null)
                {
                    field.SetValue(building, plantDef);
                    return;
                }

                type = type.BaseType;
            }

            throw new MissingFieldException("Could not find the grow-plan ThingDef field on the plant grower base type.");
        }

        private static void InvokeNonPublic(object instance, string methodName)
        {
            instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, null);
        }

        private static void InvokeNonPublic(object instance, string methodName, params object[] parameters)
        {
            instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instance, parameters);
        }

        private static object InvokeNonPublicStatic(Type type, string methodName, params object[] parameters)
        {
            return type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, parameters);
        }

        private static T GetField<T>(object instance, string fieldName)
        {
            return (T)instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        }

        private static void SetField(object instance, string fieldName, object value)
        {
            instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
        }

        private static void SetMember(object instance, string memberName, object value)
        {
            var type = instance.GetType();
            while (type != null)
            {
                var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(instance, value);
                    return;
                }

                var property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    property.SetValue(instance, value, null);
                    return;
                }

                type = type.BaseType;
            }

            throw new MissingMemberException(instance.GetType().FullName, memberName);
        }
    }
}
