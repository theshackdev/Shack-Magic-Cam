// Shack Magic Cam: The Shack's build camera. Detach the camera with Y while holding a hammer, hoe or
// cultivator near a workbench, then fly it around and build without climbing on things.
//
// A fork of Cartur's Build Camera (github.com/jekkle/cartur-build-camera, GPL-3.0), which forked
// Azumatt's Build Camera Custom Hammers Edition, which forked gittywithexcitement's / CookieMilk's
// Valheim Build Camera. GPL-3.0 like all of them; see LICENSE.txt.
//
// Why it exists: Cartur's Build Camera 1.1.1 was pulled from the Thunderstore listing in October 2026,
// so new installs of The Shack Pack quietly went without it. This is that mod's behaviour as of 1.1.1,
// rebuilt from its 1.0.0 source plus the 1.0.1 - 1.1.1 fixes, with these changes:
//   - Jotunn does the config sync and version check instead of a bundled ServerSync and a hand-rolled
//     handshake. A server with this mod never turns away players who don't have it.
//   - The defaults are The Shack's settings: Y toggles, 2 m pickup (vanilla), 1x demister (vanilla).
//   - If Cartur's Build Camera or Azumatt's original is also installed, this mod switches their patches
//     off and runs the camera itself, so a profile that still has the old mod keeps working.
using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace TheShack.MagicCam
{
	[BepInPlugin(ModGUID, ModName, ModVersion)]
	[BepInDependency(Jotunn.Main.ModGuid)]
	[NetworkCompatibility(CompatibilityLevel.VersionCheckOnly, VersionStrictness.Minor)]
	[SynchronizationMode(AdminOnlyStrictness.IfOnServer)]
	public class Plugin : BaseUnityPlugin
	{
		internal const string ModName = "Shack Magic Cam";
		internal const string ModVersion = "1.0.0";
		private const string ModGUID = "theshack.magiccam";

		/// The build cameras this one replaces. Both take over GameCamera.UpdateCamera, so two at once
		/// would fight over one transform and both toggle on the same key.
		private static readonly string[] ReplacedGUIDs =
		{
			"com.jekkle.valheim.carturbuildcamera", // Cartur's Build Camera
			"Azumatt.BuildCameraCHE",               // Azumatt's Build Camera Custom Hammers Edition
		};

		private readonly Harmony _harmony = new(ModGUID);
		private FileSystemWatcher? _configWatcher;
		private static readonly string ConfigFileName = ModGUID + ".cfg";
		private static readonly string ConfigFileFullPath = BepInEx.Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
		public static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource(ModName);

		/// Set when patching failed, so Update() stands down too.
		private bool _standDown;

		internal static bool BuildCameraActive;

		internal static float CameraYaw;
		internal static float CameraPitch;

		public enum Toggle
		{
			On = 1,
			Off = 0,
		}

		public enum RestrictionMode
		{
			Off,
			CameraNeedsCoziness,
			CameraPickUpNeedsCoziness,
		}

		public enum CameraLightShadows
		{
			Original,
			Off,
			Hard,
			Soft,
		}

		void Awake()
		{
			distanceCanBuildFromAvatar = config("General", "Distance Can Build From Avatar", 100f, new ConfigDescription("Distance from your avatar that you can build or repair. (Valheim default is 8)", new AcceptableValueRange<float>(1f, 500f)));
			distanceCanBuildFromWorkbench = config("General", "Distance Can Build From Workbench", 100f, new ConfigDescription("Maximum distance between the build camera and its workbench/stonecutter/etc. This does not change the station's actual build or player-base radius.", new AcceptableValueRange<float>(1f, 500f)));
			resourcePickupRange = config("General", "Resource Pickup Range", 2f, new ConfigDescription("Distance from which you can pick up resources on the ground while in build mode. (Valheim default is 2)", new AcceptableValueRange<float>(0.5f, 100f)));
			cameraRangeMultiplier = config("General", "Camera Range Multiplier", 1f, new ConfigDescription("Changes maximum range camera can move away from the build station. 1 means the build station's range, 2 means twice the build station range, etc.", new AcceptableValueRange<float>(0.1f, 10f)));
			cameraMoveSpeedMultiplier = config("General", "Camera Move Speed Multiplier", 3f, new ConfigDescription("Multiplies the speed at which the build camera pans (i.e. moves around).", new AcceptableValueRange<float>(0.1f, 20f)));
			cameraTerrainClearance = config("General", "Camera Terrain Clearance", 1f, new ConfigDescription("Radius kept clear around the detached camera so it cannot cross terrain or a HearthBelow cave wall.", new AcceptableValueRange<float>(0.1f, 2f)));
			restrictionMode = config("General", "Restriction Mode", RestrictionMode.CameraPickUpNeedsCoziness, "Off: no cozy restriction. CameraNeedsCoziness: cozy is required to enter and stay in camera mode. CameraPickUpNeedsCoziness: camera entry is allowed, but camera pickup requires cozy.");
			minimumComfortLevel = config("General", "Minimum Comfort Level", 1, new ConfigDescription("Minimum comfort required by the selected restriction mode.", new AcceptableValueRange<int>(1, 30)));
			moveWithRespectToWorld = config("General", "Move With Respect To World", Toggle.Off,
				"When true, camera panning input (e.g. pressing WASD) moves the camera with respect to the world coordinates. " +
				"This means that turning the camera has no effect on the direction of movement. For example, pressing W will always move the camera toward the world's 'North', " +
				"as opposed to the direction the camera is currently facing.");
			toggleBuildMode = config("Hotkeys", "Toggle build mode", new KeyboardShortcut(KeyCode.Y), "See https://docs.unity3d.com/ScriptReference/KeyCode.html for the names of all key codes. To add one or more modifier keys, separate them with +, like so: Toggle build mode = Y + LeftControl", false);
			verboseLogging = config("General", "Verbose Logging", Toggle.Off, "When true, increases verbosity of logging. Enable this if you're wondering why you're unable to enable the build camera.", false);
			demisterFollowCamera = config("Mist", "Demister Follow Camera", Toggle.On, "When enabled and you have the Demister status effect (Wisplight), the demister ball will follow the build camera instead of your character while in build mode.");
			demisterRangeMultiplier = config("Mist", "Demister Range Multiplier", 1f, new ConfigDescription("Multiplier for the demister's mist-clearing range while in build mode. Higher values clear more mist. (Vanilla: 1.0x)", new AcceptableValueRange<float>(0.1f, 10f)));
			followDvergrCircletLight = config("Helmet Light", "Follow Dvergr Circlet Light", Toggle.On, "Moves the equipped Dvergr circlet light to the detached camera. Also supports CircletExtended and RaziCirclet custom slots when installed.");
			helmetLightOffsetForward = config("Helmet Light", "Camera Light Forward/Back Offset", 0.65f, new ConfigDescription("Circlet light offset on the camera's forward axis.", new AcceptableValueRange<float>(-5f, 5f)), false);
			helmetLightOffsetUp = config("Helmet Light", "Camera Light Up/Down Offset", -0.08f, new ConfigDescription("Circlet light offset on the camera's up axis.", new AcceptableValueRange<float>(-5f, 5f)), false);
			helmetLightOffsetRight = config("Helmet Light", "Camera Light Left/Right Offset", 0f, new ConfigDescription("Circlet light offset on the camera's right axis. Negative values move it left.", new AcceptableValueRange<float>(-5f, 5f)), false);
			helmetLightIntensityMultiplier = config("Helmet Light", "Camera Light Intensity Multiplier", 1f, new ConfigDescription("Multiplies the equipped circlet light's current intensity.", new AcceptableValueRange<float>(0f, 5f)));
			helmetLightRangeMultiplier = config("Helmet Light", "Camera Light Range Multiplier", 1f, new ConfigDescription("Multiplies the equipped circlet light's current range.", new AcceptableValueRange<float>(0f, 5f)));
			helmetLightColor = config("Helmet Light", "Camera Light Color", "Original", "Original uses the equipped circlet light color. Otherwise use a hex color such as FFD27F or FFD27FFF.");
			helmetLightShadows = config("Helmet Light", "Camera Light Shadows", CameraLightShadows.Original, "Original uses the equipped circlet light's shadow mode. Off, Hard, and Soft override it.");
			pickupHudX = config("HUD", "Pickup Blocked HUD X", 0.3f, new ConfigDescription("Horizontal pickup-blocked message position as a screen ratio.", new AcceptableValueRange<float>(0f, 1f)), false);
			pickupHudY = config("HUD", "Pickup Blocked HUD Y", 0.3f, new ConfigDescription("Vertical pickup-blocked message position (1 = top, 0 = bottom).", new AcceptableValueRange<float>(0f, 1f)), false);
			pickupHudFontSize = config("HUD", "Pickup Blocked HUD Font Size", 19, new ConfigDescription("Pickup-blocked message body size. The font and panel styling come from Valheim's HUD.", new AcceptableValueRange<int>(12, 32)), false);
			invertControllerLookHorizontal = config("Controls", "Invert Controller Look Horizontal", Toggle.Off, "Inverts the horizontal (left/right) look axis for the controller right stick while in build camera mode.", false);
			invertControllerLookVertical = config("Controls", "Invert Controller Look Vertical", Toggle.Off, "Inverts the vertical (up/down) look axis for the controller right stick while in build camera mode.", false);
			invertMouseLookHorizontal = config("Controls", "Invert Mouse Look Horizontal", Toggle.Off, "Inverts the horizontal (left/right) look axis for the mouse while in build camera mode.", false);
			invertMouseLookVertical = config("Controls", "Invert Mouse Look Vertical", Toggle.Off, "Inverts the vertical (up/down) look axis for the mouse while in build camera mode. This is separate from the game's built-in mouse invert setting.", false);

			// A mod that throws in Awake takes the whole chainloader down with it, so the game would
			// not start at all. Log and let everything else load.
			try
			{
				_harmony.PatchAll(Assembly.GetExecutingAssembly());
			}
			catch (Exception ex)
			{
				_standDown = true;
				Log.LogWarning($"Patching failed, build camera disabled: {ex.Message}");
				return;
			}

			Utils.AddLocalizations(Localization.instance);
			SetupWatcher();
			Log.LogInfo($"{ModName} {ModVersion} ready (toggle: {toggleBuildMode.Value}).");
		}

		/// Runs after every plugin's Awake, so the old build cameras have patched by now and can be
		/// unpatched. Their own Update would still run the pickup HUD, so their component is disabled too.
		private void Start()
		{
			if (_standDown) return;
			foreach (string guid in ReplacedGUIDs)
			{
				if (!Chainloader.PluginInfos.TryGetValue(guid, out BepInEx.PluginInfo other)) continue;
				Harmony.UnpatchID(guid);
				if (other.Instance) other.Instance.enabled = false;
				Log.LogWarning($"{other.Metadata.Name} {other.Metadata.Version} is also installed - turned its patches off, {ModName} runs the build camera. Remove it to silence this.");
			}
		}

		private void Update()
		{
			if (_standDown) return;
			PickupBlockedHud.Update();
		}

		private void OnDestroy()
		{
			if (_configWatcher != null)
			{
				_configWatcher.EnableRaisingEvents = false;
				_configWatcher.Changed -= ReadConfigValues;
				_configWatcher.Created -= ReadConfigValues;
				_configWatcher.Renamed -= ReadConfigValues;
				_configWatcher.Dispose();
				_configWatcher = null;
			}
			PickupBlockedHud.Cleanup();
			Utils.DisableBuildMode();
			_harmony.UnpatchSelf();
			Config.Save();
		}

		private void SetupWatcher()
		{
			_configWatcher = new FileSystemWatcher(BepInEx.Paths.ConfigPath, ConfigFileName);
			_configWatcher.Changed += ReadConfigValues;
			_configWatcher.Created += ReadConfigValues;
			_configWatcher.Renamed += ReadConfigValues;
			_configWatcher.IncludeSubdirectories = false;
			_configWatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
			_configWatcher.EnableRaisingEvents = true;
		}

		private void ReadConfigValues(object sender, FileSystemEventArgs e)
		{
			if (!File.Exists(ConfigFileFullPath)) return;
			try
			{
				Log.LogDebug("ReadConfigValues called");
				Config.Reload();
			}
			catch
			{
				Log.LogError($"There was an issue loading your {ConfigFileName}");
				Log.LogError("Please check your config entries for spelling and format!");
			}
		}

		#region ConfigOptions

		internal static ConfigEntry<float> distanceCanBuildFromAvatar = null!;
		internal static ConfigEntry<float> distanceCanBuildFromWorkbench = null!;
		internal static ConfigEntry<float> resourcePickupRange = null!;
		internal static ConfigEntry<float> cameraRangeMultiplier = null!;
		internal static ConfigEntry<float> cameraMoveSpeedMultiplier = null!;
		internal static ConfigEntry<float> cameraTerrainClearance = null!;
		internal static ConfigEntry<RestrictionMode> restrictionMode = null!;
		internal static ConfigEntry<int> minimumComfortLevel = null!;
		internal static ConfigEntry<Toggle> moveWithRespectToWorld = null!;
		internal static ConfigEntry<KeyboardShortcut> toggleBuildMode = null!;
		internal static ConfigEntry<Toggle> verboseLogging = null!;
		internal static ConfigEntry<Toggle> demisterFollowCamera = null!;
		internal static ConfigEntry<float> demisterRangeMultiplier = null!;
		internal static ConfigEntry<Toggle> followDvergrCircletLight = null!;
		internal static ConfigEntry<float> helmetLightOffsetForward = null!;
		internal static ConfigEntry<float> helmetLightOffsetUp = null!;
		internal static ConfigEntry<float> helmetLightOffsetRight = null!;
		internal static ConfigEntry<float> helmetLightIntensityMultiplier = null!;
		internal static ConfigEntry<float> helmetLightRangeMultiplier = null!;
		internal static ConfigEntry<string> helmetLightColor = null!;
		internal static ConfigEntry<CameraLightShadows> helmetLightShadows = null!;
		internal static ConfigEntry<float> pickupHudX = null!;
		internal static ConfigEntry<float> pickupHudY = null!;
		internal static ConfigEntry<int> pickupHudFontSize = null!;
		internal static ConfigEntry<Toggle> invertControllerLookHorizontal = null!;
		internal static ConfigEntry<Toggle> invertControllerLookVertical = null!;
		internal static ConfigEntry<Toggle> invertMouseLookHorizontal = null!;
		internal static ConfigEntry<Toggle> invertMouseLookVertical = null!;

		/// Synced settings are admin-only: when the server has this mod, Jotunn sends the server's
		/// values to every player and only admins can change them. The rest are each player's own.
		private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
		{
			ConfigDescription extendedDescription = new(
				description.Description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
				description.AcceptableValues,
				new ConfigurationManagerAttributes { IsAdminOnly = synchronizedSetting });
			return Config.Bind(group, name, value, extendedDescription);
		}

		private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
		{
			return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
		}

		#endregion
	}
}
