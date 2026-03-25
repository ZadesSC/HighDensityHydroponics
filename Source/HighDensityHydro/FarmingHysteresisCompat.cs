using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HighDensityHydro
{
    internal static class FarmingHysteresisCompat
    {
        private static readonly object InitLock = new object();
        private static bool _initialized;
        private static bool _available;
        private static Func<IPlantToGrowSettable, object> _getFarmingHysteresisData;
        private static Func<object, bool> _getEnabled;
        private static Func<object> _getSettings;
        private static Func<object, bool> _getControlSowing;
        private static Func<IPlantToGrowSettable, bool> _getAllowSow;

        internal static Func<IPlantToGrowSettable, bool?> AllowSowOverrideForTests { get; set; }

        internal static bool AllowsSowing(IPlantToGrowSettable plantGrower)
        {
            var allowSowOverride = AllowSowOverrideForTests;
            if (allowSowOverride != null)
            {
                var overriddenValue = allowSowOverride(plantGrower);
                if (overriddenValue.HasValue)
                {
                    return overriddenValue.Value;
                }
            }

            if (plantGrower == null || !EnsureInitialized())
            {
                return true;
            }

            try
            {
                var settings = _getSettings();
                if (settings == null || !_getControlSowing(settings))
                {
                    return true;
                }

                var data = _getFarmingHysteresisData(plantGrower);
                if (data == null || !_getEnabled(data))
                {
                    return true;
                }

                return _getAllowSow(plantGrower);
            }
            catch (Exception ex)
            {
                DisableCompatibility(ex);
                return true;
            }
        }

        private static bool EnsureInitialized()
        {
            if (_initialized)
            {
                return _available;
            }

            lock (InitLock)
            {
                if (_initialized)
                {
                    return _available;
                }

                try
                {
                    var extensionsType = AccessTools.TypeByName("FarmingHysteresis.Extensions.PlantToGrowSettableExtensions");
                    var modType = AccessTools.TypeByName("FarmingHysteresis.FarmingHysteresisMod");
                    if (extensionsType == null || modType == null)
                    {
                        _available = false;
                        _initialized = true;
                        return false;
                    }

                    var getFarmingHysteresisDataMethod = AccessTools.Method(extensionsType, "GetFarmingHysteresisData", new[] { typeof(IPlantToGrowSettable) });
                    var getAllowSowMethod = AccessTools.Method(extensionsType, "GetAllowSow", new[] { typeof(IPlantToGrowSettable) });
                    var getSettingsMethod = AccessTools.PropertyGetter(modType, "Settings");
                    var getEnabledMethod = getFarmingHysteresisDataMethod == null
                        ? null
                        : AccessTools.PropertyGetter(getFarmingHysteresisDataMethod.ReturnType, "Enabled");
                    var getControlSowingMethod = getSettingsMethod == null
                        ? null
                        : AccessTools.PropertyGetter(getSettingsMethod.ReturnType, "ControlSowing");

                    if (getFarmingHysteresisDataMethod == null ||
                        getAllowSowMethod == null ||
                        getSettingsMethod == null ||
                        getEnabledMethod == null ||
                        getControlSowingMethod == null)
                    {
                        _available = false;
                        _initialized = true;
                        return false;
                    }

                    _getFarmingHysteresisData = plantGrower => getFarmingHysteresisDataMethod.Invoke(null, new object[] { plantGrower });
                    _getAllowSow = plantGrower => (bool)getAllowSowMethod.Invoke(null, new object[] { plantGrower });
                    _getSettings = () => getSettingsMethod.Invoke(null, null);
                    _getEnabled = data => (bool)getEnabledMethod.Invoke(data, null);
                    _getControlSowing = settings => (bool)getControlSowingMethod.Invoke(settings, null);
                    _available = true;
                }
                catch (Exception ex)
                {
                    DisableCompatibility(ex);
                }
                finally
                {
                    _initialized = true;
                }

                return _available;
            }
        }

        private static void DisableCompatibility(Exception ex)
        {
            _available = false;
            _getFarmingHysteresisData = null;
            _getEnabled = null;
            _getSettings = null;
            _getControlSowing = null;
            _getAllowSow = null;
            Log.Warning("[HDH] Farming Hysteresis compatibility was disabled after an unexpected reflection error: " + ex.Message);
        }
    }
}
