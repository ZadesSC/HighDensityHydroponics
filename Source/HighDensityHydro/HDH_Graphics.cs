using System;
using UnityEngine;
using Verse;

namespace HighDensityHydro
{
    [StaticConstructorOnStartup]
    public static class HDH_Graphics
    {
        public static readonly Vector2 BarSize = new Vector2(3.6f, 0.6f);

        public static readonly Material HDHBarFilledMat = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.2f, 0.85f, 0.2f), false);

        public static readonly Material HDHBarUnfilledMat = SolidColorMaterials.SimpleSolidColorMaterial(new Color(0.3f, 0.3f, 0.3f), false);
    }
}