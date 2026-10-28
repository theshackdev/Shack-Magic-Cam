using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TheShack.MagicCam
{
	/// Game members this mod needs that aren't public. Upstream compiled against publicized copies of
	/// the game assemblies; our mods compile against the plain ones, so these go through HarmonyLib's
	/// field refs and delegates instead. Each one resolves once, when this class is first touched.
	internal static class Access
	{
		private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> RightItemRef = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_rightItem");
		private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> LeftItemRef = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem");
		private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> HelmetItemRef = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_helmetItem");
		private static readonly AccessTools.FieldRef<Humanoid, VisEquipment> VisEquipmentRef = AccessTools.FieldRefAccess<Humanoid, VisEquipment>("m_visEquipment");
		private static readonly AccessTools.FieldRef<VisEquipment, GameObject> HelmetItemInstanceRef = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_helmetItemInstance");
		private static readonly AccessTools.FieldRef<Player, int> AutoPickupMaskRef = AccessTools.FieldRefAccess<Player, int>("m_autoPickupMask");
		private static readonly AccessTools.FieldRef<ItemDrop, ZNetView> ItemNetViewRef = AccessTools.FieldRefAccess<ItemDrop, ZNetView>("m_nview");
		private static readonly AccessTools.FieldRef<GameCamera, float> CameraDistanceRef = AccessTools.FieldRefAccess<GameCamera, float>("m_distance");
		private static readonly AccessTools.FieldRef<SE_Demister, GameObject> BallInstanceRef = AccessTools.FieldRefAccess<SE_Demister, GameObject>("m_ballInstance");
		private static readonly AccessTools.FieldRef<SE_Demister, Vector3> BallVelocityRef = AccessTools.FieldRefAccess<SE_Demister, Vector3>("m_ballVel");
		private static readonly AccessTools.FieldRef<SE_Demister, int> CoverRayMaskRef = AccessTools.FieldRefAccess<SE_Demister, int>("m_coverRayMask");
		private static readonly AccessTools.FieldRef<StatusEffect, float> StatusTimeRef = AccessTools.FieldRefAccess<StatusEffect, float>("m_time");
		private static readonly AccessTools.FieldRef<StatusEffect, float> StatusMessageTimerRef = AccessTools.FieldRefAccess<StatusEffect, float>("m_msgTimer");
		private static readonly AccessTools.FieldRef<bool> EnableAutoPickupRef = AccessTools.StaticFieldRefAccess<bool>(AccessTools.Field(typeof(Player), "m_enableAutoPickup"));
		private static readonly AccessTools.FieldRef<float> TakeInputDelayRef = AccessTools.StaticFieldRefAccess<float>(AccessTools.Field(typeof(Character), "takeInputDelay"));
		private static readonly AccessTools.FieldRef<List<CraftingStation>> AllStationsRef = AccessTools.StaticFieldRefAccess<List<CraftingStation>>(AccessTools.Field(typeof(CraftingStation), "m_allStations"));

		// Character.TakeInput is protected virtual; the delegate dispatches to Player's override like a normal call.
		private static readonly Func<Character, bool> TakeInputCall = AccessTools.MethodDelegate<Func<Character, bool>>(AccessTools.Method(typeof(Character), "TakeInput"));
		private static readonly Action<Player, bool, float> UpdatePlacementCall = AccessTools.MethodDelegate<Action<Player, bool, float>>(AccessTools.Method(typeof(Player), "UpdatePlacement"));
		private static readonly Action<Localization, string, string> AddWordCall = AccessTools.MethodDelegate<Action<Localization, string, string>>(AccessTools.Method(typeof(Localization), "AddWord"));

		internal static ItemDrop.ItemData RightItem(Humanoid humanoid) => RightItemRef(humanoid);
		internal static ItemDrop.ItemData LeftItem(Humanoid humanoid) => LeftItemRef(humanoid);
		internal static ItemDrop.ItemData HelmetItem(Humanoid humanoid) => HelmetItemRef(humanoid);
		internal static VisEquipment GetVisEquipment(Humanoid humanoid) => VisEquipmentRef(humanoid);
		internal static GameObject HelmetItemInstance(VisEquipment visEquipment) => HelmetItemInstanceRef(visEquipment);
		internal static int AutoPickupMask(Player player) => AutoPickupMaskRef(player);
		internal static ZNetView ItemNetView(ItemDrop item) => ItemNetViewRef(item);
		internal static ref float CameraDistance(GameCamera camera) => ref CameraDistanceRef(camera);
		internal static ref GameObject BallInstance(SE_Demister demister) => ref BallInstanceRef(demister);
		internal static ref Vector3 BallVelocity(SE_Demister demister) => ref BallVelocityRef(demister);
		internal static int CoverRayMask(SE_Demister demister) => CoverRayMaskRef(demister);
		internal static ref float StatusTime(StatusEffect effect) => ref StatusTimeRef(effect);
		internal static ref float StatusMessageTimer(StatusEffect effect) => ref StatusMessageTimerRef(effect);
		internal static bool EnableAutoPickup => EnableAutoPickupRef();
		internal static ref float TakeInputDelay => ref TakeInputDelayRef();
		internal static List<CraftingStation> AllStations => AllStationsRef();

		internal static bool TakeInput(Character character) => TakeInputCall(character);
		internal static void UpdatePlacement(Player player, bool takeInput, float dt) => UpdatePlacementCall(player, takeInput, dt);
		internal static void AddWord(Localization localization, string key, string text) => AddWordCall(localization, key, text);
	}
}
