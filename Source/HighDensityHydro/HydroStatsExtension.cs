using System;
using Verse;

namespace HighDensityHydro
{
    internal class HydroStatsExtension : DefModExtension
    {
        public static readonly HydroStatsExtension defaultValues = new HydroStatsExtension();

        public float fertility = 2.8f;
        public int capacity = 4;
        public bool requiresLightCheck = true;
        public bool requiresTemperatureCheck = true;
        public bool requiresAtmosphereCheck = true;
        public bool powerScalesCapacity = false;
        public float basePowerIncrease = 50f;
        public float capacityExponent = 1.2f;
        public float powerConsumptionWhenSunlampOff = -1f;
        public float powerConsumptionWhenSunlampOn = -1f;
        public float basePowerIncreaseWhenSunlampOff = -1f;
        public float basePowerIncreaseWhenSunlampOn = -1f;
        public float capacityExponentWhenSunlampOff = -1f;
        public float capacityExponentWhenSunlampOn = -1f;
        public int plantsPerLayer = 4;
        public int defaultPowerScalingLevel = 0;
        public int maxPowerScalingLevel = 100;
    }
}
