// using HarmonyLib;
// using RimWorld;
// using Verse;
// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Reflection;
// using System.Reflection.Emit;
//
// namespace HighDensityHydro
// {
//     [HarmonyPatch(typeof(Command_SetPlantToGrow), "ProcessInput")]
//     public static class Patch_Command_SetPlantToGrow
//     {
//         // Inject extra plants into tmpAvailablePlants after PlantUtility.ValidPlantTypesForGrowers(this.settables)
//         public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
//         {
//             var code = new List<CodeInstruction>(instructions);
//             var injectMethod = typeof(Patch_Command_SetPlantToGrow).GetMethod(nameof(InjectExtraPlants), BindingFlags.Static | BindingFlags.NonPublic);
//
//             var callIndex = code.FindIndex(ci =>
//                 ci.opcode == OpCodes.Call &&
//                 ci.operand is MethodInfo mi &&
//                 mi == AccessTools.Method(typeof(PlantUtility), nameof(PlantUtility.ValidPlantTypesForGrowers))
//             );
//
//             if (callIndex == -1)
//             {
//                 Log.Error("[HDH] Could not find ValidPlantTypesForGrowers call to inject extra plants.");
//                 return code;
//             }
//
//             // After the call and foreach begins, inject our method
//             code.Insert(callIndex + 1, new CodeInstruction(OpCodes.Ldarg_0)); // this
//             code.Insert(callIndex + 2, new CodeInstruction(OpCodes.Call, injectMethod));
//
//             return code;
//         }
//
//         private static void InjectExtraPlants(Command_SetPlantToGrow __instance)
//         {
//             if (!HDH_Mod.settings.allowOtherVanillaPlants)
//                 return;
//
//             var settables = Traverse.Create(__instance).Field("settables").GetValue<List<IPlantToGrowSettable>>();
//             if (settables == null || !settables.All(s => s is Building_HighDensityHydro))
//                 return;
//
//             foreach (var defName in new[] { "Plant_Corn", "Plant_Haygrass", "Plant_Devilstrand" })
//             {
//                 var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
//                 if (def != null && !Traverse.Create(typeof(Command_SetPlantToGrow)).Field("tmpAvailablePlants").GetValue<List<ThingDef>>().Contains(def))
//                 {
//                     if (Command_SetPlantToGrow.IsPlantAvailable(def, settables.First().Map))
//                     {
//                         Traverse.Create(typeof(Command_SetPlantToGrow)).Field("tmpAvailablePlants").GetValue<List<ThingDef>>().Add(def);
//                         Log.Message($"[HDH] Injected plant {def.defName}");
//                     }
//                 }
//                 else if (def == null)
//                 {
//                     Log.Warning($"[HDH] Could not find plant def '{defName}'");
//                 }
//             }
//         }
//     }
// }
