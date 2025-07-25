using System;
using Verse;

namespace HighDensityHydro
{
    internal class HydroStatsExtension : DefModExtension
    {
        public static readonly HydroStatsExtension defaultValues = new HydroStatsExtension();

        public float fertility = 2.8f;

        public int capacity = 52;
    }
}