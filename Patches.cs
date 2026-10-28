using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TheShack.MagicCam.Compatibility.WardIsLove;
using UnityEngine;

namespace TheShack.MagicCam
{
	// Method names are strings, not nameof, wherever the game method isn't public.

	[HarmonyPatch(typeof(Localization), "SetupLanguage")]
	static class Localization_SetupLanguage_Patch
	{
		static void Postfix(Localization __instance)
		{
			Utils.AddLocalizations(__instance);
			PickupBlockedHud.LanguageChanged();
		}
	}

	[HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.HaveBuildStationInRange))]
	static class CraftingStation_HaveBuildStationInRange_Patch
	{
		static void Postfix(string name, Vector3 point, ref CraftingStation __result)
		{
			if (!__result && Utils.InBuildMode() && Utils.FindCameraStation(name, point) is { } station)
				__result = station;
		}
	}

	[HarmonyPatch]
	static class Player_BuildCameraPlacementDistance_Patch
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.DeclaredMethod(typeof(Player), "PieceRayTest");
			yield return AccessTools.DeclaredMethod(typeof(Player), "UpdateWearNTearHover");
			yield return AccessTools.DeclaredMethod(typeof(Player), "CopyPiece");
			yield return AccessTools.DeclaredMethod(typeof(Player), "RemovePiece");
		}

		static void Prefix(ref float ___m_maxPlaceDistance, out float __state)
		{
			__state = ___m_maxPlaceDistance;
			if (Utils.InBuildMode())
				___m_maxPlaceDistance = Mathf.Max(___m_maxPlaceDistance, Plugin.distanceCanBuildFromAvatar.Value);
		}

		static Exception Finalizer(Exception __exception, ref float ___m_maxPlaceDistance, float __state)
		{
			___m_maxPlaceDistance = __state;
			return __exception;
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.SetLocalPlayer))]
	static class Player_SetLocalPlayer_Patch
	{
		static void Postfix()
		{
			PickupBlockedHud.Cleanup();
			Utils.DisableBuildMode();
		}
	}

	/// Dying in build mode used to leave the camera detached with Player.Update skipped and
	/// PlayerController.TakeInput forced false, so you watched your own corpse and could not
	/// take control back. SetLocalPlayer only fires on logout or character switch, not on death.
	[HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
	static class Player_OnDeath_Patch
	{
		static void Postfix()
		{
			Utils.DisableBuildMode();
		}
	}

	/// Logging out in build mode destroys the player without a SetLocalPlayer or OnDeath, which left
	/// the camera detached on the next character (Cartur's 1.1.0 fix).
	[HarmonyPatch(typeof(Player), "OnDestroy")]
	static class Player_OnDestroy_Patch
	{
		static void Prefix(Player __instance)
		{
			if (Utils.IsLocalPlayer(__instance)) Utils.DisableBuildMode();
		}
	}

	[HarmonyPatch(typeof(Player), "Update")]
	static class Player_Update_Patch
	{
		private static readonly string[] HotbarButtons =
		{
			"Hotbar1", "Hotbar2", "Hotbar3", "Hotbar4", "Hotbar5", "Hotbar6", "Hotbar7", "Hotbar8",
		};

		// Vanilla registers these alongside the plain ones, from a "Hotbar{0}Alt" format string in
		// ZInput.ResetKBMButtons. Upstream's loop left them out, so alt hotbar binds silently
		// stopped working while the build camera was up.
		private static readonly string[] HotbarAltButtons =
		{
			"Hotbar1Alt", "Hotbar2Alt", "Hotbar3Alt", "Hotbar4Alt", "Hotbar5Alt", "Hotbar6Alt", "Hotbar7Alt", "Hotbar8Alt",
		};

		// None of the early returns below assign __runOriginal = true. Harmony passes the running
		// value in, so writing true would resurrect a method an earlier prefix deliberately
		// skipped - leaving it alone is both correct and what "do nothing" means here.
		static void Prefix(Player __instance, ref bool __runOriginal)
		{
			if (!Utils.IsLocalPlayer(__instance) || !Utils.InBuildMode()) return;

			if (!Utils.CanUseCamera())
			{
				Utils.ShowComfortMessage();
				Utils.DisableBuildMode();
				return;
			}

			if (Utils.ShouldDeactivateBuildMode(__instance))
			{
				Utils.DisableBuildMode();
				return;
			}

			// A portal hop moves the player but leaves the camera clamped to a station in the
			// world just left, stranding it outside its own range check.
			if (__instance.IsTeleporting())
			{
				Utils.DisableBuildMode();
				return;
			}

			__runOriginal = false;
			if (!Access.TakeInput(__instance)) return;
			// Vanilla's own hotbar loop in Player.Update is exactly:
			//     if (ZInput.GetButtonDown($"Hotbar{i}") || ZInput.GetButtonDown($"Hotbar{i}Alt"))
			//         UseHotbarItem(i);
			for (int index = 0; index < HotbarButtons.Length; ++index)
			{
				if (ZInput.GetButtonDown(HotbarButtons[index]) || ZInput.GetButtonDown(HotbarAltButtons[index]))
					__instance.UseHotbarItem(index + 1);
			}

			if (ZInput.GetButtonDown("Hide") || ZInput.GetButtonDown("JoyHide"))
			{
				if ((Access.RightItem(__instance) != null || Access.LeftItem(__instance) != null) && !__instance.InAttack())
					__instance.HideHandItems();
			}

			Access.UpdatePlacement(__instance, true, Time.deltaTime);
		}

		static void Postfix(Player __instance)
		{
			if (!Utils.IsLocalPlayer(__instance) || !Utils.ToggleKeyDown()) return;
			if (Utils.InBuildMode())
			{
				if (Access.TakeInput(__instance)) Utils.DisableBuildMode();
				return;
			}

			if (!Access.TakeInput(__instance))
			{
				Utils.LogWhenVerbose("Build Mode not enabled because chat, console, menu, inventory, map, or similar is open.");
				return;
			}

			if (!Utils.ToolIsEquipped(__instance))
			{
				Utils.LogWhenVerbose("Build Mode not enabled because hammer is not equipped.");
				return;
			}

			if (!Utils.BuildStationInRange(__instance))
			{
				Utils.LogWhenVerbose("Build Mode not enabled because no build station (e.g. workbench) is in range.");
				return;
			}

			if (!Utils.CanUseCamera())
			{
				Utils.ShowComfortMessage();
				Utils.LogWhenVerbose("Build Mode not enabled because the cozy and comfort requirement is not met.");
				return;
			}

			// Vanilla owns a detached camera of its own (GameCamera.UpdateFreeFly, reached through
			// the freefly console command). Our GameCamera.UpdateCamera prefix skips the original
			// outright, so entering build mode during free-fly would put two systems on one transform.
			if (GameCamera.InFreeFly())
			{
				Utils.LogWhenVerbose("Build Mode not enabled because the game's own free-fly camera is active.");
				return;
			}

			Utils.EnableBuildMode();
		}
	}

	[HarmonyPatch(typeof(PlayerController), "TakeInput")]
	static class PlayerController_TakeInput_Patch
	{
		static void Prefix(ref bool __result, ref bool __runOriginal)
		{
			if (!Utils.InBuildMode()) return;

			__result = false;
			__runOriginal = false;
		}
	}

	[HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
	[HarmonyBefore("Azumatt.FirstPersonMode")]
	[HarmonyPriority(Priority.VeryHigh)]
	static class GameCamera_UpdateCamera_Patch
	{
		private const float PickupScanInterval = 0.1f;
		private static float _nextPickupScan;

		static void Prefix(float dt, GameCamera __instance, ref bool __runOriginal)
		{
			if (!Utils.InBuildMode())
			{
				_nextPickupScan = 0f;
				return;
			}

			Utils.KeepOutOfFirstPerson(__instance);
			Utils.UpdateBuildCamera(dt, __instance);
			if (Time.time >= _nextPickupScan)
			{
				_nextPickupScan = Time.time + PickupScanInterval;
				if (Utils.TryGetAutoPickupPlayer(out Player player))
				{
					if (WardAccess.Check(player.GetPlayerID(), __instance.transform.position)) Utils.AutoPickup(__instance, player);
				}
			}

			__runOriginal = false;
		}

		static void Postfix(GameCamera __instance)
		{
			if (Utils.InBuildMode()) DvergrCircletCameraLight.Update(__instance);
		}
	}
}
