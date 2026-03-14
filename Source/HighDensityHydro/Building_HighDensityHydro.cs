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
		private const string BuiltInSunlampCellLightDefName = "HDH_BuiltInSunlampCellLight";
		private static Texture2D _resetHydroponicsIcon;
		private static Texture2D ResetHydroponicsIcon => _resetHydroponicsIcon ??= ContentFinder<Texture2D>.Get("UI/Commands/ResetHydroponics");
		private static Texture2D _builtInSunlampIcon;
		private static Texture2D BuiltInSunlampIcon => _builtInSunlampIcon ??= ContentFinder<Texture2D>.Get("Things/Building/Production/LampSun");

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
		private const int RareTickInterval = 250;
		private const int LongTickInterval = 2000;
		private int _tickCounter = 0;
		IEnumerable<IntVec3> IPlantToGrowSettable.Cells => this.OccupiedRect().Cells;
		private Building_HighDensityHydro.BayStage _bayStage;
		private CompPowerTrader _powerCompCached;
		private List<Thing> _builtInSunlampCellLights = new List<Thing>();

		// plant stuff
		// I should make a custom plant class or struct
		private ThingDef _currentPlantDefToGrow = null;
		private float _plantHealth = 100f;
		private int _plantAge = 0;
		private int _plantCapacity;
		private int _plantCapacityFromDef;
		private int _numStoredPlants = 0;
		private int _numStoredPlantsBuffer = 0; // buffer used for multi-harvestable plants
		private float _fertility = 2.8f; // default
		private float _curGrowth = 0;
		private float _avgGlow;
		private int _unlitTicks = 0;
		private float _averageHarvestGrowth = 0f; // this keeps track of average growth of plants that can be harvested multiple times
		private bool _requiresLightCheck = true;
		private bool _requiresTemperatureCheck = true;
		private bool _requiresAtmosphereCheck = true;
		private bool _powerScalesCapacity = false;
		private float _basePowerIncrease = 50f;
		private float _capacityExponent = 1.2f;
		private float _powerConsumptionWhenSunlampOff;
		private float _powerConsumptionWhenSunlampOn;
		private float _basePowerIncreaseWhenSunlampOff;
		private float _basePowerIncreaseWhenSunlampOn;
		private float _capacityExponentWhenSunlampOff;
		private float _capacityExponentWhenSunlampOn;
		private int _plantsPerLayer = 4;
		private int _defaultPowerScalingLevel = 0;
		private int _currentPowerScalingLevel = 0;
		private int _maxPowerScalingLevel = 100;
		private bool _builtInSunlampEnabled;
		private bool _hasBuiltInSunlampSetting;
		private int MinimumPowerScalingLevel => _powerScalesCapacity ? 1 : 0;
		
		
		public Building_HighDensityHydro()
		{
		}
		
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
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
			InitializeBuiltInSunlampState(respawningAfterLoad);
			RefreshScaledCapacityAndPower(initializeDefaultScalingLevel: !respawningAfterLoad);
			if (!respawningAfterLoad)
			{
				SyncBuiltInSunlampGlowIfNeeded();
			}
		}
		
		private void LoadConfig()
		{
			var defaultPowerConsumption = def.GetCompProperties<CompProperties_Power>()?.PowerConsumption ?? 0f;
			HydroStatsExtension modExt = def.GetModExtension<HydroStatsExtension>();
			if (modExt != null)
			{
				_plantCapacityFromDef = modExt.capacity;
				_fertility = modExt.fertility;
				_requiresLightCheck = modExt.requiresLightCheck;
				_requiresTemperatureCheck = modExt.requiresTemperatureCheck;
				_requiresAtmosphereCheck = modExt.requiresAtmosphereCheck;
				_powerScalesCapacity = modExt.powerScalesCapacity;
				_basePowerIncrease = modExt.basePowerIncrease;
				_capacityExponent = modExt.capacityExponent;
				_powerConsumptionWhenSunlampOff = modExt.powerConsumptionWhenSunlampOff >= 0f ? modExt.powerConsumptionWhenSunlampOff : defaultPowerConsumption;
				_powerConsumptionWhenSunlampOn = modExt.powerConsumptionWhenSunlampOn >= 0f ? modExt.powerConsumptionWhenSunlampOn : _powerConsumptionWhenSunlampOff;
				_basePowerIncreaseWhenSunlampOff = modExt.basePowerIncreaseWhenSunlampOff >= 0f ? modExt.basePowerIncreaseWhenSunlampOff : _basePowerIncrease;
				_basePowerIncreaseWhenSunlampOn = modExt.basePowerIncreaseWhenSunlampOn >= 0f ? modExt.basePowerIncreaseWhenSunlampOn : _basePowerIncrease;
				_capacityExponentWhenSunlampOff = modExt.capacityExponentWhenSunlampOff >= 0f ? modExt.capacityExponentWhenSunlampOff : _capacityExponent;
				_capacityExponentWhenSunlampOn = modExt.capacityExponentWhenSunlampOn >= 0f ? modExt.capacityExponentWhenSunlampOn : _capacityExponent;
				_defaultPowerScalingLevel = modExt.defaultPowerScalingLevel;
				_maxPowerScalingLevel = modExt.maxPowerScalingLevel;
				_plantsPerLayer = HydroCoreLogic.SanitizePlantsPerLayer(modExt.plantsPerLayer);
			}
			else
			{
				_powerConsumptionWhenSunlampOff = defaultPowerConsumption;
				_powerConsumptionWhenSunlampOn = defaultPowerConsumption;
				_basePowerIncreaseWhenSunlampOff = _basePowerIncrease;
				_basePowerIncreaseWhenSunlampOn = _basePowerIncrease;
				_capacityExponentWhenSunlampOff = _capacityExponent;
				_capacityExponentWhenSunlampOn = _capacityExponent;
			}
		}
		
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look<int>(ref this._tickCounter, "tickCounter", 0, false);
			Scribe_Values.Look<Building_HighDensityHydro.BayStage>(ref this._bayStage, "bayStage", Building_HighDensityHydro.BayStage.Sowing, false);
			Scribe_Values.Look<int>(ref this._numStoredPlants, "storedPlants", 0, false);
			Scribe_Values.Look<int>(ref this._numStoredPlantsBuffer, "storedPlantsBuffer", 0, false);
			Scribe_Values.Look<int>(ref this._plantAge, "plantAge", 0, false);
			Scribe_Values.Look<float>(ref this._curGrowth, "growth", 0f, false);
			Scribe_Values.Look<int>(ref this._unlitTicks, "unlitTicks", 0, false);
			Scribe_Values.Look<float>(ref this._averageHarvestGrowth, "averageHarvestGrowth", 0f, false);
			Scribe_Defs.Look(ref _currentPlantDefToGrow, "queuedPlantDefToGrow");
			Scribe_Values.Look<int>(ref this._plantCapacity, "plantCapacity", 0, false);
			Scribe_Values.Look<int>(ref this._currentPowerScalingLevel, "currentPowerScalingLevel", 0, false);
			Scribe_Values.Look<float>(ref this._plantHealth, "plantHealth", -1f, false);
			Scribe_Values.Look<bool>(ref this._builtInSunlampEnabled, "builtInSunlampEnabled", false, false);
			Scribe_Values.Look<bool>(ref this._hasBuiltInSunlampSetting, "hasBuiltInSunlampSetting", false, false);
			Scribe_Collections.Look(ref _builtInSunlampCellLights, "builtInSunlampCellLights", LookMode.Reference);
			
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (_plantHealth < 0f)
				{
					_plantHealth = _currentPlantDefToGrow?.BaseMaxHitPoints ?? 100f;
				}

				if (!_hasBuiltInSunlampSetting)
				{
					_builtInSunlampEnabled = GetMigratedBuiltInSunlampDefault();
					_hasBuiltInSunlampSetting = true;
				}

				_builtInSunlampCellLights ??= new List<Thing>();
				_builtInSunlampCellLights.RemoveAll(light => light == null);
			}
			
			
			// if (Scribe.mode == LoadSaveMode.LoadingVars)
			// {
			// 	Log.Warning($"[HDH] Loading hydro: storedPlants={storedPlants}, growth={growth}, stage={bayStage}");
			// }
			
			// fix wierd edge case
			// if (_numStoredPlants > _plantCapacity)
			// {
			// 	_numStoredPlants = _plantCapacity;
			// }
		}
		
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		public override string GetInspectString()
		{
			string text = base.GetInspectString();
			if (!_requiresTemperatureCheck)
			{
				text = RemoveInspectLine(text, "CannotGrowBadSeasonTemperature".Translate().ToString());
				text = RemoveInspectLine(text, "GrowSeasonHereNow".Translate().ToString());
			}

			SyncPowerOutputIfNeeded();
			if (_powerScalesCapacity)
			{
				text = RemoveInspectLineStartingWith(text, "PowerNeeded".Translate().ToString());
				text = RemoveInspectLineStartingWith(text, "PowerConsumptionMode".Translate().ToString());
				text += "\n" + "PowerConsumptionMode".Translate() + ": " + CalculateCurrentPowerCost().ToString("F0") + " W";
			}

			text += "\n" + "HDH_NumStoredPlants".Translate(_numStoredPlants + _numStoredPlantsBuffer);

			if (this._numStoredPlants > 0)
			{
				string plantLabel = _currentPlantDefToGrow?.LabelCap ?? "HDH_GenericPlantLabel".Translate().ToString();
				text += "\n" + "HDH_CurrentPlantGrowth".Translate(plantLabel, string.Format("{0:#0}%", this._curGrowth * 100f));

				if (RequiresLightCheck && this._avgGlow >= 0f)
				{
					// "Average Light: {0}%"
					text += "\n" + "HDH_AverageLight".Translate(string.Format("{0:0}%", this._avgGlow * 100f));
				}
			}

			if (Prefs.DevMode)
			{
				text += "\n" + "HDH_DebugUnlitTicks".Translate(_unlitTicks);
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

		private static string RemoveInspectLine(string text, string lineToRemove)
		{
			if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(lineToRemove))
			{
				return text;
			}

			var lines = text
				.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
				.Where(line => !string.Equals(line, lineToRemove, StringComparison.Ordinal));

			return string.Join("\n", lines);
		}

		private static string RemoveInspectLineStartingWith(string text, string linePrefix)
		{
			if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(linePrefix))
			{
				return text;
			}

			var lines = text
				.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
				.Where(line => !line.StartsWith(linePrefix, StringComparison.Ordinal));

			return string.Join("\n", lines);
		}
		
		// Add dev gizmos
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		public override IEnumerable<Gizmo> GetGizmos()
		{
			foreach (var g in base.GetGizmos())
				yield return g;

			yield return new Command_Toggle
			{
				defaultLabel = "HDH_BuiltInSunlampToggle".Translate(),
				defaultDesc = "HDH_BuiltInSunlampToggleDesc".Translate(),
				icon = BuiltInSunlampIcon,
				isActive = () => _builtInSunlampEnabled,
				toggleAction = delegate()
				{
					SetBuiltInSunlampEnabled(!_builtInSunlampEnabled);
				}
			};

			yield return new Command_Action
			{
				defaultLabel = "HDH_ResetHydroponics".Translate(),
				defaultDesc = "HDH_ResetHydroponicsDesc".Translate(),
				icon = ResetHydroponicsIcon,
				action = delegate()
				{
					KillAllPlantsAndReset();
				}
			};

			if (DebugSettings.ShowDevGizmos)
			{
				yield return new Command_Action
				{
					defaultLabel = "HDH_DevIncreaseGrowth".Translate(),
					action = delegate()
					{
						_curGrowth += 0.1f;
						_numStoredPlants = _plantCapacity;
					}
				};
				
					yield return new Command_Action
					{
						defaultLabel = "HDH_DevSetGrowthMax".Translate(),
						action = delegate()
						{
							ForceHarvestReadyForDev();
						}
					};

				yield return new Command_Action
				{
					defaultLabel = "HDH_DevIncreaseAge".Translate(),
					action = delegate()
					{
						_plantAge += 60000;
					}
				};
				
				// yield return new Command_Action
				// {
				// 	defaultLabel = "Dev: Set stored plants to 0",
				// 	action = delegate()
				// 	{
				// 		_numStoredPlants = 0;
				// 	}
					// };
				}
			}

		private void ForceHarvestReadyForDev()
		{
			_curGrowth = 1f;
			_numStoredPlants = _plantCapacity;
			if (_currentPlantDefToGrow == null)
			{
				_currentPlantDefToGrow = GetPlantDefToGrow();
			}

			_bayStage = BayStage.Harvest;
			MarkPlantsMeshDirty();
		}

		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		private void MarkPlantsMeshDirty()
		{
			_drawMatrixCache.Clear();
			if (Map == null)
			{
				return;
			}

			foreach (IntVec3 cell in this.OccupiedRect().Cells)
			{
				Map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.Things);
			}
		}

		// Tick rare should only handle sowing and harvesting stage to make it feel more responsible, should tick every
		// ~4.1s at speed 1 (60tps)
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		public override void TickRare()
		{
			_tickCounter += RareTickInterval;
			//Log.Message($"[HDH] Tick: storedPlants={storedPlants}, growth={growth}, stage={bayStage}");
			
			//TODO: check if we need to call this
			//base.TickRare();
			SyncPowerOutputIfNeeded();
			ApplyVanillaPowerLossDamageToSpawnedPlants(RareTickInterval);
			if (_bayStage != BayStage.Growing)
			{
				ApplyVanillaPowerLossDamageToStoredPlants(RareTickInterval);
			}
			
			switch (_bayStage)
			{
			case BayStage.Sowing:
				HandleSowing();
				break;
			case BayStage.Harvest:
				HandleHarvest();
				break;
			case BayStage.Growing:
				break;
			default:
				return;
			}

			if (_tickCounter >= LongTickInterval)
			{
				_tickCounter = 0;
				TickLong();
			}
		}
		
		// TickLong will simulate plant growth and other plant related data since vanilla also does this in tick long
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		public override void TickLong()
		{
			//TODO: check if we need to call this
			//base.TickLong();
			
			switch (_bayStage)
			{
				case BayStage.Growing:
					ApplyVanillaPowerLossDamageToStoredPlants(LongTickInterval);
					if (HandleInternalPlantLifecycleTick(resetWhenEmpty: true))
					{
						return;
					}

					HandleGrowing();
					return;
				case BayStage.Sowing:
				case BayStage.Harvest:
					if (_numStoredPlants <= 0)
					{
						return;
					}

					HandleInternalPlantLifecycleTick(resetWhenEmpty: false);
					return;
				default:
					return;
			}
		}
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		void IPlantToGrowSettable.SetPlantDefToGrow(ThingDef plantDef)
		{
			SetPlantDefToGrowInternal(plantDef);
		}

		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		public new void SetPlantDefToGrow(ThingDef plantDef)
		{
			SetPlantDefToGrowInternal(plantDef);
		}

		private void SetPlantDefToGrowInternal(ThingDef plantDef)
		{
			ThingDef previousPlantDef = GetPlantDefToGrow();
			base.SetPlantDefToGrow(plantDef);

			if (previousPlantDef == plantDef)
			{
				return;
			}

			if (_bayStage == BayStage.Sowing)
			{
				ResetPlantStateForSowing(clearSpawnedPlants: true);
			}
		}
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		bool IPlantToGrowSettable.CanAcceptSowNow()
		{
			return CanAcceptSowNowInternal();
		}

		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		public new bool CanAcceptSowNow()
		{
			return CanAcceptSowNowInternal();
		}

		private bool CanAcceptSowNowInternal()
		{
			if (_bayStage != BayStage.Sowing || _numStoredPlants >= _plantCapacity)
			{
				return false;
			}

			if (_requiresTemperatureCheck)
			{
				return base.CanAcceptSowNow();
			}

			if (PowerComp != null && !PowerComp.PowerOn)
			{
				return false;
			}

			return GetPlantDefToGrow() != null;
		}
		
		// used to draw progress bar
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
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
			        var barRotation = (this.def.size.x == this.def.size.z)
			        	? this.Rotation
			        	: this.Rotation.Rotated(RotationDirection.Clockwise);
		        GenDraw.FillableBarRequest barReq = new GenDraw.FillableBarRequest
		        {
			        preRotationOffset = offset,
		            center = center,
		            size = this._barsize,
		            fillPercent = this._curGrowth,
		            filledMat = HDH_Graphics.HDHBarFilledMat,
		            unfilledMat = HDH_Graphics.HDHBarUnfilledMat,
		            margin = this._margin,
		            rotation = barRotation
		        };
		        GenDraw.DrawFillableBar(barReq);
		    }
		}
		
		//draw plants stuff
		private static readonly Color32[] workingColors = new Color32[4]; // mimic Plant.workingColors

		//draw plants stuff
		//TODO: clean up this code
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
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
			[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
			static void PlantPosIndices()
			{
				for (int i = 0; i < 25; i++)
				{
					rootList[i] = new int[8][];
					for (int j = 0; j < 8; j++)
					{
						int[] array = new int[i + 1];
						for (int k = 0; k <= i; k++)
						{
							array[k] = k;
						}

					array.Shuffle<int>();
					rootList[i][j] = array;
				}
			}
		}
		
		//draw plants stuff
			[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
			private static int[] GetStablePositionIndices(ThingDef plant, Vector3 cell, int index)
			{
				int maxMeshCount = plant.plant.maxMeshCount;
				if (maxMeshCount < 1 || maxMeshCount > rootList.Length)
				{
					return new[] { 0 };
				}
				int num = ((cell.GetHashCode() + index) ^ 42348528) % 8;
				if (num < 0)
				{
					num += 8;
				}
				return rootList[maxMeshCount - 1][num];
			}



		// sowing logic, should allow sowing and once plants are sowed, the hydroponics will "store" them by deleting
		// them and then adding them to the internal counter
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		private void HandleSowing()
		{
			if (_currentPlantDefToGrow == null || _currentPlantDefToGrow != GetPlantDefToGrow())
			{
				ResetPlantStateForSowing(clearSpawnedPlants: true);
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
				MarkPlantsMeshDirty();
				SoundDefOf.CryptosleepCasket_Accept.PlayOneShot(new TargetInfo(Position, Map, false));
				return;
			}
			
			// if not max, keep storing plants as normal
			bool storedAnyPlants = false;
			foreach (Plant plant in PlantsOnMe.ToList<Plant>())
			{
				// make sure the plant is actually growing (ie. pawns have finishing sowing the plant) before storing it
				if (plant.LifeStage != PlantLifeStage.Growing)
					continue;
				
				plant.DeSpawn(DestroyMode.Vanish);
				_numStoredPlants++;
				storedAnyPlants = true;
			}

			if (storedAnyPlants)
			{
				MarkPlantsMeshDirty();
			}
		}

		// call this only in tick long, logic in here assume per 2000tick intervals
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		private void HandleGrowing()
		{
			ThingDef plantDef = _currentPlantDefToGrow;
			if (plantDef?.plant == null)
			{
				Log.Warning($"[HDH] [{this.ThingID}] No plantDef.plant found; skipping growth");
				return;
			}
			
			if (_numStoredPlants <= 0)
			{
				Log.Warning($"[HDH] [{this.ThingID}] No stored plants during growing stage, going back to sowing");
				ResetPlantStateForSowing(clearSpawnedPlants: true);
				return;
			}
			
			if (_numStoredPlants > _plantCapacity)
			{
				Log.Warning($"[HDH] [{ThingID}] Number of stored plant is at  {_numStoredPlants}, which is higher than the capacity of {_plantCapacity}, setting it to capacity");
				_numStoredPlants = _plantCapacity;
				return;
			}
			
			float growthRateFromGlow = GetGrowthRateFromGlow(plantDef);
			
			if (PowerComp != null && !PowerComp.PowerOn)
			{
				return;
			}
			
			if (!PlantUtility.GrowthSeasonNow(Position, Map, _currentPlantDefToGrow) && _requiresTemperatureCheck)
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
			
			if (RequiresLightCheck)
			{
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
			float fertilitySensitivity = _currentPlantDefToGrow.plant.fertilitySensitivity;
			_curGrowth += HydroCoreLogic.CalculateGrowthDelta(fertilitySensitivity, _fertility, growthRateFromGlow, growDays);
			_curGrowth = Mathf.Clamp01(_curGrowth);
			if (_curGrowth >= 1f)
			{
				_bayStage = BayStage.Harvest;
				MarkPlantsMeshDirty();
			}
		}

		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
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
			
			var completion = HydroCoreLogic.ResolveHarvestCompletion(
				plantDef.plant.harvestAfterGrowth,
				_numStoredPlantsBuffer,
				_averageHarvestGrowth,
				_fertility,
				plantDef.plant.fertilitySensitivity,
				plantDef.plant.growDays,
				_plantAge,
				plantDef.plant.LifespanTicks);

			if (completion.Action == HarvestCompletionAction.ResetToSowing)
			{
				ResetPlantStateForSowing(clearSpawnedPlants: false);
				return;
			}

			_numStoredPlants = completion.StoredPlants;
			_numStoredPlantsBuffer = completion.StoredPlantsBuffer;
			_curGrowth = completion.Growth;
			_averageHarvestGrowth = completion.AverageHarvestGrowth;
			_bayStage = BayStage.Growing;
			MarkPlantsMeshDirty();
		}

		// Token: 0x17000014 RID: 20
		// (get) Token: 0x06000139 RID: 313
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
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
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			// Check if we're being minified
			if (mode == DestroyMode.Vanish)
			{
				//Log.Message($"[HDH] {this} is being minified — clearing internal plants.");
				KillAllPlantsAndReset();
			}

			DespawnBuiltInSunlampCellLights();
			base.DeSpawn(mode);
			_drawMatrixCache.Clear();
		}

		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		public override void PostMapInit()
		{
			base.PostMapInit();
			SyncBuiltInSunlampGlowIfNeeded();
		}

		protected override void ReceiveCompSignal(string signal)
		{
			base.ReceiveCompSignal(signal);
			switch (signal)
			{
			case "PowerTurnedOn":
			case "PowerTurnedOff":
			case "FlickedOn":
			case "FlickedOff":
				SyncBuiltInSunlampGlowIfNeeded();
				break;
			}
		}

		// Token: 0x0600015B RID: 347
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		private void KillAllPlantsAndReset()
		{
			ResetPlantStateForSowing(clearSpawnedPlants: true);
		}
		
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		private void ResetPlantStateForSowing(bool clearSpawnedPlants)
		{
			_bayStage = BayStage.Sowing;
			_numStoredPlants = 0;
			_numStoredPlantsBuffer = 0;
			_plantAge = 0;
			_unlitTicks = 0;
			_curGrowth = 0f;
			_averageHarvestGrowth = 0f;
			_currentPlantDefToGrow = GetPlantDefToGrow();
			_plantHealth = _currentPlantDefToGrow?.BaseMaxHitPoints ?? 100f;
			MarkPlantsMeshDirty();
			
			if (!clearSpawnedPlants || !base.Spawned)
			{
				return;
			}
			
			foreach (Plant plant in PlantsOnMe.ToList<Plant>())
			{
				plant.Destroy(DestroyMode.Vanish);
			}
		}

		private bool HandleInternalPlantLifecycleTick(bool resetWhenEmpty)
		{
			ThingDef plantDef = _currentPlantDefToGrow;
			if (plantDef?.plant == null)
			{
				return false;
			}

			if (_numStoredPlants <= 0)
			{
				if (resetWhenEmpty)
				{
					Log.Warning($"[HDH] [{ThingID}] No stored plants during growing stage, going back to sowing");
					ResetPlantStateForSowing(clearSpawnedPlants: true);
					return true;
				}

				return false;
			}

			if (_numStoredPlants > _plantCapacity)
			{
				Log.Warning($"[HDH] [{ThingID}] Number of stored plant is at {_numStoredPlants}, which is higher than the capacity of {_plantCapacity}, setting it to capacity");
				_numStoredPlants = _plantCapacity;
			}

			float dyingDamage = PlantCurrentDyingDamagePerTick * LongTickInterval;
			PlantTakeDamage(dyingDamage);

			if (_plantHealth <= 0)
			{
				string key = DyingBecauseExposedToVacuum
					? "MessagePlantDiedOfRot_ExposedToVacuum"
					: "MessagePlantDiedOfRot";
				Messages.Message(key.Translate(_currentPlantDefToGrow?.label ?? "HDH_GenericPlantLabel".Translate().ToString()), new TargetInfo(Position, Map, false), MessageTypeDefOf.NegativeEvent, true);
				ResetPlantStateForSowing(clearSpawnedPlants: false);
				return true;
			}

			_plantAge += LongTickInterval;
			if (plantDef.plant.LifespanTicks > 0 && _plantAge > plantDef.plant.LifespanTicks)
			{
				Messages.Message("MessagePlantDiedOfRot".Translate(_currentPlantDefToGrow?.label ?? "HDH_GenericPlantLabel".Translate().ToString()), new TargetInfo(Position, Map, false), MessageTypeDefOf.NegativeEvent, true);
				ResetPlantStateForSowing(clearSpawnedPlants: false);
				return true;
			}

			GetGrowthRateFromGlow(plantDef);
			return false;
		}

		private float GetGrowthRateFromGlow(ThingDef plantDef)
		{
			_avgGlow = -1f;
			float growthRateFromGlow = 1f;
			if (RequiresLightCheck)
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

				_avgGlow = cellCount > 0 ? totalGlow / cellCount : 1f;
				growthRateFromGlow = HydroCoreLogic.CalculateGlowGrowthRate(_avgGlow, minGlow, optimalGlow);
			}

			_unlitTicks = HydroCoreLogic.UpdateUnlitTicks(
				RequiresLightCheck,
				RequiresLightCheck,
				growthRateFromGlow,
				_unlitTicks,
				LongTickInterval);

			return growthRateFromGlow;
		}

		private void ApplyVanillaPowerLossDamageToSpawnedPlants(int tickInterval)
		{
			if (tickInterval <= 0 || PowerComp == null || PowerComp.PowerOn)
			{
				return;
			}

			float damage = HydroCoreLogic.CalculateVanillaPowerLossDamage(tickInterval, RareTickInterval);
			if (damage <= 0f)
			{
				return;
			}

			foreach (Plant plant in PlantsOnMe.ToList())
			{
				plant.TakeDamage(new DamageInfo(DamageDefOf.Rotting, damage));
			}
		}

		private void ApplyVanillaPowerLossDamageToStoredPlants(int tickInterval)
		{
			if (tickInterval <= 0 || PowerComp == null || PowerComp.PowerOn || !HasStoredPlantBatch())
			{
				return;
			}

			PlantTakeDamage(HydroCoreLogic.CalculateVanillaPowerLossDamage(tickInterval, RareTickInterval));
		}

		private bool HasStoredPlantBatch()
		{
			if (_currentPlantDefToGrow?.plant == null)
			{
				return false;
			}

			return _numStoredPlants > 0 || _numStoredPlantsBuffer > 0;
		}

		private void RefreshScaledCapacityAndPower(bool initializeDefaultScalingLevel)
		{
			var levelOffset = initializeDefaultScalingLevel ? _defaultPowerScalingLevel : 0;
			var currentLevel = initializeDefaultScalingLevel ? 0 : _currentPowerScalingLevel;
			_currentPowerScalingLevel = HydroCoreLogic.ClampScalingLevel(currentLevel, levelOffset, _maxPowerScalingLevel, MinimumPowerScalingLevel);

			_plantCapacity = CalculateCurrentPlantCapacity();
			SyncPowerOutputIfNeeded();
		}

		private void SyncPowerOutputIfNeeded()
		{
			var powerComp = PowerComp;
			if (powerComp != null)
			{
				var expectedPowerOutput = -1f * CalculateCurrentPowerCost();
				if (!Mathf.Approximately(powerComp.PowerOutput, expectedPowerOutput))
				{
					powerComp.PowerOutput = expectedPowerOutput;
				}
			}

			SyncBuiltInSunlampGlowIfNeeded();
		}

		private void SyncBuiltInSunlampGlowIfNeeded()
		{
			if (!Spawned || Map == null)
			{
				return;
			}

			if (ShouldEmitBuiltInSunlampLight())
			{
				EnsureBuiltInSunlampCellLights();
				return;
			}

			DespawnBuiltInSunlampCellLights();
		}

		private bool ShouldEmitBuiltInSunlampLight()
		{
			if (!_builtInSunlampEnabled || !Spawned || Map == null)
			{
				return false;
			}

			if (!FlickUtility.WantsToBeOn(this))
			{
				return false;
			}

			var powerComp = PowerComp;
			return powerComp == null || powerComp.PowerOn;
		}

		private void EnsureBuiltInSunlampCellLights()
		{
			var helperDef = DefDatabase<ThingDef>.GetNamedSilentFail(BuiltInSunlampCellLightDefName);
			if (helperDef == null)
			{
				return;
			}

			var expectedCells = this.OccupiedRect().Cells.Where(cell => cell.InBounds(Map)).ToList();
			if (BuiltInSunlampCellLightsMatch(expectedCells, helperDef))
			{
				return;
			}

			DespawnBuiltInSunlampCellLights();
			foreach (var cell in expectedCells)
			{
				var helperLight = ThingMaker.MakeThing(helperDef);
				var spawnedLight = GenSpawn.Spawn(helperLight, cell, Map, WipeMode.Vanish);
				_builtInSunlampCellLights.Add(spawnedLight);
			}
		}

		private bool BuiltInSunlampCellLightsMatch(List<IntVec3> expectedCells, ThingDef helperDef)
		{
			if (_builtInSunlampCellLights == null || _builtInSunlampCellLights.Count != expectedCells.Count)
			{
				return false;
			}

			var remainingCells = new HashSet<IntVec3>(expectedCells);
			foreach (var helperLight in _builtInSunlampCellLights)
			{
				if (helperLight == null ||
				    helperLight.Destroyed ||
				    !helperLight.Spawned ||
				    helperLight.Map != Map ||
				    helperLight.def != helperDef ||
				    !remainingCells.Remove(helperLight.Position))
				{
					return false;
				}
			}

			return remainingCells.Count == 0;
		}

		private void DespawnBuiltInSunlampCellLights()
		{
			if (_builtInSunlampCellLights == null)
			{
				_builtInSunlampCellLights = new List<Thing>();
				return;
			}

			foreach (var helperLight in _builtInSunlampCellLights)
			{
				if (helperLight != null && !helperLight.Destroyed)
				{
					helperLight.Destroy(DestroyMode.Vanish);
				}
			}

			_builtInSunlampCellLights.Clear();
		}

		private void InitializeBuiltInSunlampState(bool respawningAfterLoad)
		{
			if (respawningAfterLoad || _hasBuiltInSunlampSetting)
			{
				return;
			}

			_builtInSunlampEnabled = HDH_Mod.settings?.defaultBuiltInSunlampEnabled ?? false;
			_hasBuiltInSunlampSetting = true;
		}

		private bool GetMigratedBuiltInSunlampDefault()
		{
			if (def?.defName == "HDH_Hydroponics_Quantum")
			{
				return true;
			}

			return HDH_Mod.settings?.defaultBuiltInSunlampEnabled ?? false;
		}

		private void SetBuiltInSunlampEnabled(bool enabled)
		{
			if (_builtInSunlampEnabled == enabled)
			{
				return;
			}

			_builtInSunlampEnabled = enabled;
			_hasBuiltInSunlampSetting = true;
			if (enabled)
			{
				_unlitTicks = 0;
			}

			SyncPowerOutputIfNeeded();
		}
		
			[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
			public float PlantCurrentDyingDamagePerTick
			{
				get
				{
					if (!base.Spawned || _currentPlantDefToGrow?.plant == null)
					{
						return 0f;
					}
				float num = 0f;
				// TODO: add dying for no sunlight and dying exposed to light
				// if (!_currentPlantDefToGrow.plant.diesToLight && _currentPlantDefToGrow.plant.dieIfNoSunlight && this.unlitTicks > 450000)
				// {
				// 	num = Mathf.Max(num, 0.005f);
				// }
				// if (DyingBecauseExposedToLight)
				// {
				// 	float lerpPct = _avgGlow;
				// 	num = Mathf.Max(num, Plant.DyingDamagePerTickBecauseExposedToLight.LerpThroughRange(lerpPct));
				// }
					num = HydroCoreLogic.CalculateDyingDamagePerTick(
						_currentPlantDefToGrow.plant.LimitedLifespan,
						_plantAge,
						_currentPlantDefToGrow.plant.LifespanTicks,
						!_currentPlantDefToGrow.plant.diesToLight &&
						_currentPlantDefToGrow.plant.dieIfNoSunlight &&
						RequiresLightCheck,
						_unlitTicks,
						_requiresAtmosphereCheck,
						DyingBecauseExposedToVacuum,
						base.Position.GetVacuum(base.Map));
				// We don't track pollution or terrain
				// if (this.DyingFromPollution || this.DyingFromNoPollution)
				// {
				// 	num = Mathf.Max(num, Plant.PollutionDamagePerTickRange.RandomInRangeSeeded(base.Position.GetHashCode()));
				// }
				// if (this.DyingBecauseOfTerrainTags)
				// {
				// 	num = Mathf.Max(num, 0.005f);
				// }
				return num;
			}
		}

		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		private void PlantTakeDamage(float damage)
		{
			_plantHealth -= damage;
		}
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		private bool DyingBecauseExposedToLight
		{
			get
			{
				return _currentPlantDefToGrow.plant.diesToLight && base.Spawned && _avgGlow > 0f;
			}
		}

			[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
			private bool DyingBecauseExposedToVacuum
			{
				get
				{
					return _requiresAtmosphereCheck &&
					       _currentPlantDefToGrow?.plant != null &&
					       !_currentPlantDefToGrow.plant.vacuumResistant &&
					       base.Spawned &&
					       base.Position.GetVacuum(base.Map) >= 0.5f;
				}
			}
		
		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		private bool Dying
		{
			get
			{
				return this.PlantCurrentDyingDamagePerTick > 0f;
			}
		}

		[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
		public void AdjustCapacity(int scalingLevelOffset)
		{
			_currentPowerScalingLevel = HydroCoreLogic.ClampScalingLevel(_currentPowerScalingLevel, scalingLevelOffset, _maxPowerScalingLevel, MinimumPowerScalingLevel);
			RefreshScaledCapacityAndPower(initializeDefaultScalingLevel: false);
		}

		public int CalculateCurrentPlantCapacity()
		{
			return HydroCoreLogic.CalculateCapacity(_plantCapacityFromDef, _currentPowerScalingLevel, _plantsPerLayer);
		}
		
		public float CalculateCurrentPowerCost()
		{
			return CalculatePowerCost(_currentPowerScalingLevel);
		}
		public float CalculatePowerCost(int scalingLevel)
		{
			var power = _powerCompCached ?? GetComp<CompPowerTrader>();
			if (power == null)
				return 0f;

			if (!_powerScalesCapacity)
			{
				return CurrentBasePowerConsumption;
			}
			
			return HydroCoreLogic.CalculatePowerCost(CurrentBasePowerConsumption, CurrentBasePowerIncrease, CurrentCapacityExponent, scalingLevel);
		}

		public float CalculateNextPowerCostIncrease()
		{
			return CalculatePowerCost(_currentPowerScalingLevel + 1) - CalculatePowerCost(_currentPowerScalingLevel);
		}
		public int MaxPlantCapacity => _plantCapacity;
		public int StoredPlantCount => _numStoredPlants;
		public ThingDef CurrentPlantedDef => _currentPlantDefToGrow;
		public ThingDef SelectedPlantDef => GetPlantDefToGrow();
		public int PlantAge => _plantAge;
		public float PlantHealth => _plantHealth;
		public float Fertility => _fertility;
		public float PlantGrowth => _curGrowth;
		public float LastAverageGlow => _avgGlow;
		public bool RequiresLightCheck => _requiresLightCheck;
		public bool RequiresTemperatureCheck => _requiresTemperatureCheck;
		public bool RequiresAtmosphereCheck => _requiresAtmosphereCheck;
		public bool PowerScalesCapacity => _powerScalesCapacity;
		public float BasePowerIncrease => _basePowerIncrease;
		public float CapacityExponent => _capacityExponent;
		public int PlantsPerLayer => _plantsPerLayer;
		public int CurrentPowerScalingLevel => _currentPowerScalingLevel;
		private float CurrentBasePowerConsumption => _builtInSunlampEnabled ? _powerConsumptionWhenSunlampOn : _powerConsumptionWhenSunlampOff;
		private float CurrentBasePowerIncrease => _builtInSunlampEnabled ? _basePowerIncreaseWhenSunlampOn : _basePowerIncreaseWhenSunlampOff;
		private float CurrentCapacityExponent => _builtInSunlampEnabled ? _capacityExponentWhenSunlampOn : _capacityExponentWhenSunlampOff;
	}
}
