using System;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace TheShack.MagicCam.Compatibility.WardIsLove;

internal static class WardAccess
{
	private const string PluginGuid = "Azumatt.WardIsLove";
	private static readonly System.Version MinimumVersion = new System.Version(2, 3, 3);

	private delegate bool CheckAccessHandler(long playerId, Vector3 point, float radius, bool flash);

	private static readonly bool WardIsLoveLoaded = Chainloader.PluginInfos.TryGetValue(PluginGuid, out BepInEx.PluginInfo plugin) && plugin.Metadata.Version.CompareTo(MinimumVersion) >= 0;
	private static readonly CheckAccessHandler? WardIsLoveCheck = WardIsLoveLoaded ? ResolveWardIsLoveCheck() : null;

	private static bool _warnedAboutMissingCheck;

	internal static bool Check(long playerId, Vector3 point)
	{
		if (WardIsLoveCheck != null) return WardIsLoveCheck(playerId, point, 0f, false);

		// Upstream returned false here, which silently killed every camera pickup with no log
		// line whenever WardIsLove was installed but its CheckAccess could not be resolved -
		// a rename on their side turned this mod's pickup feature off with no way to tell.
		// Vanilla's own ward check is the correct answer when their API is unavailable.
		if (WardIsLoveLoaded && !_warnedAboutMissingCheck)
		{
			_warnedAboutMissingCheck = true;
			Plugin.Log.LogWarning(
				$"{PluginGuid} is loaded but WardIsLove.Util.CustomCheck.CheckAccess could not be resolved - falling back to the vanilla ward check.");
		}

		return PrivateArea.CheckAccess(point, flash: false, wardCheck: true);
	}

	private static CheckAccessHandler? ResolveWardIsLoveCheck()
	{
		try
		{
			Type? type = Type.GetType("WardIsLove.Util.CustomCheck, WardIsLove", false);
			MethodInfo? method = type?.GetMethod("CheckAccess", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(long), typeof(Vector3), typeof(float), typeof(bool) }, null);
			return method == null ? null : (CheckAccessHandler)Delegate.CreateDelegate(typeof(CheckAccessHandler), method);
		}
		catch
		{
			return null;
		}
	}
}
