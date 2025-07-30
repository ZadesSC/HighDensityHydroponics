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
		private Dictionary<IntVec3, Matrix4x4> _drawMatrixCache = new Dictionary<IntVec3, Matrix4x4>();
		private Vector2 _barsize;
		private float _margin;
		
		// hydroponic bay stages
		private enum BayStage
		{
			Sowing,
			Growing,
			Harvest
		}
		
		// hydroponic stuff
		private int _tickCounter = 0;
		IEnumerable<IntVec3> IPlantToGrowSettable.Cells => this.OccupiedRect().Cells;
		private Building_HighDensityHydro.BayStage _bayStage;
		private CompPowerTrader _powerCompCached;
		private bool _wasPoweredLastTick = true;

		// plant stuff
		private ThingDef _currentPlantDefToGrow = null;
		private int _plantAge = 0;
		private int _plantCapacity;
		private int _numStoredPlants = 0;
		private int _numStoredPlantsBuffer = 0; // buffer used for multi-harvestable plants
		private float _fertility = 2.8f;
		private float _curGrowth = 0;
		private float _avgGlow;
		private float _averageHarvestGrowth = 0f; // this keeps track of average growth of plants that can be harvested multiple times
		
		public Building_HighDensityHydro()
		{
		}
		
		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			LoadConfig();
			
			int x = def.size.x;
			float y = (x == 1) ? 0.6f : 0.1f;
			y = 0.1f;
			_margin = ((x == 1) ? 0.15f : 0.08f);
			_margin = 0.05f;
			_barsize = new Vector2(def.size.z - 0.4f, y);

			_drawMatrixCache.Clear();
			
			//TODO: maybe move this somewhere else, should only be called and generated once
			PlantPosIndices();
		}
		
		private void LoadConfig()
		{
			HydroStatsExtension modExt = def.GetModExtension<HydroStatsExtension>();
			if (modExt != null)
			{
				_plantCapacity = modExt.capacity;
				_fertility = modExt.fertility;
			}
		}
		
		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look<int>(ref this._tickCounter, "tickCounter", 0, false);
			Scribe_Values.Look<Building_HighDensityHydro.BayStage>(ref this._bayStage, "bayStage", Building_HighDensityHydro.BayStage.Sowing, false);
			Scribe_Values.Look<int>(ref this._numStoredPlants, "storedPlants", 0, false);
			Scribe_Values.Look<int>(ref this._numStoredPlantsBuffer, "storedPlantsBuffer", 0, false);
			Scribe_Values.Look<int>(ref this._plantAge, "plantAge", 0, false);
			Scribe_Values.Look<float>(ref this._curGrowth, "growth", 0f, false);
			Scribe_Values.Look<float>(ref this._averageHarvestGrowth, "averageHarvestGrowth", 0f, false);
			Scribe_Defs.Look(ref _currentPlantDefToGrow, "queuedPlantDefToGrow");
			
			// if (Scribe.mode == LoadSaveMode.LoadingVars)
			// {
			// 	Log.Warning($"[HDH] Loading hydro: storedPlants={storedPlants}, growth={growth}, stage={bayStage}");
			// }
			
			// fix wierd edge case
			if (_numStoredPlants > _plantCapacity)
			{
				_numStoredPlants = _plantCapacity;
			}
		}
		
		public override string GetInspectString()
		{
			string text = base.GetInspectString();

			text += "\n" + "HDH_NumStoredPlants".Translate(_numStoredPlants + _numStoredPlantsBuffer);

			if (this._numStoredPlants > 0)
			{
				// "{PlantLabel}: {GrowthPercent}%"
				text += "\n" + _currentPlantDefToGrow.LabelCap + ": " + string.Format("{0:#0}%", this._curGrowth * 100f);

				if (HDH_Mod.settings.lightRequirement && this._avgGlow >= 0f)
				{
					// "Average Light: {0}%"
					text += "\n" + "HDH_AverageLight".Translate(string.Format("{0:0}%", this._avgGlow * 100f));
				}
			}

			// if (Prefs.DevMode)
			// {
			// 	text += "\nPlant Age: " + _plantAge + "(" + ((float)_plantAge / 60000) + ")";
			// 	text += "\nBay Stage: " + _bayStage;
			// 	text += "\nCurrent PlantDef: " + (_currentPlantDefToGrow?.defName ?? "null");
			// 	text += "\nFertility: " + _fertility.ToString("P0");
			// 	text += "\nAvgGlow (raw): " + _avgGlow.ToString("F2");
			// }

			return text;
		}
		
		// Add dev gizmos
		public override IEnumerable<Gizmo> GetGizmos()
		{
			foreach (var g in base.GetGizmos())
				yield return g;

			if (DebugSettings.ShowDevGizmos)
			{
				yield return new Command_Action
				{
					defaultLabel = "Dev: Increase Growth by 10%",
					action = delegate()
					{
						_curGrowth += 0.1f;
						_numStoredPlants = _plantCapacity;
					}
				};
				
				yield return new Command_Action
				{
					defaultLabel = "Dev: Set Growth to 100%",
					action = delegate()
					{
						_curGrowth = 1f;
						_numStoredPlants = _plantCapacity;
					}
				};

				yield return new Command_Action
				{
					defaultLabel = "Dev: Increase Age by 1 day",
					action = delegate()
					{
						_plantAge += 60000;
					}
				};
			}
		}

		// Tick rare should only handle sowing and harvesting stage to make it feel more responsible, should tick every
		// ~4.1s at speed 1 (60tps)
		public override void TickRare()
		{
			_tickCounter += 250;
			//Log.Message($"[HDH] Tick: storedPlants={storedPlants}, growth={growth}, stage={bayStage}");
			
			//TODO: check if we need to call this
			//base.TickRare();
			
			bool poweredNow = this.PowerComp == null || this.PowerComp.PowerOn;
			if (HDH_Mod.settings.killPlantsOnNoPower)
			{
				if (this._wasPoweredLastTick && !poweredNow)
				{
					this.KillAllPlantsAndReset();
				}
			}
			this._wasPoweredLastTick = poweredNow;
			
			switch (_bayStage)
			{
			case BayStage.Sowing:
				HandleSowing();
				return;
			case BayStage.Harvest:
				HandleHarvest();
				return;
			case BayStage.Growing:
				break;
			default:
				return;
			}

			if (_tickCounter >= 2000)
			{
				_tickCounter = 0;
				TickLong();
			}
		}
		
		// TickLong will simulate plant growth and other plant related data since vanilla also does this in tick long
		public override void TickLong()
		{
			//TODO: check if we need to call this
			//base.TickLong();
			
			switch (_bayStage)
			{
				case BayStage.Growing:
					HandleGrowing();
					return;
				case BayStage.Sowing:
				case BayStage.Harvest:
				default:
					return;
			}
		}
		public new void SetPlantDefToGrow(ThingDef plantDef)
		{
			base.SetPlantDefToGrow(plantDef);
		}
		public new bool CanAcceptSowNow()
		{
			return base.CanAcceptSowNow() && _bayStage == BayStage.Sowing && _numStoredPlants < _plantCapacity;
		}
		
		// used to draw progress bar
		protected override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
		    base.DrawAt(drawLoc, flip);
			
		    //Draw growth bar during Growing stage
		    if (this._numStoredPlants > 0 && this._bayStage == BayStage.Growing)
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
		            size = this._barsize,
		            fillPercent = this._curGrowth,
		            filledMat = HDH_Graphics.HDHBarFilledMat,
		            unfilledMat = HDH_Graphics.HDHBarUnfilledMat,
		            margin = this._margin,
		            rotation = this.Rotation.Rotated(RotationDirection.Clockwise)
		        };
		        GenDraw.DrawFillableBar(barReq);
		    }
		}
		
		//draw plants stuff
		private static readonly Color32[] workingColors = new Color32[4]; // mimic Plant.workingColors

		//draw plants stuff
		//TODO: clean up this code
		public override void Print(SectionLayer layer)
		{
			base.Print(layer);
			
			if (this._currentPlantDefToGrow == null || this._numStoredPlants <= 0)
				return;
			
			List<IntVec3> cells = this.OccupiedRect().Cells.ToList();
			int cellCount = cells.Count;
			int visibleCount = 0;
			if (_bayStage == BayStage.Growing)
			{
				visibleCount = cellCount;
			}
			else if (this._bayStage == BayStage.Sowing && this._numStoredPlants > 0)
			{
				visibleCount = Mathf.Clamp(Mathf.FloorToInt((this._numStoredPlants / (float)this._plantCapacity) * cellCount), 1, cellCount);
			}
			else if (this._bayStage == BayStage.Harvest)
			{
				visibleCount = 0;
			}
			
			Graphic graphic = _currentPlantDefToGrow.graphicData.Graphic;
			if (_curGrowth <= _currentPlantDefToGrow.plant.harvestMinGrowth && _currentPlantDefToGrow.plant.immatureGraphic != null)
			{
				graphic = _currentPlantDefToGrow.plant.immatureGraphic;
			}

			Material baseMat = graphic.MatSingleFor(this);
			//Material mat = graphic.MatAt(Rot4.North); ;

			for (int i = 0; i < visibleCount; i++)
			{
				Vector3 center = cells[i].ToVector3ShiftedWithAltitude(AltitudeLayer.BuildingOnTop) + Vector3.up * 0.02f;
				Rand.PushState();
				Rand.Seed = Gen.HashCombineInt(this.Position.GetHashCode(), i);

				int num = Mathf.CeilToInt(this._curGrowth * (float)_currentPlantDefToGrow.plant.maxMeshCount);
				if (num < 1) num = 1;
				
				float num2 = this._currentPlantDefToGrow.plant.visualSizeRange.LerpThroughRange(this._curGrowth);
				float num3 = this._currentPlantDefToGrow.graphicData.drawSize.x * num2;
				int num4 = 0;
				int[] positionIndices = GetStablePositionIndices(_currentPlantDefToGrow, center, i);
				bool flag = false;
				foreach (int num5 in positionIndices)
				{
					Vector3 vector;
					if (_currentPlantDefToGrow.plant.maxMeshCount != 1)
					{
						int num6 = 1;
						int maxMeshCount = _currentPlantDefToGrow.plant.maxMeshCount;
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
							vector.z = num10 + num2 / 2f;// + 0.2f; // removed  +0.2f because it doesn't match vanilla when the plants actually spawn in during harvest stage
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
					SetWindExposureColors(workingColors, _currentPlantDefToGrow);
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



		// sowing logic, should allow sowing and once plants are sowed, the hydroponics will "store" them by deleting
		// them and then adding them to the internal counter
		private void HandleSowing()
		{
			if (_currentPlantDefToGrow == null)
			{
				_currentPlantDefToGrow = GetPlantDefToGrow();
			}

			if (_currentPlantDefToGrow != GetPlantDefToGrow())
			{
				_currentPlantDefToGrow = GetPlantDefToGrow();
				KillAllPlantsAndReset();
			}
			
			// if max, clean up current stage and move on to grow stage
			if (_numStoredPlants >= _plantCapacity)
			{
				_numStoredPlants = _plantCapacity;
				
				// Cancel sowing jobs targeting this grower and then delete all current plants on the hydroponics
				foreach (Pawn pawn in Map.mapPawns.AllPawnsSpawned)
				{
					Job curJob = pawn.CurJob;
					if (curJob == null || curJob.def != JobDefOf.Sow || !curJob.targetA.HasThing) continue;
					if (curJob.targetA.Thing == this)
					{
						pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, true);
					}
				}
				
				foreach (Plant plant in PlantsOnMe.ToList<Plant>())
				{
					plant.DeSpawn(DestroyMode.Vanish);
				}
				
				_bayStage = BayStage.Growing;
				SoundDefOf.CryptosleepCasket_Accept.PlayOneShot(new TargetInfo(Position, Map, false));
				return;
			}
			
			// if not max, keep storing plants as normal
			foreach (Plant plant in PlantsOnMe.ToList<Plant>())
			{
				// make sure the plant is actually growing (ie. pawns have finishing sowing the plant) before storing it
				if (plant.LifeStage != PlantLifeStage.Growing)
					continue;
				
				plant.DeSpawn(DestroyMode.Vanish);
				_numStoredPlants++;
			}
		}

		// call this only in tick long, logic in here assume per 2000tick intervals
		private void HandleGrowing()
		{
			ThingDef plantDef = _currentPlantDefToGrow;
			if (plantDef?.plant == null)
			{
				Log.Warning("[HDH] No plantDef.plant found; skipping growth");
				return;
			}
			
			// age the plant
			_plantAge += 2000;
			
			// check if plant died of old age lmao
			// 0 case is ageless
			if (plantDef.plant.LifespanTicks > 0 && _plantAge > plantDef.plant.LifespanTicks)
			{
				//Log.Message($"[HDH] Plant died of old age at {_plantAge} ticks (lifespan: {_currentPlantDefToGrow.plant.LifespanTicks})");
				Messages.Message("MessagePlantDiedOfRot".Translate(_currentPlantDefToGrow?.label ?? "plant"), new TargetInfo(base.Position, Map, false), MessageTypeDefOf.NegativeEvent, true);
				_curGrowth = 0f;
				_plantAge = 0;
				_numStoredPlants = 0;
				_numStoredPlantsBuffer = 0;
				_averageHarvestGrowth = 0f;
				_currentPlantDefToGrow = GetPlantDefToGrow();
				_bayStage = BayStage.Sowing;
				return;
			}
			
			
			_avgGlow = -1f;
			
			if (PowerComp != null && !PowerComp.PowerOn)
			{
				return;
			}
			
			if (!PlantUtility.GrowthSeasonNow(Position, Map, _currentPlantDefToGrow))
			{
				return;
			}
			
			float dayPct = GenLocalDate.DayPercent(this);
			if (dayPct < 0.25f || dayPct > 0.8f)
			{
				return;
			}
			
			//TODO: maybe add leafless check?
			//TODO: check for vacuum as well
			//TODO: check and track unlit ticks for rotting plants
			
			float growthRateFromGlow = 0f;
			if (HDH_Mod.settings.lightRequirement)
			{
				float minGlow = plantDef.plant.growMinGlow;
				float optimalGlow = plantDef.plant.growOptimalGlow;
				float totalGlow = 0f;
				int cellCount = 0;
				foreach (IntVec3 cell in this.OccupiedRect().Cells)
				{
					totalGlow += Map.glowGrid.GroundGlowAt(cell, false, false);
					cellCount++;
				}
				_avgGlow = ((cellCount > 0) ? (totalGlow / cellCount) : 1f);
				if (_avgGlow >= minGlow)
				{
					growthRateFromGlow = Mathf.Clamp01((_avgGlow - minGlow) / (optimalGlow - minGlow));
				}
			
				if (growthRateFromGlow <= 0f)
				{
					return;
				}
			}
			else
			{
				growthRateFromGlow = 1f;
			}
			
			// assume we are ticking in TickLong(), which we should
			float growDays = _currentPlantDefToGrow.plant.growDays;
			float growthPerTick = 1f / (60000f * growDays) * 2000f;
			_curGrowth += _fertility * growthRateFromGlow * growthPerTick;
			_curGrowth = Mathf.Clamp01(_curGrowth);
			if (_curGrowth >= 1f)
			{
				_bayStage = BayStage.Harvest;
			}
		}

		private void HandleHarvest()
		{
			// this keeps track of unharvested plants
			// all plants need to be harvested/stored before proceeding to next stage
			bool allHarvested = true;
			
			ThingDef plantDef = _currentPlantDefToGrow;
			if (plantDef?.plant == null)
			{
				Log.Warning("[HDH] No plantDef.plant found; skipping harvest");
				return;
			}
			
			foreach (IntVec3 cell in this.OccupiedRect().Cells)
			{
				List<Thing> things = Map.thingGrid.ThingsListAt(cell);
				Plant existingPlant = things.OfType<Plant>().FirstOrDefault();
				
				if (existingPlant != null)
				{
					allHarvested = false;
				}
				
				if (existingPlant == null && _numStoredPlants > 0)
				{
					Plant spawnedPlant = ((Plant)GenSpawn.Spawn(ThingMaker.MakeThing(plantDef, null), cell, Map, WipeMode.Vanish));
					spawnedPlant.Growth = _curGrowth;
					spawnedPlant.Age = _plantAge;
					_numStoredPlants--;
				}
				else
				{
					if (plantDef.plant.harvestAfterGrowth > 0 && existingPlant?.def != null && 
					    existingPlant.def == plantDef && existingPlant.Growth < 0.999f)
					{
						_averageHarvestGrowth += existingPlant.Growth;
						existingPlant.DeSpawn(DestroyMode.Vanish);
						_numStoredPlantsBuffer++;
					}
				}
			}

			if (_numStoredPlants != 0) return;
			if (!allHarvested) return;
			
			// if it is a single harvest plant (eg rice), reset and go back to sowing stage
			// if it is a multi harvest plant (eg ambrosia), reset buffer, set growth, and go to growing stage
			if (plantDef.plant.harvestAfterGrowth == 0f)
			{
				_curGrowth = 0f;
				_plantAge = 0;
				_numStoredPlants = 0;
				_numStoredPlantsBuffer = 0;
				_averageHarvestGrowth = 0f;
				_currentPlantDefToGrow = GetPlantDefToGrow();
				_bayStage = BayStage.Sowing;
			}
			else
			{
				_averageHarvestGrowth /= (float)_numStoredPlantsBuffer;
				
				// Calculate estimated time to grow
				// Assume optimal conditions and give a 5% fudge factor
				// 0 case is ageless
				float growthRemaining = 1f - _averageHarvestGrowth;
				float estimatedTicksToGrow = (growthRemaining * 60000f * plantDef.plant.growDays) / 
					(32500f * _fertility) * 60000f * 1.05f;
				
				int ageAfterNextGrow = _plantAge + (int)estimatedTicksToGrow;
				bool willDieBeforeNextHarvest = plantDef.plant.LifespanTicks > 0 && ageAfterNextGrow > plantDef.plant.LifespanTicks;
				
				if (willDieBeforeNextHarvest)
				{
					_curGrowth = 0f;
					_plantAge = 0;
					_numStoredPlantsBuffer = 0;
					_averageHarvestGrowth = 0f;
					_currentPlantDefToGrow = GetPlantDefToGrow();
					_bayStage = BayStage.Sowing;
					return;
				}
				
				_numStoredPlants = _numStoredPlantsBuffer;
				_numStoredPlantsBuffer = 0;
				_curGrowth = _averageHarvestGrowth;
				_averageHarvestGrowth = 0f;
				_bayStage = BayStage.Growing;
			}
		}

		// Token: 0x17000014 RID: 20
		// (get) Token: 0x06000139 RID: 313
		private new CompPowerTrader PowerComp
		{
			get
			{
				if (this._powerCompCached == null)
				{
					this._powerCompCached = base.GetComp<CompPowerTrader>();
				}
				return this._powerCompCached;
			}
		}

		// Token: 0x06000142 RID: 322
		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			// Check if we're being minified
			if (mode == DestroyMode.Vanish)
			{
				//Log.Message($"[HDH] {this} is being minified — clearing internal plants.");
				KillAllPlantsAndReset();
			}

			base.DeSpawn(mode);
			_drawMatrixCache.Clear();
		}

		// Token: 0x0600015B RID: 347
		private void KillAllPlantsAndReset()
		{
			_bayStage = BayStage.Sowing;
			_numStoredPlants = 0;
			_numStoredPlantsBuffer = 0;
			_plantAge = 0;
			_curGrowth = 0f;
			_averageHarvestGrowth = 0f;
			foreach (Plant plant in PlantsOnMe.ToList<Plant>())
			{
				plant.Destroy(DestroyMode.Vanish);
			}
		}

		public int GetHDHCapacity()
		{
			return _plantCapacity;
		}

		public int GetNumStoredPlants()
		{
			return _numStoredPlants;
		}


	}
}
