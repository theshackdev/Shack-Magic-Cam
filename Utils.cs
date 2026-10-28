using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace TheShack.MagicCam
{
	public static class Utils
	{
		private const float ComfortRefreshInterval = 0.2f;
		private static readonly Collider[] NearbyPickupColliders = new Collider[128];
		private static readonly HashSet<int> NearbyPickupItemIds = new();
		private static readonly Dictionary<string, CraftingStation> CameraStationsByName = new();
		private static readonly int TerrainMask = LayerMask.GetMask("terrain");
		private static CraftingStation? _cameraStation;
		private static Vector3 _lastSafeCameraPosition;
		private static bool _hasLastSafeCameraPosition;
		private static Player? _comfortPlayer;
		private static bool _hasRequiredComfort;
		private static float _nextComfortRefresh;
		private static float _nextComfortMessageTime;

		/// Zoom held just outside first person while build mode is on (see KeepOutOfFirstPerson).
		private const float AboveFirstPerson = 0.01f;
		private static float? _distanceBeforeBuild;

		internal static void LogWhenVerbose(string message)
		{
			if (Plugin.verboseLogging.Value != Plugin.Toggle.On) return;
			// A caller can reach here with no local player - the hotkey handler runs from a
			// Player.Update postfix, but DisableBuildMode paths do not.
			if (Player.m_localPlayer) Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, message);
			Plugin.Log.LogInfo(message);
		}

		public static bool InBuildMode()
		{
			return Player.m_localPlayer && Plugin.BuildCameraActive;
		}

		/// First-person mods (Landoria's First Person, and any that switch on at full zoom) treat a
		/// zero camera distance as "first person" and move the camera every frame, which pushed the
		/// build camera backwards to the edge of its range so WASD seemed dead. Hold the zoom just
		/// outside first person while building and give it back afterwards (Cartur's 1.1.1 fix).
		internal static void KeepOutOfFirstPerson(GameCamera camera)
		{
			ref float distance = ref Access.CameraDistance(camera);
			if (distance > 0.001f) return;
			_distanceBeforeBuild ??= distance;
			distance = AboveFirstPerson;
		}

		private static void RestoreCameraDistance()
		{
			if (_distanceBeforeBuild is { } distance && GameCamera.instance)
				Access.CameraDistance(GameCamera.instance) = distance;
			_distanceBeforeBuild = null;
		}

		internal static void DisableBuildMode()
		{
			Plugin.BuildCameraActive = false;
			RestoreCameraDistance();
			_cameraStation = null;
			CameraStationsByName.Clear();
			_hasLastSafeCameraPosition = false;
			_comfortPlayer = null;
			_hasRequiredComfort = false;
			_nextComfortRefresh = 0f;
			_nextComfortMessageTime = 0f;
			DvergrCircletCameraLight.Cleanup();
			SE_DemisterCameraPatch.Cleanup();
		}

		/// KeyboardShortcut.IsDown() is false whenever any key outside the shortcut is held, so the
		/// toggle did nothing while you walked or ran. Only the shortcut's own keys count here
		/// (Cartur's 1.1.0 fix).
		internal static bool ToggleKeyDown()
		{
			KeyboardShortcut shortcut = Plugin.toggleBuildMode.Value;
			if (shortcut.MainKey == KeyCode.None || !Input.GetKeyDown(shortcut.MainKey)) return false;
			foreach (KeyCode modifier in shortcut.Modifiers)
			{
				if (!Input.GetKey(modifier)) return false;
			}

			return true;
		}

		internal static void EnableBuildMode()
		{
			if (!Player.m_localPlayer || !TryFindCameraStation(Player.m_localPlayer.transform.position, out _cameraStation)) return;
			Plugin.BuildCameraActive = true;
			Quaternion rotation = Player.m_localPlayer.m_eye.transform.rotation;
			// eulerAngles.x is 0-360, so looking up came out near 360 and was clamped to straight
			// down. DeltaAngle turns it into -180..180, keeping the direction you were looking.
			Plugin.CameraPitch = Mathf.DeltaAngle(0f, rotation.eulerAngles.x);
			Plugin.CameraYaw = rotation.eulerAngles.y;
			if (GameCamera.instance)
			{
				_lastSafeCameraPosition = GameCamera.instance.transform.position;
				_hasLastSafeCameraPosition = true;
			}

			Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, "Entering Build Mode.");
		}

		internal static bool IsLocalPlayer(Player player)
		{
			return Player.m_localPlayer && player == Player.m_localPlayer;
		}

		private static bool IsTool(ItemDrop.ItemData? itemData)
		{
			return itemData?.m_shared.m_buildPieces;
		}

		internal static bool ToolIsEquipped(Player player)
		{
			return IsTool(Access.RightItem(player));
		}

		internal static bool ShouldDeactivateBuildMode(Player player)
		{
			return !ToolIsEquipped(player);
		}

		internal static bool BuildStationInRange(Player player)
		{
			return TryFindCameraStation(player.transform.position, out _);
		}

		private static bool TryFindCameraStation(Vector3 point, out CraftingStation? station)
		{
			station = null;
			float nearestDistanceSquared = float.MaxValue;
			foreach (CraftingStation candidate in Access.AllStations)
			{
				if (!candidate) continue;
				float distanceSquared = HorizontalDistanceSquared(candidate.transform.position, point);
				float range = GetCameraStationRange(candidate);
				if (distanceSquared <= range * range && distanceSquared < nearestDistanceSquared)
				{
					station = candidate;
					nearestDistanceSquared = distanceSquared;
				}
			}

			return station;
		}

		internal static CraftingStation? FindCameraStation(string name, Vector3 point)
		{
			if (CameraStationsByName.TryGetValue(name, out CraftingStation cachedStation) && IsCameraStationInRange(cachedStation, name, point))
				return cachedStation;

			CraftingStation? closest = null;
			float nearestDistanceSquared = float.MaxValue;
			foreach (CraftingStation station in Access.AllStations)
			{
				if (!station || station.m_name != name) continue;
				float distanceSquared = HorizontalDistanceSquared(station.transform.position, point);
				float range = GetCameraStationRange(station);
				if (distanceSquared < range * range && distanceSquared < nearestDistanceSquared)
				{
					closest = station;
					nearestDistanceSquared = distanceSquared;
				}
			}

			if (closest) CameraStationsByName[name] = closest!;
			else CameraStationsByName.Remove(name);
			return closest;
		}

		private static bool IsCameraStationInRange(CraftingStation station, string name, Vector3 point)
		{
			if (!station || station.m_name != name) return false;
			float range = GetCameraStationRange(station);
			return HorizontalDistanceSquared(station.transform.position, point) < range * range;
		}

		private static float GetCameraStationRange(CraftingStation station)
		{
			return Mathf.Max(station.GetStationBuildRange(), Plugin.distanceCanBuildFromWorkbench.Value);
		}

		private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
		{
			float x = first.x - second.x;
			float z = first.z - second.z;
			return x * x + z * z;
		}

		private static Vector3 ClampToStationRange(Vector3 position)
		{
			if (!_cameraStation)
			{
				DisableBuildMode();
				return position;
			}

			Vector3 stationPosition = _cameraStation!.transform.position;
			float limit = GetCameraStationRange(_cameraStation) * Plugin.cameraRangeMultiplier.Value;
			Vector3 offset = position - stationPosition;
			float distanceSquared = offset.sqrMagnitude;
			return distanceSquared <= limit * limit ? position : stationPosition + offset * (limit / Mathf.Sqrt(distanceSquared));
		}

		private static Vector3 ClampToTerrain(Vector3 currentPosition, Vector3 wantedPosition)
		{
			float clearance = Plugin.cameraTerrainClearance.Value;
			Vector3 movement = wantedPosition - currentPosition;
			float distanceSquared = movement.sqrMagnitude;
			if (distanceSquared <= 0.000001f) return currentPosition;

			// Build mode can start with the camera already inside the clearance (a slope behind you).
			// Every move from there used to be rejected, freezing the camera for good. Let it move as
			// long as it doesn't get closer to the ground (Cartur's 1.0.1 fix).
			if (Physics.CheckSphere(currentPosition, clearance, TerrainMask, QueryTriggerInteraction.Ignore))
			{
				ZoneSystem zoneSystem = ZoneSystem.instance;
				if (!zoneSystem || wantedPosition.y - zoneSystem.GetGroundHeight(wantedPosition) >= currentPosition.y - zoneSystem.GetGroundHeight(currentPosition))
					return wantedPosition;
				return currentPosition;
			}

			float distance = Mathf.Sqrt(distanceSquared);
			Vector3 direction = movement / distance;
			if (Physics.SphereCast(currentPosition, clearance, direction, out RaycastHit hit, distance, TerrainMask, QueryTriggerInteraction.Ignore))
				wantedPosition = currentPosition + direction * Mathf.Max(0f, hit.distance - 0.02f);

			if (Physics.CheckSphere(wantedPosition, clearance, TerrainMask, QueryTriggerInteraction.Ignore))
			{
				if (_hasLastSafeCameraPosition && !Physics.CheckSphere(_lastSafeCameraPosition, clearance, TerrainMask, QueryTriggerInteraction.Ignore))
					return _lastSafeCameraPosition;
				return currentPosition;
			}

			_lastSafeCameraPosition = wantedPosition;
			_hasLastSafeCameraPosition = true;
			return wantedPosition;
		}

		private static Quaternion UpdateBuildCameraViewDirection(float dt)
		{
			float mouseHorizontalPolarity = Plugin.invertMouseLookHorizontal.Value == Plugin.Toggle.On ? -1f : 1f;
			float controllerHorizontalPolarity = Plugin.invertControllerLookHorizontal.Value == Plugin.Toggle.On ? -1f : 1f;
			Plugin.CameraYaw += mouseHorizontalPolarity * PlayerController.m_mouseSens * Input.GetAxis("Mouse X") + controllerHorizontalPolarity * ZInput.GetJoyRightStickX() * 110f * dt;

			float vanillaMousePolarity = PlayerController.m_invertMouse ? -1f : 1f;
			float mouseVerticalPolarity = Plugin.invertMouseLookVertical.Value == Plugin.Toggle.On ? -vanillaMousePolarity : vanillaMousePolarity;
			float controllerVerticalPolarity = Plugin.invertControllerLookVertical.Value == Plugin.Toggle.On ? -1f : 1f;
			float pitch = Plugin.CameraPitch - (mouseVerticalPolarity * PlayerController.m_mouseSens * Input.GetAxis("Mouse Y") - controllerVerticalPolarity * ZInput.GetJoyRightStickY() * 110f * dt);
			Plugin.CameraPitch = Mathf.Clamp(pitch, -89f, 89f);
			return Quaternion.Euler(0f, Plugin.CameraYaw, 0f) * Quaternion.Euler(Plugin.CameraPitch, 0f, 0f);
		}

		private static Vector3 GetMovementInput(float dt)
		{
			Vector3 movement = Vector3.zero;
			if (ZInput.GetButton("Left")) movement -= Vector3.right;
			if (ZInput.GetButton("Right")) movement += Vector3.right;
			if (ZInput.GetButton("Forward")) movement += Vector3.forward;
			if (ZInput.GetButton("Backward")) movement -= Vector3.forward;

			ref float takeInputDelay = ref Access.TakeInputDelay;
			takeInputDelay = Mathf.Max(0f, takeInputDelay - dt);
			if (ZInput.GetButton("Jump") || ZInput.GetButton("JoyJump") && takeInputDelay <= 0f && !Hud.IsPieceSelectionVisible())
				movement += Vector3.up;
			if (ZInput.GetButton("Crouch") || ZInput.GetButtonPressedTimer("JoyCrouch") > 0.33f)
				movement -= Vector3.up;

			movement.Normalize();
			movement += Vector3.right * ZInput.GetJoyLeftStickX();
			movement -= Vector3.forward * ZInput.GetJoyLeftStickY();
			float baseSpeed = ZInput.GetButton("Run") ? Player.m_localPlayer.m_runSpeed : Player.m_localPlayer.m_walkSpeed;
			return movement * (dt * baseSpeed * Plugin.cameraMoveSpeedMultiplier.Value);
		}

		internal static void UpdateBuildCamera(float dt, GameCamera camera)
		{
			if (Console.IsVisible() || !Access.TakeInput(Player.m_localPlayer) || Hud.IsPieceSelectionVisible()) return;
			Vector3 movement = GetMovementInput(dt);
			Transform cameraTransform = camera.transform;
			if (Plugin.moveWithRespectToWorld.Value != Plugin.Toggle.On)
				movement = cameraTransform.TransformVector(movement);
			Vector3 wantedPosition = ClampToStationRange(cameraTransform.position + movement);
			cameraTransform.position = ClampToTerrain(cameraTransform.position, wantedPosition);
			cameraTransform.rotation = UpdateBuildCameraViewDirection(dt);
		}

		private static bool HasRequiredComfort()
		{
			Player player = Player.m_localPlayer;
			if (!player) return false;
			if (player == _comfortPlayer && Time.time < _nextComfortRefresh) return _hasRequiredComfort;

			_comfortPlayer = player;
			_nextComfortRefresh = Time.time + ComfortRefreshInterval;
			_hasRequiredComfort = CalculateComfort(player);
			return _hasRequiredComfort;
		}

		private static bool CalculateComfort(Player player)
		{
			if (player.GetComfortLevel() < Plugin.minimumComfortLevel.Value) return false;
			SEMan statusEffects = player.GetSEMan();
			if (statusEffects == null) return false;
			if (statusEffects.HaveStatusEffect(SEMan.s_statusEffectResting)) return true;

			bool nearFire = statusEffects.HaveStatusEffect(SEMan.s_statusEffectCampFire);
			bool shelteredOrSitting = player.InShelter() || player.IsSitting();
			bool insideWarmArea = EffectArea.IsPointInsideArea(player.transform.position, EffectArea.Type.WarmCozyArea, 1f);
			bool wet = statusEffects.HaveStatusEffect(SEMan.s_statusEffectWet) && !insideWarmArea;
			bool cold = statusEffects.HaveStatusEffect(SEMan.s_statusEffectCold) || statusEffects.HaveStatusEffect(SEMan.s_statusEffectFreezing);
			return nearFire && shelteredOrSitting && !player.IsSensed() && !wet && !cold && !statusEffects.HaveStatusEffect(SEMan.s_statusEffectBurning);
		}

		internal static bool CanUseCamera()
		{
			return Plugin.restrictionMode.Value != Plugin.RestrictionMode.CameraNeedsCoziness || HasRequiredComfort();
		}

		internal static bool CanPickUpFromCamera()
		{
			return Plugin.restrictionMode.Value != Plugin.RestrictionMode.CameraPickUpNeedsCoziness || HasRequiredComfort();
		}

		internal static void ShowComfortMessage()
		{
			if (!Player.m_localPlayer || Time.time < _nextComfortMessageTime) return;
			_nextComfortMessageTime = Time.time + 1.5f;
			string text = Localization.instance.Localize("$shackmagiccam_needs_cozy", Plugin.minimumComfortLevel.Value.ToString());
			Player.m_localPlayer.Message(MessageHud.MessageType.Center, text);
		}

		internal static bool ShouldShowPickupWarning()
		{
			return InBuildMode() && !CanPickUpFromCamera() && HasNearbyPickup();
		}

		private static bool HasNearbyPickup()
		{
			if (!GameCamera.instance || !Player.m_localPlayer) return false;
			Vector3 pickupCenter = GameCamera.instance.transform.position + Vector3.up;
			int count = Physics.OverlapSphereNonAlloc(pickupCenter, Plugin.resourcePickupRange.Value, NearbyPickupColliders, Access.AutoPickupMask(Player.m_localPlayer));
			try
			{
				for (int i = 0; i < count; ++i)
				{
					if (TryFindItemDrop(NearbyPickupColliders[i], out ItemDrop item) && item.m_autoPickup && !item.IsPiece())
						return true;
				}

				return false;
			}
			finally
			{
				Array.Clear(NearbyPickupColliders, 0, count);
			}
		}

		internal static bool TryGetAutoPickupPlayer(out Player player)
		{
			player = Player.m_localPlayer;
			return player && !player.IsTeleporting() && Access.EnableAutoPickup && CanPickUpFromCamera();
		}

		internal static void AutoPickup(GameCamera camera, Player player)
		{
			Vector3 pickupCenter = camera.transform.position + Vector3.up;
			int count = Physics.OverlapSphereNonAlloc(pickupCenter, Plugin.resourcePickupRange.Value, NearbyPickupColliders, Access.AutoPickupMask(player));
			NearbyPickupItemIds.Clear();
			Inventory inventory = player.GetInventory();
			try
			{
				for (int i = 0; i < count; ++i)
				{
					if (!TryFindItemDrop(NearbyPickupColliders[i], out ItemDrop item) || !item.m_autoPickup || item.IsPiece() || player.HaveUniqueKey(item.m_itemData.m_shared.m_name)) continue;
					if (!NearbyPickupItemIds.Add(item.GetInstanceID())) continue;
					ZNetView netView = Access.ItemNetView(item);
					if (!netView || !netView.IsValid()) continue;
					if (!item.CanPickup())
					{
						item.RequestOwn();
						continue;
					}

					if (item.InTar()) continue;
					item.Load();
					if (!inventory.CanAddItem(item.m_itemData) || item.m_itemData.GetWeight() + inventory.GetTotalWeight() > player.GetMaxCarryWeight()) continue;
					player.Pickup(item.gameObject);
				}
			}
			finally
			{
				Array.Clear(NearbyPickupColliders, 0, count);
				NearbyPickupItemIds.Clear();
			}
		}

		private static bool TryFindItemDrop(Collider collider, out ItemDrop item)
		{
			item = null!;
			if (!collider || !collider.attachedRigidbody) return false;
			Rigidbody rigidbody = collider.attachedRigidbody;
			item = rigidbody.GetComponent<ItemDrop>();
			if (item) return true;
			FloatingTerrainDummy floatingTerrain = rigidbody.GetComponent<FloatingTerrainDummy>();
			if (!floatingTerrain || !floatingTerrain.m_parent) return false;
			item = floatingTerrain.m_parent.GetComponent<ItemDrop>();
			return item;
		}

		// Prefixed with the mod name so the keys can't collide with Cartur's or Azumatt's if one of
		// them is still in the profile.
		internal static void AddLocalizations(Localization localization)
		{
			Access.AddWord(localization, "shackmagiccam_needs_cozy", "Be cozy to use the build camera (requires comfort $1).");
			Access.AddWord(localization, "shackmagiccam_pickup_blocked_title", "Build Camera Pickup");
			Access.AddWord(localization, "shackmagiccam_pickup_blocked", "Get cozy before collecting items through the build camera.\nRequired comfort: $1");
		}
	}
}
