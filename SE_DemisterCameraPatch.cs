using HarmonyLib;
using UnityEngine;

namespace TheShack.MagicCam
{
	[HarmonyPatch(typeof(SE_Demister), nameof(SE_Demister.UpdateStatusEffect))]
	public static class SE_DemisterCameraPatch
	{
		private static GameObject _trackedBall = null!;
		private static ParticleSystemForceField _trackedForceField = null!;
		private static float _originalForceFieldRange;

		public static bool Prefix(SE_Demister __instance, float dt)
		{
			Player? player = __instance.m_character as Player;
			GameCamera camera = GameCamera.instance;
			if (!player || !camera || !Utils.IsLocalPlayer(player!) || !Utils.InBuildMode() || Plugin.demisterFollowCamera.Value != Plugin.Toggle.On)
			{
				RestoreNormalForceField();
				return true;
			}

			// Skipping the original also skipped StatusEffect.UpdateStatusEffect's bookkeeping, so the
			// effect stopped ageing while the camera was out. Do that part here (Cartur's 1.1.0 fix).
			AgeStatusEffect(__instance, dt);
			UpdateForceFieldRange(__instance);
			UpdateDemisterForCamera(__instance, player!, camera.transform, dt);

			return false;
		}

		private static void AgeStatusEffect(StatusEffect effect, float dt)
		{
			Access.StatusTime(effect) += dt;
			if (effect.m_repeatInterval <= 0f || string.IsNullOrEmpty(effect.m_repeatMessage)) return;
			ref float messageTimer = ref Access.StatusMessageTimer(effect);
			messageTimer += dt;
			if (messageTimer > effect.m_repeatInterval)
			{
				messageTimer = 0f;
				effect.m_character.Message(effect.m_repeatMessageType, effect.m_repeatMessage);
			}
		}

		private static void UpdateForceFieldRange(SE_Demister instance)
		{
			GameObject ball = Access.BallInstance(instance);
			if (!ball) return;

			if (_trackedBall != ball || !_trackedForceField)
			{
				RestoreNormalForceField();
				ParticleSystemForceField forceField = ball.GetComponentInChildren<ParticleSystemForceField>();
				if (!forceField) return;
				_trackedBall = ball;
				_trackedForceField = forceField;
				_originalForceFieldRange = forceField.endRange;
			}

			_trackedForceField.endRange = _originalForceFieldRange * Plugin.demisterRangeMultiplier.Value;
		}

		private static void RestoreNormalForceField()
		{
			if (_trackedForceField) _trackedForceField.endRange = _originalForceFieldRange;
			_trackedBall = null!;
			_trackedForceField = null!;
			_originalForceFieldRange = 0f;
		}

		private static void UpdateDemisterForCamera(SE_Demister instance, Player player, Transform camera, float dt)
		{
			Vector3 cameraPosition = camera.position;
			Vector3 cameraForward = camera.forward;
			ref GameObject ballInstance = ref Access.BallInstance(instance);
			ref Vector3 ballVelocity = ref Access.BallVelocity(instance);

			if (!ballInstance)
			{
				ballInstance = Object.Instantiate(instance.m_ballPrefab, cameraPosition + cameraForward * 0.5f, Quaternion.identity);
				return;
			}

			bool isUnderRoof = IsUnderRoof(instance, cameraPosition);
			Transform ballTransform = ballInstance.transform;
			Vector3 ballPosition = ballTransform.position;
			Vector3 offset = isUnderRoof ? instance.m_offsetInterior : instance.m_offset;
			float noiseDistance = isUnderRoof ? instance.m_noiseDistanceInterior : instance.m_noiseDistance;
			Vector3 transformedOffset = camera.right * offset.x + camera.up * offset.y + cameraForward * offset.z;
			Vector3 targetPosition = cameraPosition + transformedOffset;
			float time = Time.time * instance.m_noiseSpeed;
			Vector3 noiseOffset = new Vector3(Mathf.Sin(time * 4f), Mathf.Sin(time * 2f) * instance.m_noiseDistanceYScale, Mathf.Cos(time * 5f)) * noiseDistance;
			Vector3 targetWithNoise = targetPosition + noiseOffset;
			Vector3 ballToTarget = targetWithNoise - ballPosition;
			float distanceSquared = ballToTarget.sqrMagnitude;
			float maxDistanceSquared = instance.m_maxDistance * instance.m_maxDistance;

			if (distanceSquared > maxDistanceSquared * 4f)
			{
				ballPosition = targetWithNoise;
			}
			else if (distanceSquared > maxDistanceSquared)
			{
				ballPosition = targetWithNoise - ballToTarget * (instance.m_maxDistance / Mathf.Sqrt(distanceSquared));
			}

			ballToTarget = targetWithNoise - ballPosition;
			float distanceToTargetSquared = ballToTarget.sqrMagnitude;
			if (distanceToTargetSquared > 0.000001f)
				ballVelocity += ballToTarget * (instance.m_ballAcceleration * dt / Mathf.Sqrt(distanceToTargetSquared));

			float speedSquared = ballVelocity.sqrMagnitude;
			float maxSpeedSquared = instance.m_ballMaxSpeed * instance.m_ballMaxSpeed;
			if (speedSquared > maxSpeedSquared)
			{
				ballVelocity *= instance.m_ballMaxSpeed / Mathf.Sqrt(speedSquared);
			}

			if (!isUnderRoof)
			{
				ballVelocity += player.GetVelocity() * instance.m_characterVelocityFactor * dt;
			}

			ballVelocity -= ballVelocity * instance.m_ballFriction;

			ballTransform.position = ballPosition + ballVelocity * dt;
			ballTransform.rotation *= Quaternion.Euler(instance.m_rotationSpeed, 0.0f, instance.m_rotationSpeed * 0.5321f);
		}

		private static bool IsUnderRoof(SE_Demister instance, Vector3 cameraPosition)
		{
			return Physics.Raycast(cameraPosition, Vector3.up, out RaycastHit _, 4f, Access.CoverRayMask(instance));
		}

		internal static void CleanupBall(GameObject ball)
		{
			if (_trackedBall == ball) RestoreNormalForceField();
		}

		internal static void Cleanup()
		{
			RestoreNormalForceField();
		}
	}

	[HarmonyPatch(typeof(SE_Demister), "RemoveEffects")]
	public static class SE_DemisterCleanupPatch
	{
		[HarmonyPrefix]
		public static void RemoveEffects_Prefix(SE_Demister __instance)
		{
			SE_DemisterCameraPatch.CleanupBall(Access.BallInstance(__instance));
		}
	}
}
