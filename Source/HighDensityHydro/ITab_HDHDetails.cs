using System.Diagnostics.CodeAnalysis;
using RimWorld;
using UnityEngine;
using Verse;

namespace HighDensityHydro
{
    [ExcludeFromCodeCoverage]
    public class ITab_HDHDetails : ITab
    {
        public ITab_HDHDetails()
        {
            size = new Vector2(400f, 400f);
            labelKey = "HDH_ITabLabel";
        }

        protected override void FillTab()
        {
            Rect mainRect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            Text.Anchor = TextAnchor.UpperLeft;

            Building_HighDensityHydro building = SelThing as Building_HighDensityHydro;
            if (building == null)
            {
                Widgets.Label(mainRect, "HDH_ITabInvalidBuilding".Translate());
                return;
            }

            size.y = building.PowerScalesCapacity ? 400f : 250f;

            var power = building.GetComp<CompPowerTrader>();
            if (power == null)
            {
                return;
            }

            ThingDef selectedPlant = building.SelectedPlantDef;
            ThingDef currentPlant = building.CurrentPlantedDef;
            int storedPlants = building.StoredPlantCount;
            int maxCapacity = building.MaxPlantCapacity;
            string noneLabel = "HDH_None".Translate().ToString();

            float lightLevel = building.LastAverageGlow;

            float temperature = -500f;
            if (building.RequiresTemperatureCheck)
            {
                temperature = building.Position.GetTemperature(building.Map);
            }

            float vacuum = 0f;
            if (building.RequiresAtmosphereCheck)
            {
                vacuum = building.Position.GetVacuum(building.Map);
            }

            float currentPower = -power.PowerOutput;
            float lineHeight = 24f;

            Rect plantLabelBox = new Rect(mainRect.x, mainRect.y, mainRect.width, lineHeight);
            string plantLabel = selectedPlant != null ? selectedPlant.LabelCap : noneLabel;
            Widgets.Label(plantLabelBox.TopPartPixels(lineHeight), "HDH_ITabSelectedPlant".Translate(plantLabel));
            Widgets.DrawLineHorizontal(mainRect.x, mainRect.y + lineHeight, mainRect.width, Color.white);

            Rect plantBox = new Rect(mainRect.x, mainRect.y + lineHeight + 5f, 120f, 100f);
            if (selectedPlant?.uiIcon != null)
            {
                Widgets.DefIcon(plantBox.BottomPartPixels(80f), selectedPlant);
            }

            int infoLineCount = building.RequiresLightCheck ? 7 : 6;
            Rect infoBox = new Rect(mainRect.x + 120f, mainRect.y + lineHeight + 5f, mainRect.width - 130f, lineHeight * infoLineCount);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(infoBox);
            listing.Label("HDH_ITabCurrentBatch".Translate(currentPlant != null ? currentPlant.LabelCap : noneLabel));

            if (selectedPlant != null && selectedPlant != currentPlant)
            {
                listing.Label("HDH_ITabNextSowingPlant".Translate(selectedPlant.LabelCap));
            }

            listing.Label("HDH_ITabStoredPlants".Translate(storedPlants, maxCapacity));

            if (currentPlant == null)
            {
                listing.Label("HDH_ITabPlantHealthNA".Translate());
            }
            else
            {
                listing.Label("HDH_ITabPlantHealth".Translate(building.PlantHealth.ToString("F0"), currentPlant.BaseMaxHitPoints));
            }

            listing.Label("HDH_ITabGrowth".Translate(building.PlantGrowth.ToString("P0")));
            listing.Label("HDH_ITabFertility".Translate(building.Fertility.ToString("P0")));

            if (building.RequiresLightCheck)
            {
                if (lightLevel < 0f)
                {
                    listing.Label("HDH_ITabLightNA".Translate());
                }
                else
                {
                    listing.Label("HDH_ITabLight".Translate(lightLevel.ToString("P0")));
                }
            }

            if (!building.RequiresTemperatureCheck || Mathf.Approximately(temperature, -500f))
            {
                listing.Label("HDH_ITabTemperatureNA".Translate());
            }
            else
            {
                listing.Label("HDH_ITabTemperature".Translate(temperature.ToString("F1")));
            }

            if (!building.RequiresAtmosphereCheck)
            {
                listing.Label("HDH_ITabVacuumNA".Translate());
            }
            else
            {
                listing.Label("HDH_ITabVacuum".Translate(vacuum.ToString("P0")));
            }

            listing.End();

            Widgets.DrawLineHorizontal(mainRect.x, infoBox.y + lineHeight * infoLineCount, mainRect.width, Color.white);

            Rect powerBox = new Rect(mainRect.x, infoBox.y + lineHeight * infoLineCount + 5f, mainRect.width, lineHeight);
            Widgets.Label(powerBox, "HDH_ITabCurrentPowerUsage".Translate(currentPower.ToString("F0")));

            if (!building.PowerScalesCapacity)
            {
                return;
            }

            Rect powerDescBox = new Rect(powerBox.x, powerBox.y + lineHeight + 5f, powerBox.width, lineHeight * 2);
            Widgets.Label(powerDescBox, "HDH_ITabPowerScalingDescription".Translate(building.PlantsPerLayer));

            Rect controlBox = new Rect(mainRect.x, powerBox.y + (lineHeight * 3) + 5f, mainRect.width, lineHeight);
            float btnWidth = 40f;

            Rect left5 = controlBox.LeftPartPixels(btnWidth);
            if (Widgets.ButtonText(left5, "-5"))
            {
                building.AdjustCapacity(-5);
            }

            Rect left1 = new Rect(left5.xMax + 5f, left5.y, btnWidth, left5.height);
            if (Widgets.ButtonText(left1, "-1"))
            {
                building.AdjustCapacity(-1);
            }

            Rect right5 = controlBox.RightPartPixels(btnWidth);
            if (Widgets.ButtonText(right5, "+5"))
            {
                building.AdjustCapacity(5);
            }

            Rect right1 = new Rect(right5.x - btnWidth - 5f, right5.y, btnWidth, right5.height);
            if (Widgets.ButtonText(right1, "+1"))
            {
                building.AdjustCapacity(1);
            }

            Rect powerCostBox = new Rect(controlBox.x + controlBox.width / 2f - 80f, controlBox.y, 160f, lineHeight * 3);
            Widgets.Label(powerCostBox, "HDH_ITabNextPowerIncrease".Translate(building.CalculateNextPowerCostIncrease().ToString("F0")));

            Rect scalingLevelBox = new Rect(controlBox.x, controlBox.y + lineHeight * 3f, controlBox.width, lineHeight * 2f);
            Widgets.Label(scalingLevelBox, "HDH_ITabCurrentDensityLevel".Translate(building.CurrentPowerScalingLevel));
        }

        public override bool IsVisible => SelThing is Building_HighDensityHydro;
    }
}
