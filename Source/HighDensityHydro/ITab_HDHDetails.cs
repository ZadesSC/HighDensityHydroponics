using HighDensityHydro;
using RimWorld;
using UnityEngine;
using Verse;

namespace HighDensityHydro
{
    public class ITab_HDHDetails : ITab
    {
        public ITab_HDHDetails()
        {
            this.size = new Vector2(400f, 400f);
            this.labelKey = "Hydroponics";
        }

        protected override void FillTab()
        {
            // Draw and cleanup
            Rect mainRect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            Text.Anchor = TextAnchor.UpperLeft;
            
            // Get all the values 
            Building_HighDensityHydro building = SelThing as Building_HighDensityHydro;
            if (building == null)
            {
                Widgets.Label(mainRect, "Not a valid HDH building.");
                return;
            }

            if (!building.PowerScalesCapacity)
            {
                this.size.y = 250;
            }
            else
            {
                this.size.y = 400f;
            }
            
            var power = building.GetComp<CompPowerTrader>();
            if (power == null)
            {
                return;
            }

            ThingDef currentPlant = building.CurrentPlantedDef; // Replace with your accessor
            int storedPlants = building.StoredPlantCount;
            int maxCapacity = building.MaxPlantCapacity;

            float lightLevel = 1f;
            if (building.RequiresLightCheck)
            {
                lightLevel = building.LastAverageGlow; // approximate
            }

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
            
            //bool growingSeason = GenTemperature.SeasonAndOutdoorTempAcceptableForGrower(building, building.Map);
            
            float currentPower = -power.PowerOutput; // positive W

            float lineHeight = 24f;
            
            // ------------------- TOP: Plant Label --------------------
            Rect plantLabelBox = new Rect(mainRect.x, mainRect.y, mainRect.width, lineHeight);
            string plantLabel = currentPlant != null ? (string)currentPlant.LabelCap : "None";
            Widgets.Label(plantLabelBox.TopPartPixels(lineHeight), $"Currently Growing: {plantLabel}");
            Widgets.DrawLineHorizontal(x: mainRect.x, y: mainRect.y + lineHeight, mainRect.width, Color.white);

            // ------------------- TOP LEFT: Plant icon --------------------
            Rect plantBox = new Rect(mainRect.x, mainRect.y + lineHeight + 5f, 120f, 100f);
            //Widgets.Label(plantBox.TopPartPixels(lineHeight * 2f), $"Currently Growing: {currentPlant.LabelCap}");
            if (currentPlant?.uiIcon != null)
                Widgets.DefIcon(plantBox.BottomPartPixels(80f), currentPlant);

            // ------------------- TOP RIGHT: Info --------------------
            Rect infoBox = new Rect(mainRect.x + 120f, mainRect.y + lineHeight + 5f, mainRect.width - 130f, lineHeight * 7);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(infoBox);
            listing.Label($"Stored Plants: {storedPlants} / {maxCapacity}");
            listing.Label($"Plant Health: {building.PlantHealth} / {building.CurrentPlantedDef.BaseMaxHitPoints}");
            listing.Label($"Growth: {building.PlantGrowth:P0}");
            listing.Label($"Fertility: {building.Fertility:P0}");
            if (lightLevel < 0)
            {
                listing.Label($"Light: N/A");
            }
            else
            {
                listing.Label($"Light: {lightLevel:P0}");
            }
            
            if (!building.RequiresTemperatureCheck || Mathf.Approximately(temperature, -500f))
            {
                listing.Label($"Temp: N/A");
            }
            else
            {
                listing.Label($"Temp: {temperature:F1}°C");
            }
            
            if (!building.RequiresAtmosphereCheck)
            {
                listing.Label($"Vacuum: N/A");
            }
            else
            {
                listing.Label($"Vacuum: {vacuum:P0}");
            }
            
            //listing.Label($"Growing Season: N/A");
            listing.End();

            // ------------------- BOTTOM: Power usage + controls --------------------
            
            Widgets.DrawLineHorizontal(x: mainRect.x, y: infoBox.y + lineHeight * 7, mainRect.width, Color.white);
            
            Rect powerBox = new Rect(mainRect.x, infoBox.y + lineHeight * 7 + 5, mainRect.width, lineHeight);
            Widgets.Label(powerBox, $"Current Power Usage: {currentPower:F0}W");
            
            
            // power and density scaling
            if (!building.PowerScalesCapacity)
            {
                return;
            }
            Rect powerDescBox = new Rect(powerBox.x, powerBox.y + lineHeight + 5f, powerBox.width, lineHeight * 2);
            Widgets.Label(powerDescBox, $"Increase or decrease density of this hydroponics.\nEach increase increases capacity by {building.PlantsPerLayer}.");

            Rect controlBox = new Rect(mainRect.x, powerBox.y + (lineHeight * 3) + 5f, mainRect.width, lineHeight);
            float btnWidth = 40f;

            Rect left5 = controlBox.LeftPartPixels(btnWidth);
            if (Widgets.ButtonText(left5, "-5"))
                building.AdjustCapacity(-5);

            Rect left1 = new Rect(left5.xMax + 5f, left5.y, btnWidth, left5.height);
            if (Widgets.ButtonText(left1, "-1"))
                building.AdjustCapacity(-1);

            Rect right5 = controlBox.RightPartPixels(btnWidth);
            if (Widgets.ButtonText(right5, "+5"))
                building.AdjustCapacity(+5);

            Rect right1 = new Rect(right5.x - btnWidth - 5f, right5.y, btnWidth, right5.height);
            if (Widgets.ButtonText(right1, "+1"))
                building.AdjustCapacity(+1);
            
            Rect powerCostBox = new Rect(controlBox.x + controlBox.width / 2f - 80f, controlBox.y, 160f, lineHeight * 3);
            Widgets.Label(powerCostBox, $"Next increase increases power consumption by {building.CalculateNextPowerCostIncrease():F0}W");
            
            Rect scalingLevelBox = new Rect(controlBox.x, controlBox.y + lineHeight * 3, controlBox.width, lineHeight * 2);
            Widgets.Label(scalingLevelBox, $"Current density level: {building.CurrentPowerScalingLevel:F0}");
        }

        public override bool IsVisible => SelThing is Building_HighDensityHydro;
    }
}