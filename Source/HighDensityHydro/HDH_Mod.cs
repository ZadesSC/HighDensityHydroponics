using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace HighDensityHydro
{
    public class HDH_Settings : ModSettings
    {
        public bool lightRequirement;
        public bool killPlantsOnNoPower;
        
        public override void ExposeData()
        {
            Scribe_Values.Look(ref lightRequirement, "lightRequirement", true);
            Scribe_Values.Look(ref killPlantsOnNoPower, "killPlantsOnNoPower", true);
            base.ExposeData();
        }
    }
    public class HDH_Mod : Mod
    {
        public static HDH_Settings settings;

        public HDH_Mod(ModContentPack content) : base(content)
        {
            settings = GetSettings<HDH_Settings>();
        }

        public override string SettingsCategory()
        {
            return "High Density Hydroponics";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            listingStandard.CheckboxLabeled("HDH_LightRequirementLabel".Translate(), ref settings.lightRequirement, "HDH_LightRequirementTooltip".Translate());
            listingStandard.CheckboxLabeled("HDH_KillOnPowerLossLabel".Translate(), ref settings.killPlantsOnNoPower, "HDH_KillOnPowerLossTooltip".Translate());
            
            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }
    }
}