using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HighDensityHydro
{
    [StaticConstructorOnStartup]
    public static class DBHHydroPatch
    {
        private static PropertyInfo pipeNetProp;
        private static PropertyInfo anyRecyclersProp;
        private static MethodInfo compTickRareMethod;
        private static Action<object> compPipeTickRareDelegate;
        private static Func<object, bool> anyRecyclersDelegate;
        
        static DBHHydroPatch()
        {
            try
            {
                if (!ModsConfig.ActiveModsInLoadOrder.Any(m => 
                        m.PackageId.IndexOf("dubwise.dubsbadhygiene", StringComparison.OrdinalIgnoreCase) >= 0))
                    return;

                var dbhMod = LoadedModManager.RunningModsListForReading
                    .FirstOrDefault(m => m.PackageId.IndexOf("dubwise.dubsbadhygiene", StringComparison.OrdinalIgnoreCase) >= 0);
                if (dbhMod == null)
                    return;

                var dbhAssembly = dbhMod.assemblies.loadedAssemblies
                    .FirstOrDefault(a => a.GetName().Name.IndexOf("badhygiene", StringComparison.OrdinalIgnoreCase) >= 0);
                if (dbhAssembly == null)
                    return;
                
                var settingsType = dbhAssembly.GetType("DubsBadHygiene.DubsBadHygieneMod");
                var settingsField = settingsType?.GetField("Settings", BindingFlags.Public | BindingFlags.Static);
                var settings = settingsField?.GetValue(null);

                var hydroponicsField = settings?.GetType().GetField("Hydroponics");
                if (hydroponicsField == null)
                    return;

                bool hydroponicsEnabled = (bool)hydroponicsField.GetValue(settings);
                if (!hydroponicsEnabled)
                    return;
                
                Log.Message("[HDH] DBH is loaded and hydroponics setting is enabled, loading HDH DBH patch.");
                
                Type defExtType = dbhAssembly.GetType("DubsBadHygiene.DefExtensions");
                if (defExtType == null)
                {
                    Log.Error("[HDH] Could not find DubsBadHygiene.DefExtensions type.");
                    return;
                }
                object defExtInstance = Activator.CreateInstance(defExtType);
                MethodInfo givePipe = defExtType.GetMethod("GivePipe", BindingFlags.Instance | BindingFlags.Public);
                MethodInfo giveFuel = defExtType.GetMethod("GiveFuel", BindingFlags.Instance | BindingFlags.Public);
                
                if (givePipe == null || giveFuel == null)
                {
                    Log.Error("[HDH] Could not find one or both methods: GivePipe, GiveFuel.");
                    Log.Error("[HDH] Cannot patch HDH with DBH");
                    return;
                }

                foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading
                             .Where(x => x.thingClass == typeof(HighDensityHydro.Building_HighDensityHydro)))
                {
                    if (def.GetCompProperties<CompProperties_Power>() != null)
                    {
                        givePipe?.Invoke(defExtInstance, new object[] { true, def });
                        float fuelCap = -1;
                        float fuelRate = -1;
                        if (def.modExtensions != null)
                        {
                            var hydroStats = def.GetModExtension<HighDensityHydro.HydroStatsExtension>();
                            if (hydroStats != null)
                            {
                                fuelCap = 10f * hydroStats.capacity;
                                fuelRate = 0.2f * hydroStats.capacity;
                            }
                        }

                        if (fuelCap < 0 || fuelRate < 0)
                        {
                            fuelCap = 10f * def.size.x * def.size.z;
                            fuelRate = 0.2f * def.size.x * def.size.z;
                        }
                        
                        giveFuel?.Invoke(defExtInstance, new object[] { true, def, fuelCap, fuelRate,  "Water"});
                    }
                }

                
                // Cache pipeNetField, AnyRecyclers prop, and CompTickRare
                Type compPipeType = dbhAssembly.GetType("DubsBadHygiene.CompPipe");
                if (compPipeType != null)
                {
                    pipeNetProp =  AccessTools.Property(compPipeType, "pipeNet");
                    if (pipeNetProp == null)
                        Log.Error("[HDH] Failed to get 'pipeNet' property from CompPipe");
                    compTickRareMethod =  AccessTools.Method(compPipeType, "CompTickRare");
                    if (compTickRareMethod == null)
                    {
                        Log.Error("[HDH] Failed to get 'CompTickRare' method from CompPipe");
                    }
                    else
                    {
                        try
                        {
                            var param = Expression.Parameter(typeof(object), "instance");
                            var call = Expression.Call(
                                Expression.Convert(param, compPipeType),
                                compTickRareMethod
                            );
                            compPipeTickRareDelegate = Expression.Lambda<Action<object>>(call, param).Compile();
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[HDH] Failed to compile CompTickRare delegate: {ex}");
                        }
                    }
                }
                else
                {
                    Log.Error("[HDH] Failed to find DubsBadHygiene.CompPipe type");
                }

                Type plumbingNetType = dbhAssembly.GetType("DubsBadHygiene.PlumbingNet");
                if (plumbingNetType != null)
                {
                    try
                    {
                        anyRecyclersProp = plumbingNetType.GetProperty("AnyRecyclers", BindingFlags.Instance | BindingFlags.Public);
                        var param = Expression.Parameter(typeof(object), "obj");
                        var casted = Expression.Convert(param, plumbingNetType);
                        var property = Expression.Property(casted, "AnyRecyclers");
                        var lambda = Expression.Lambda<Func<object, bool>>(property, param);
                        anyRecyclersDelegate = lambda.Compile();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[HDH] Failed to compile AnyRecyclers delegate: {ex}");
                    }
                    
                    
                }

                
                Harmony harmony = new Harmony("highdensityhydro.dbh.patch");
                harmony.Patch(
                    original: AccessTools.Method(typeof(Building_HighDensityHydro), "TickRare"),
                    prefix: new HarmonyMethod(typeof(DBHHydroPatch), nameof(HydroTickRare_Prefix))
                );
                
                Log.Message("[HDH] DBH Hydroponics patch applied.");
            }
            catch (Exception ex)
            {
                Log.Error($"[HDH] Error patching DBH Hydroponics: {ex}");
            }
        }

        public static bool HydroTickRare_Prefix(Building_HighDensityHydro __instance)
        {
            var fuel = __instance.GetComp<CompRefuelable>();
            if (fuel != null)
            {
                var compPipe = __instance.AllComps.FirstOrDefault(c => c.GetType().Name == "CompPipe");
                var pipeNet = pipeNetProp?.GetValue(compPipe);
                if (anyRecyclersDelegate != null && (pipeNet == null || !anyRecyclersDelegate(pipeNet)))
                {
                    fuel.ConsumeFuel(0.05f * __instance.StoredPlantCount);
                }

                compPipeTickRareDelegate?.Invoke(compPipe);
                
            }
            return true;
        }
    }
}
