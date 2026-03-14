using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace HighDensityHydro
{
    [ExcludeFromCodeCoverage]
    public class HDH_Settings : ModSettings
    {
        public bool defaultBuiltInSunlampEnabled;
        public bool allowOtherVanillaPlants;
        private bool _hasDefaultBuiltInSunlampSetting;
        
        public override void ExposeData()
        {
            var defaultBuiltInSunlampEnabled = this.defaultBuiltInSunlampEnabled;
            var hasDefaultBuiltInSunlampSetting = _hasDefaultBuiltInSunlampSetting;
            var legacyLightRequirement = true;

            Scribe_Values.Look(ref defaultBuiltInSunlampEnabled, "defaultBuiltInSunlampEnabled", false);
            Scribe_Values.Look(ref hasDefaultBuiltInSunlampSetting, "hasDefaultBuiltInSunlampSetting", false);
            Scribe_Values.Look(ref legacyLightRequirement, "lightRequirement", true);
            Scribe_Values.Look(ref allowOtherVanillaPlants, "allowOtherVanillaPlants", true);

            this.defaultBuiltInSunlampEnabled = hasDefaultBuiltInSunlampSetting
                ? defaultBuiltInSunlampEnabled
                : !legacyLightRequirement;
            _hasDefaultBuiltInSunlampSetting = true;
            base.ExposeData();
        }
    }
    [ExcludeFromCodeCoverage]
    public class HDH_Mod : Mod
    {
        public static HDH_Settings settings;

        public HDH_Mod(ModContentPack content) : base(content)
        {
            settings = GetSettings<HDH_Settings>();
            
            var harmony = new Harmony("MapleApple.HighDensityHydroponics.Fixed.ZouHb.zades");
            harmony.PatchAll();
        }

        public override string SettingsCategory()
        {
            return "HDH_SettingsCategory".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listingStandard = new Listing_Standard();
            listingStandard.Begin(inRect);
            listingStandard.CheckboxLabeled("HDH_DefaultBuiltInSunlampLabel".Translate(), ref settings.defaultBuiltInSunlampEnabled, "HDH_DefaultBuiltInSunlampTooltip".Translate());
            //listingStandard.CheckboxLabeled("HDH_AllowOtherVanillaPlantsLabel".Translate(), ref settings.allowOtherVanillaPlants, "HDH_AllowOtherVanillaPlantsTooltip".Translate());
            
            listingStandard.End();
            base.DoSettingsWindowContents(inRect);
        }
    }
}
