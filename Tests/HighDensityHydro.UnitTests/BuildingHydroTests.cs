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
                    plantsPerLayer = 0,
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
            Assert.Equal(12, GetField<int>(building, "_maxPowerScalingLevel"));
        }

        [Fact]
        public void CalculateCurrentPlantCapacity_UsesCurrentScalingLevel()
        {
            var building = new Building_HighDensityHydro();
            SetField(building, "_plantCapacityFromDef", 4);
            SetField(building, "_currentPowerScalingLevel", 3);
            SetField(building, "_plantsPerLayer", 4);

            Assert.Equal(16, building.CalculateCurrentPlantCapacity());
        }

        [Fact]
        public void CalculatePowerCost_UsesDefBaseConsumptionWhenPowerCompIsCached()
        {
            var building = new Building_HighDensityHydro();
            building.def = CreateHydroDef();
            SetField(building, "_basePowerIncrease", 50f);
            SetField(building, "_capacityExponent", 1.2f);
            SetField(building, "_powerCompCached", (CompPowerTrader)FormatterServices.GetUninitializedObject(typeof(CompPowerTrader)));

            Assert.Equal(2850f, building.CalculatePowerCost(0), 3);
            Assert.Equal(2886.4f, building.CalculatePowerCost(3), 3);
        }

        [Fact]
        public void CalculateNextPowerCostIncrease_ComparesAdjacentScalingLevels()
        {
            var building = new Building_HighDensityHydro();
            building.def = CreateHydroDef();
            SetField(building, "_basePowerIncrease", 50f);
            SetField(building, "_capacityExponent", 1.2f);
            SetField(building, "_currentPowerScalingLevel", 2);
            SetField(building, "_powerCompCached", (CompPowerTrader)FormatterServices.GetUninitializedObject(typeof(CompPowerTrader)));

            var expected = building.CalculatePowerCost(3) - building.CalculatePowerCost(2);
            Assert.Equal(expected, building.CalculateNextPowerCostIncrease(), 3);
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

            Assert.True(building.CanAcceptSowNow());
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

            Assert.Equal(((Building_PlantGrower)building).CanAcceptSowNow(), building.CanAcceptSowNow());
        }

        [Fact]
        public void ForceHarvestReadyForDev_SetsGrowthStoredPlantsAndHarvestStage()
        {
            var building = new Building_HighDensityHydro();
            var plant = CreatePlantDef(0f);

            SetField(building, "_plantCapacity", 4);
            SetSelectedPlantDef(building, plant);

            InvokeNonPublic(building, "ForceHarvestReadyForDev");

            Assert.Equal(1f, building.PlantGrowth, 3);
            Assert.Equal(4, building.StoredPlantCount);
            Assert.Equal("Harvest", GetField<object>(building, "_bayStage").ToString());
            Assert.Same(plant, building.CurrentPlantedDef);
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

            throw new MissingMemberException(type.FullName, memberName);
        }
    }
}
