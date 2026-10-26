using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using DiveOrDieCoop.Networking;
using HarmonyLib;
using UnityEngine;

namespace DiveOrDieCoop.Patches
{
	internal static class CoopCoreFixes { }

	internal static class CoopSavePathHelper
	{
		internal static string DataPath => Application.isEditor ? "Assets/Resources/Data/Json/" : Application.persistentDataPath + "/";
		internal static string SavePath(int slot, string suffix = "") => Path.Combine(DataPath, $"Save_{slot}.coop{suffix}.sav");
		internal static string MapPath(int slot) => Path.Combine(DataPath, $"Map_{slot}.coop.data");
	}

	[HarmonyPatch(typeof(ZipSerializer), "GetZipPath")]
	internal static class ZipSerializer_GetZipPath_CoopSavePatch
	{
		private static bool Prefix(int saveSlot, string suffix, ref string __result)
		{
			__result = CoopSavePathHelper.SavePath(saveSlot, suffix ?? string.Empty);
			return false;
		}
	}

	[HarmonyPatch(typeof(GameState), nameof(GameState.RemoveSave))]
	internal static class GameState_RemoveSave_CoopPatch
	{
		private static bool Prefix(GameState __instance, int saveSlot)
		{
			if (saveSlot < 0 || saveSlot >= 8) return false;
			try
			{
				string main = CoopSavePathHelper.SavePath(saveSlot);
				string backup = CoopSavePathHelper.SavePath(saveSlot, "_backup");
				string map = CoopSavePathHelper.MapPath(saveSlot);
				string corrupt = main + ".corrupt";
				if (File.Exists(main)) File.Delete(main);
				if (File.Exists(backup)) File.Delete(backup);
				if (File.Exists(map)) File.Delete(map);
				if (File.Exists(corrupt)) File.Delete(corrupt);
				for (int i = 0; i <= 3; i++)
				{
					string quick = CoopSavePathHelper.SavePath(saveSlot, $"_quicksave_{i}");
					if (File.Exists(quick)) File.Delete(quick);
				}
			}
			catch (Exception ex) { Debug.LogWarning($"DiveOrDieCoop: failed to remove coop save slot {saveSlot}: {ex}"); }
			return false;
		}
	}

	[HarmonyPatch(typeof(GameState), "TryRestoreFromBackup")]
	internal static class GameState_TryRestoreFromBackup_CoopPatch
	{
		private static bool Prefix(GameState __instance, int saveSlot, ref bool __result)
		{
			__result = false;
			try
			{
				string main = CoopSavePathHelper.SavePath(saveSlot);
				string backup = CoopSavePathHelper.SavePath(saveSlot, "_backup");
				if (!File.Exists(backup)) return false;
				ZipSerializer serializer = new ZipSerializer();
				if (!serializer.IsSaveArchiveValid(saveSlot, "_backup")) return false;
				if (File.Exists(main))
				{
					string corrupt = main + ".corrupt";
					if (File.Exists(corrupt)) File.Delete(corrupt);
					File.Move(main, corrupt);
				}
				File.Copy(backup, main, true);
				ProgressionState progression = serializer.DeserializeFromJson<ProgressionState>("Progression.json", true, saveSlot);
				if (progression == null) return false;
				__result = true;
				return false;
			}
			catch (Exception ex) { Debug.LogWarning($"DiveOrDieCoop: failed to restore coop save slot {saveSlot}: {ex.Message}"); return false; }
		}
	}

	[HarmonyPatch]
	internal static class CoopMapFilePathPatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			foreach (Type type in new[] { typeof(FogRevealController), typeof(MenuLibraryTabMap) })
				foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
					if (!method.IsAbstract && !method.IsGenericMethodDefinition && method.GetMethodBody() != null)
						yield return method;
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			foreach (CodeInstruction instruction in instructions)
			{
				if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string text)
				{
					if (text == "Map_{0}.data") instruction.operand = "Map_{0}.coop.data";
					else if (text == "/Map_{0}.data") instruction.operand = "/Map_{0}.coop.data";
					else if (text == "\\Map_{0}.data") instruction.operand = "\\Map_{0}.coop.data";
				}
				yield return instruction;
			}
		}
	}
}