using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace HighDensityHydro
{
	public class Building_HighDensityHydro : Building_PlantGrower, IPlantToGrowSettable
	{
		// for graphics drawing
		private static readonly Dictionary<ThingDef, Graphic> plantGraphicCache = new Dictionary<ThingDef, Graphic>();
		private Dictionary<IntVec3, Matrix4x4> drawMatrixCache = new Dictionary<IntVec3, Matrix4x4>();
		
		// Token: 0x17000001 RID: 1
		// (get) Token: 0x06000001 RID: 1
		IEnumerable<IntVec3> IPlantToGrowSettable.Cells
		{
			get
			{
				return this.OccupiedRect().Cells;
			}
		}

		// Token: 0x06000002 RID: 2
		public override void TickRare()
		{
			//Log.Message($"[HDH] Tick: storedPlants={storedPlants}, growth={growth}, stage={bayStage}");
			
			bool poweredNow = this.PowerComp == null || this.PowerComp.PowerOn;
			if (HDH_Mod.settings.killPlantsOnNoPower)
			{
				if (this.wasPoweredLastTick && !poweredNow)
				{
					this.KillAllPlantsAndReset();
				}
			}
			
			this.wasPoweredLastTick = poweredNow;
			switch (this.bayStage)
			{
			case Building_HighDensityHydro.BayStage.Sowing:
				this.HandleSowing();
				return;
			case Building_HighDensityHydro.BayStage.Growing:
				this.HandleGrowing();
				return;
			case Building_HighDensityHydro.BayStage.Harvest:
				this.HandleHarvest();
				return;
			default:
				return;
			}
		}

		// Token: 0x06000004 RID: 4
		public override string GetInspectString()
		{
			string text = base.GetInspectString();

			text += "\n" + "HDH_NumStoredPlants".Translate(this.storedPlants);

			if (this.storedPlants > 0)
			{
				// "{PlantLabel} | {GrowthPercent}%"
				text += "\n" + currentPlantDefToGrow.LabelCap + ": " + string.Format("{0:#0}%", this.growth * 100f);

				if (HDH_Mod.settings.lightRequirement && this.avgGlow >= 0f)
				{
					// "Average Light: {0}%"
					text += "\n" + "HDH_AverageLight".Translate(string.Format("{0:0}%", this.avgGlow * 100f));
				}
			}

			return text;
		}

		// Token: 0x06000007 RID: 7
		public Building_HighDensityHydro()
		{
		}

		// Token: 0x06000009 RID: 9
		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			this.LoadConfig();
			int x = this.def.size.x;
			float y = (x == 1) ? 0.6f : 0.1f;
			y = 0.1f;
			this.margin = ((x == 1) ? 0.15f : 0.08f);
			this.margin = 0.05f;
			this.barsize = new Vector2((float)this.def.size.z - 0.4f, y);

			drawMatrixCache.Clear();
			
			//TODO: maybe move this somewhere else, should only be called and generated once
			PlantPosIndices();
		}

		// Token: 0x0600000E RID: 14
		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look<Building_HighDensityHydro.BayStage>(ref this.bayStage, "bayStage", Building_HighDensityHydro.BayStage.Sowing, false);
			Scribe_Values.Look<int>(ref this.storedPlants, "storedPlants", 0, false);
			Scribe_Values.Look<float>(ref this.growth, "growth", 0f, false);
			Scribe_Defs.Look(ref currentPlantDefToGrow, "queuedPlantDefToGrow");
			
			// if (Scribe.mode == LoadSaveMode.LoadingVars)
			// {
			// 	Log.Warning($"[HDH] Loading hydro: storedPlants={storedPlants}, growth={growth}, stage={bayStage}");
			// }
		}

		// Token: 0x06000012 RID: 18
		public new void SetPlantDefToGrow(ThingDef plantDef)
		{
			base.SetPlantDefToGrow(plantDef);
		}

		// Token: 0x060000DE RID: 222
		public new bool CanAcceptSowNow()
		{
			return base.CanAcceptSowNow() && this.bayStage == Building_HighDensityHydro.BayStage.Sowing && this.storedPlants < this.capacity;
		}
		
		// Token: 0x060000ED RID: 237
		protected override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
		    base.DrawAt(drawLoc, flip);
			
		    //Draw growth bar during Growing stage
		    if (this.storedPlants > 0 && this.bayStage == BayStage.Growing)
		    {
			    Vector3 center;
			    Vector2 offset;
			    int x = this.def.size.x;
			    
			    //special position for the 1x4 hydroponics
			    //height (y) of +0.01f above build, +0.02f are the plants 
			    if (x == 1)
			    {
				    offset = Vector2.down * 0.3f;
				    //center = drawLoc + Vector3.up * 0.01f + Vector3.forward * 0.3f;
				    center = drawLoc + Vector3.up * 0.01f;
			    }
			    else
			    {
				    offset = Vector2.zero;
				    center = drawLoc + Vector3.up * 0.01f;
			    }
		        GenDraw.FillableBarRequest barReq = new GenDraw.FillableBarRequest
		        {
			        preRotationOffset = offset,
		            center = center,
		            size = this.barsize,
		            fillPercent = this.growth,
		            filledMat = HDH_Graphics.HDHBarFilledMat,
		            unfilledMat = HDH_Graphics.HDHBarUnfilledMat,
		            margin = this.margin,
		            rotation = this.Rotation.Rotated(RotationDirection.Clockwise)
		        };
		        GenDraw.DrawFillableBar(barReq);
		    }
		    
		    
		    //
		    // if (this.currentPlantDefToGrow == null || this.storedPlants <= 0)
		    //     return;
		    //
		    // // Cache the graphic
		    // Graphic graphic;
		    // if (!plantGraphicCache.TryGetValue(this.currentPlantDefToGrow, out graphic))
		    // {
		    //     graphic = this.currentPlantDefToGrow.graphicData.Graphic;
		    //     plantGraphicCache[this.currentPlantDefToGrow] = graphic;
		    // }
		    //
		    // // Stable non-wind material
		    // Material mat = new Material(graphic.MatSingle);
		    // mat.shader = ShaderDatabase.Cutout;
		    //
		    // // Use in-game ticks
		    // int ticks = Find.TickManager.TicksGame;
		    // float swayAmplitude = 0.005f;
		    // float swaySpeed = 0.1f; // radians per tick (adjust to taste)
		    // //float scale = Mathf.Lerp(0.2f, 1.0f, this.growth);
		    //
		    // List<IntVec3> cells = this.OccupiedRect().Cells.ToList();
		    // int cellCount = cells.Count;
		    //
		    // int visibleCount = 0;
		    // if (this.bayStage == BayStage.Growing)
		    // {
			   //  visibleCount = cellCount;
		    // }
		    // else if (this.bayStage == BayStage.Sowing && this.storedPlants > 0)
		    // {
			   //  visibleCount = Mathf.Clamp(Mathf.FloorToInt((this.storedPlants / (float)this.capacity) * cellCount), 1, cellCount);
		    // }
		    // else if (this.bayStage == BayStage.Harvest)
		    // {
			   //  visibleCount = 0;
		    // }
		    //
		    // for (int i = 0; i < visibleCount; i++)
		    // {
			   //  IntVec3 cell = cells[i];
		    //
			   //  float phaseOffset = (cell.x * 17 + cell.z * 31) % 100 / 100f;
			   //  float swayOffset = Mathf.Sin(ticks * swaySpeed + phaseOffset * Mathf.PI * 2f) * swayAmplitude;
		    //
			   //  Vector3 cellDrawPos = cell.ToVector3ShiftedWithAltitude(AltitudeLayer.BuildingOnTop);
			   //  cellDrawPos.x += swayOffset;
		    //
			   //  float scale = Mathf.Lerp(0.2f, 1.0f, this.growth);
		    //
			   //  Matrix4x4 matrix = Matrix4x4.TRS(
				  //   cellDrawPos,
				  //   Quaternion.identity,
				  //   new Vector3(scale, 1f, scale)
			   //  );
		    //
			   //  Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
		    // }
		}
		
		//draw plants stuff
		private static readonly Color32[] workingColors = new Color32[4]; // mimic Plant.workingColors

		//draw plants stuff
		//TODO: clean up this code
		public override void Print(SectionLayer layer)
		{
			base.Print(layer);
			
			if (this.currentPlantDefToGrow == null || this.storedPlants <= 0)
				return;
			
			List<IntVec3> cells = this.OccupiedRect().Cells.ToList();
			int cellCount = cells.Count;
			int visibleCount = 0;
			if (bayStage == BayStage.Growing)
			{
				visibleCount = cellCount;
			}
			else if (this.bayStage == BayStage.Sowing && this.storedPlants > 0)
			{
				visibleCount = Mathf.Clamp(Mathf.FloorToInt((this.storedPlants / (float)this.capacity) * cellCount), 1, cellCount);
			}
			else if (this.bayStage == BayStage.Harvest)
			{
				visibleCount = 0;
			}
			
			Graphic graphic = currentPlantDefToGrow.graphicData.Graphic;
			if (growth <= currentPlantDefToGrow.plant.harvestMinGrowth && currentPlantDefToGrow.plant.immatureGraphic != null)
			{
				graphic = currentPlantDefToGrow.plant.immatureGraphic;
			}

			Material baseMat = graphic.MatSingleFor(this);
			//Material mat = graphic.MatAt(Rot4.North); ;

			for (int i = 0; i < visibleCount; i++)
			{
				Vector3 center = cells[i].ToVector3ShiftedWithAltitude(AltitudeLayer.BuildingOnTop) + Vector3.up * 0.02f;
				Rand.PushState();
				Rand.Seed = Gen.HashCombineInt(this.Position.GetHashCode(), i);

				int num = Mathf.CeilToInt(this.growth * (float)currentPlantDefToGrow.plant.maxMeshCount);
				if (num < 1) num = 1;
				
				float num2 = this.currentPlantDefToGrow.plant.visualSizeRange.LerpThroughRange(this.growth);
				float num3 = this.currentPlantDefToGrow.graphicData.drawSize.x * num2;
				int num4 = 0;
				int[] positionIndices = GetStablePositionIndices(currentPlantDefToGrow, center, i);
				bool flag = false;
				foreach (int num5 in positionIndices)
				{
					Vector3 vector;
					if (currentPlantDefToGrow.plant.maxMeshCount != 1)
					{
						int num6 = 1;
						int maxMeshCount = currentPlantDefToGrow.plant.maxMeshCount;
						switch (maxMeshCount)
						{
							case 1:  num6 = 1; break;
							case 4:  num6 = 2; break;
							case 9:  num6 = 3; break;
							case 16: num6 = 4; break;
							case 25: num6 = 5; break;
							default:
								//throw new ArgumentException($"{def} must have plant.maxMeshCount that is a perfect square.");
								Log.Error($"[HDH] {def} must have plant.maxMeshCount that is a perfect square.");
								break;
						}
						float num7 = 1f / (float)num6;

						vector = center;
						vector.y = this.def.Altitude;

						int num8 = num5 / num6;
						int num9 = num5 % num6;

						float spacing = 1f / (float)num6;
						vector.x += (num8 - (num6 - 1) * 0.5f) * spacing;
						vector.z += (num9 - (num6 - 1) * 0.5f) * spacing;
						
						float max = num7 * 0.3f;
						vector += Gen.RandomHorizontalVector(max);
					}
					else
					{
						vector = center + Gen.RandomHorizontalVector(0.05f);
						float num10 = (float)center.z -0.5f;
						if (vector.z - num2 / 2f < num10)
						{
							vector.z = num10 + num2 / 2f + 0.2f;
							flag = true;
						}
					}
					
					bool @bool = Rand.Bool;

					// if (Graphic is Graphic_Random)
					// {
					// 	mat = graphic.MatSingleFor(this);
					// }

					Material mat = baseMat;
					
					Vector2[] uvs;
					Color32 color;
					Graphic.TryGetTextureAtlasReplacementInfo(mat, ThingCategory.Plant.ToAtlasGroup(), @bool, false, out mat, out uvs, out color);
					SetWindExposureColors(workingColors, currentPlantDefToGrow);
					Vector2 size = new Vector2(num3, num3);
					Printer_Plane.PrintPlane(layer, vector, size, mat, 0f, @bool, uvs, workingColors, 0.1f, (float)(this.HashOffset() % 1024));
					num4++;
					if (num4 >= num)
					{
						break;
					}
					
				}
				if (this.def.graphicData.shadowData != null)
				{
					Vector3 center2 = center + this.def.graphicData.shadowData.offset * num2;
					if (flag)
					{
						center2.z = base.Position.ToVector3Shifted().z + this.def.graphicData.shadowData.offset.z;
					}
					center2.y -= 0.03658537f;
					Vector3 volume = this.def.graphicData.shadowData.volume * num2;
					Printer_Shadow.PrintShadow(layer, center, volume, Rot4.North);
				}
				Rand.PopState();
				
			}
			
		}
		
		//draw plants stuff
		public static void SetWindExposureColors(Color32[] colors, ThingDef plant)
		{
			colors[1].a = (colors[2].a = GetWindExposure(plant));
			colors[0].a = (colors[3].a = 0);
		}
		
		//draw plants stuff
		public static byte GetWindExposure(ThingDef plant)
		{
			return (byte)Mathf.Min(255f * plant.plant.topWindExposure, 255f);
		}
		
		//draw plants stuff
		private static int[][][] rootList = new int[25][][];
		//draw plants stuff
		static void PlantPosIndices()
		{
			for (int i = 0; i < 25; i++)
			{
				rootList[i] = new int[8][];
				for (int j = 0; j < 8; j++)
				{
					int[] array = new int[i + 1];
					for (int k = 0; k < i; k++)
					{
						array[k] = k;
					}

					array.Shuffle<int>();
					rootList[i][j] = array;
				}
			}
		}
		
		//draw plants stuff
		private static int[] GetStablePositionIndices(ThingDef plant, Vector3 cell, int index)
		{
			int maxMeshCount = plant.plant.maxMeshCount;
			int num = ((cell.GetHashCode() + index) ^ 42348528) % 8;
			return rootList[maxMeshCount - 1][num];
		}

		// Token: 0x0600012D RID: 301
		private void LoadConfig()
		{
			HydroStatsExtension modExt = this.def.GetModExtension<HydroStatsExtension>();
			if (modExt != null)
			{
				this.capacity = modExt.capacity;
				this.fertility = modExt.fertility;
			}
		}

		// Token: 0x0600012E RID: 302
		private void HandleSowing()
		{
			if (currentPlantDefToGrow == null)
			{
				currentPlantDefToGrow = GetPlantDefToGrow();
			}

			if (currentPlantDefToGrow != GetPlantDefToGrow())
			{
				currentPlantDefToGrow = GetPlantDefToGrow();
				KillAllPlantsAndReset();
			}
			
			foreach (Plant plant in base.PlantsOnMe.ToList<Plant>())
			{
				if (plant.LifeStage != PlantLifeStage.Growing)
					continue;
				
				plant.DeSpawn(DestroyMode.Vanish);
				this.storedPlants++;
			}
			
			if (this.storedPlants >= this.capacity)
			{
				this.storedPlants = this.capacity;
				foreach (Plant plant2 in base.PlantsOnMe.ToList<Plant>())
				{
					plant2.DeSpawn(DestroyMode.Vanish);
				}
				
				// Cancel sowing jobs targeting this grower
				foreach (Pawn pawn in this.Map.mapPawns.AllPawnsSpawned)
				{
					Job curJob = pawn.CurJob;
					if (curJob != null && curJob.def == JobDefOf.Sow && curJob.targetA.HasThing)
					{
						if (curJob.targetA.Thing == this)
						{
							pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
						}
					}
				}
				this.bayStage = Building_HighDensityHydro.BayStage.Growing;
				SoundDefOf.CryptosleepCasket_Accept.PlayOneShot(new TargetInfo(base.Position, base.Map, false));
			}
		}

		// Token: 0x0600012F RID: 303
		private void HandleGrowing()
		{
			if (this.PowerComp != null && !this.PowerComp.PowerOn)
			{
				this.avgGlow = -1f;
				return;
			}
			float temperature = base.Position.GetTemperature(base.Map);
			if (temperature < 10f || temperature > 42f)
			{
				this.avgGlow = -1f;
				return;
			}
			float dayPct = GenLocalDate.DayPercent(this);
			if (dayPct < 0.25f || dayPct > 0.8f)
			{
				this.avgGlow = -1f;
				return;
			}
			ThingDef plantDef = currentPlantDefToGrow;
			if (((plantDef != null) ? plantDef.plant : null) == null)
			{
				this.avgGlow = -1f;
				return;
			}
			
			float growthRate = 0f;
			if (HDH_Mod.settings.lightRequirement)
			{
				float minGlow = plantDef.plant.growMinGlow;
				float optimalGlow = plantDef.plant.growOptimalGlow;
				float totalGlow = 0f;
				int cellCount = 0;
				foreach (IntVec3 cell in this.OccupiedRect().Cells)
				{
					totalGlow += base.Map.glowGrid.GroundGlowAt(cell, false, false);
					cellCount++;
				}
				this.avgGlow = ((cellCount > 0) ? (totalGlow / (float)cellCount) : 1f);
				if (this.avgGlow >= minGlow)
				{
					growthRate = Mathf.Clamp01((this.avgGlow - minGlow) / (optimalGlow - minGlow));
				}
			
				if (growthRate <= 0f)
				{
					return;
				}
			}
			else
			{
				growthRate = 1f;
			}
			
			float growthPerTick = 1f / (60000f * this.growDays()) * 250f;
			this.growth += this.fertility * growthRate * growthPerTick;
			this.growth = Mathf.Clamp01(this.growth);
			if (this.growth >= 1f)
			{
				this.bayStage = Building_HighDensityHydro.BayStage.Harvest;
			}
		}

		// Token: 0x06000130 RID: 304
		private void HandleHarvest()
		{
			foreach (IntVec3 cell in this.OccupiedRect().Cells)
			{
				if (this.storedPlants <= 0)
				{
					break;
				}
				if (!base.Map.thingGrid.ThingsListAt(cell).Any((Thing t) => t is Plant))
				{
					ThingDef plantDef = currentPlantDefToGrow;
					if (plantDef != null)
					{
						((Plant)GenSpawn.Spawn(ThingMaker.MakeThing(plantDef, null), cell, base.Map, WipeMode.Vanish)).Growth = 1f;
						this.storedPlants--;
					}
				}
			}
			if (this.storedPlants == 0)
			{
				this.growth = 0f;

				currentPlantDefToGrow = GetPlantDefToGrow();
				this.bayStage = Building_HighDensityHydro.BayStage.Sowing;
			}
		}

		// Token: 0x06000131 RID: 305
		private float growDays()
		{
			ThingDef plantDef = base.GetPlantDefToGrow();
			if (plantDef == null || plantDef.plant == null)
			{
				return 5f;
			}
			return plantDef.plant.growDays;
		}

		// Token: 0x17000014 RID: 20
		// (get) Token: 0x06000139 RID: 313
		private new CompPowerTrader PowerComp
		{
			get
			{
				if (this.powerCompCached == null)
				{
					this.powerCompCached = base.GetComp<CompPowerTrader>();
				}
				return this.powerCompCached;
			}
		}

		// Token: 0x06000142 RID: 322
		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			//this.KillAllPlantsAndReset();
			base.DeSpawn(mode);
			drawMatrixCache.Clear();
		}

		// Token: 0x0600015B RID: 347
		private void KillAllPlantsAndReset()
		{
			this.storedPlants = 0;
			this.growth = 0f;
			this.bayStage = Building_HighDensityHydro.BayStage.Sowing;
			foreach (Plant plant in base.PlantsOnMe.ToList<Plant>())
			{
				plant.Destroy(DestroyMode.Vanish);
			}
		}

		public int GetHDHCapacity()
		{
			return capacity;
		}

		public int GetNumStoredPlants()
		{
			return storedPlants;
		}

		// Token: 0x040000C8 RID: 200
		private int capacity;

		// Token: 0x040000C9 RID: 201
		private float fertility = 2.8f;

		// Token: 0x040000CA RID: 202
		private Building_HighDensityHydro.BayStage bayStage;

		// Token: 0x040000CB RID: 203
		private Vector2 barsize;

		// Token: 0x040000CC RID: 204
		private float margin;

		// Token: 0x040000CD RID: 205
		private int storedPlants = 0;

		// Token: 0x040000CE RID: 206
		private float growth = 0;

		// Token: 0x040000D1 RID: 209
		private CompPowerTrader powerCompCached;

		// Token: 0x040000E7 RID: 231
		private bool wasPoweredLastTick = true;

		// Token: 0x0400011E RID: 286
		private float avgGlow;

		private ThingDef currentPlantDefToGrow = null;

		// Token: 0x02000009 RID: 9
		private enum BayStage
		{
			// Token: 0x040000C5 RID: 197
			Sowing,
			// Token: 0x040000C6 RID: 198
			Growing,
			// Token: 0x040000C7 RID: 199
			Harvest
		}
	}
}
