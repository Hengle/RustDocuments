using ConVar;
using Oxide.Core;
using UnityEngine;

public class AttackEntity : HeldEntity
{
	[Header("Attack Entity")]
	public float deployDelay = 1f;

	public float repeatDelay = 0.5f;

	public float animationDelay;

	[Header("NPCUsage")]
	public float effectiveRange = 1f;

	public float npcDamageScale = 1f;

	public float attackLengthMin = -1f;

	public float attackLengthMax = -1f;

	public float attackSpacing;

	public float aiAimSwayOffset;

	public float aiAimCone;

	public bool aiOnlyInRange;

	public float CloseRangeAddition;

	public float MediumRangeAddition;

	public float LongRangeAddition;

	public bool CanUseAtMediumRange = true;

	public bool CanUseAtLongRange = true;

	public SoundDefinition[] reloadSounds;

	public SoundDefinition thirdPersonMeleeSound;

	[Header("Recoil Compensation")]
	public float recoilCompDelayOverride;

	public bool wantsRecoilComp;

	public float nextAttackTime = float.NegativeInfinity;

	protected bool UsingInfiniteAmmoCheat
	{
		get
		{
			BasePlayer ownerPlayer = GetOwnerPlayer();
			if (ownerPlayer == null || (!ownerPlayer.IsAdmin && !ownerPlayer.IsDeveloper))
			{
				return false;
			}
			return ownerPlayer.GetInfoBool("player.infiniteammo", defaultVal: false);
		}
	}

	public float NextAttackTime => nextAttackTime;

	public virtual Vector3 GetInheritedVelocity(BasePlayer player, Vector3 direction)
	{
		return Vector3.zero;
	}

	public virtual float AmmoFraction()
	{
		return 0f;
	}

	public virtual bool CanReload()
	{
		return false;
	}

	public virtual bool ServerIsReloading()
	{
		return false;
	}

	public virtual void ServerReload()
	{
	}

	public virtual bool ServerTryReload(IAmmoContainer ammoSource)
	{
		return true;
	}

	public virtual void TopUpAmmo()
	{
	}

	public virtual Vector3 ModifyAIAim(Vector3 eulerInput, float swayModifier = 1f)
	{
		return eulerInput;
	}

	public virtual void GetAttackStats(HitInfo info)
	{
	}

	public void StartAttackCooldownRaw(float cooldown)
	{
		nextAttackTime = UnityEngine.Time.time + cooldown;
	}

	public void StartAttackCooldown(float cooldown)
	{
		nextAttackTime = CalculateCooldownTime(nextAttackTime, cooldown, catchup: true);
	}

	public void ResetAttackCooldown()
	{
		nextAttackTime = float.NegativeInfinity;
	}

	public bool HasAttackCooldown()
	{
		return UnityEngine.Time.time < nextAttackTime;
	}

	protected float GetAttackCooldown()
	{
		return Mathf.Max(nextAttackTime - UnityEngine.Time.time, 0f);
	}

	protected float GetAttackIdle()
	{
		return Mathf.Max(UnityEngine.Time.time - nextAttackTime, 0f);
	}

	protected float CalculateCooldownTime(float nextTime, float cooldown, bool catchup)
	{
		float time = UnityEngine.Time.time;
		float num = 0f;
		if (base.isServer)
		{
			BasePlayer ownerPlayer = GetOwnerPlayer();
			num += 0.1f;
			num += cooldown * 0.1f;
			num += (ownerPlayer ? ownerPlayer.desyncTimeClamped : 0.1f);
			num += Mathf.Max(UnityEngine.Time.deltaTime, UnityEngine.Time.smoothDeltaTime);
		}
		nextTime = ((nextTime < 0f) ? Mathf.Max(0f, time + cooldown - num) : ((!(time - nextTime <= num)) ? Mathf.Max(nextTime + cooldown, time + cooldown - num) : Mathf.Min(nextTime + cooldown, time + cooldown)));
		return nextTime;
	}

	protected bool VerifyClientRPC(BasePlayer player)
	{
		if (player == null)
		{
			Debug.LogWarning("Received RPC from null player");
			return false;
		}
		BasePlayer ownerPlayer = GetOwnerPlayer();
		if (ownerPlayer == null)
		{
			AntiHack.Log(player, AntiHackType.AttackHack, "Owner not found (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "owner_missing");
			return false;
		}
		if (ownerPlayer != player)
		{
			AntiHack.Log(player, AntiHackType.AttackHack, "Player mismatch (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "player_mismatch");
			return false;
		}
		if (player.IsDead())
		{
			AntiHack.Log(player, AntiHackType.AttackHack, "Player dead (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "player_dead");
			return false;
		}
		if (player.IsWounded())
		{
			AntiHack.Log(player, AntiHackType.AttackHack, "Player down (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "player_down");
			return false;
		}
		if (player.IsSleeping())
		{
			AntiHack.Log(player, AntiHackType.AttackHack, "Player sleeping (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "player_sleeping");
			return false;
		}
		if (player.desyncTimeRaw > ConVar.AntiHack.maxdesync)
		{
			AntiHack.Log(player, AntiHackType.AttackHack, "Player stalled (" + base.ShortPrefabName + " with " + player.desyncTimeRaw + "s)");
			player.stats.combat.LogInvalid(player, this, "player_stalled");
			return false;
		}
		Item ownerItem = GetOwnerItem();
		if (ownerItem == null)
		{
			AntiHack.Log(player, AntiHackType.AttackHack, "Item not found (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "item_missing");
			return false;
		}
		if (ownerItem.isBroken)
		{
			AntiHack.Log(player, AntiHackType.AttackHack, "Item broken (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "item_broken");
			return false;
		}
		return true;
	}

	protected virtual bool VerifyClientAttack(BasePlayer player)
	{
		if (!VerifyClientRPC(player))
		{
			return false;
		}
		if (HasAttackCooldown())
		{
			AntiHack.Log(player, AntiHackType.CooldownHack, "T-" + GetAttackCooldown() + "s (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "attack_cooldown");
			return false;
		}
		return true;
	}

	protected bool ValidateEyePos(BasePlayer player, Vector3 eyePos, bool checkLineOfSight = true)
	{
		object obj = Interface.CallHook("OnEyePosValidate", this, player, eyePos, checkLineOfSight);
		if (obj is bool)
		{
			return (bool)obj;
		}
		bool flag = true;
		if (eyePos.IsNaNOrInfinity())
		{
			string shortPrefabName = base.ShortPrefabName;
			AntiHack.Log(player, AntiHackType.EyeHack, "Contains NaN (" + shortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "eye_nan");
			flag = false;
		}
		if (ConVar.AntiHack.eye_protection > 0)
		{
			float num = 1f + ConVar.AntiHack.eye_forgiveness;
			float eye_clientframes = ConVar.AntiHack.eye_clientframes;
			float eye_serverframes = ConVar.AntiHack.eye_serverframes;
			float num2 = eye_clientframes / 60f;
			float num3 = eye_serverframes * Mathx.Max(UnityEngine.Time.deltaTime, UnityEngine.Time.smoothDeltaTime, UnityEngine.Time.fixedDeltaTime);
			float num4 = (player.desyncTimeClamped + num2 + num3) * num;
			if (ConVar.AntiHack.eye_protection >= 1)
			{
				float num5 = player.MaxVelocity() + player.GetParentVelocity().magnitude;
				float num6 = player.BoundsPadding() + num4 * num5;
				float num7 = Vector3.Distance(player.eyes.position, eyePos);
				if (num7 > num6)
				{
					string shortPrefabName2 = base.ShortPrefabName;
					AntiHack.Log(player, AntiHackType.EyeHack, "Distance (" + shortPrefabName2 + " on attack with " + num7 + "m > " + num6 + "m)");
					player.stats.combat.LogInvalid(player, this, "eye_distance");
					flag = false;
				}
			}
			if (ConVar.AntiHack.eye_protection >= 3)
			{
				float num8 = Mathf.Abs(player.GetMountVelocity().y + player.GetParentVelocity().y);
				float num9 = player.BoundsPadding() + num4 * num8 + player.GetJumpHeight();
				float num10 = Mathf.Abs(player.eyes.position.y - eyePos.y);
				if (num10 > num9)
				{
					string shortPrefabName3 = base.ShortPrefabName;
					AntiHack.Log(player, AntiHackType.EyeHack, "Altitude (" + shortPrefabName3 + " on attack with " + num10 + "m > " + num9 + "m)");
					player.stats.combat.LogInvalid(player, this, "eye_altitude");
					flag = false;
				}
			}
			if (checkLineOfSight)
			{
				int num11 = 2162688;
				if (ConVar.AntiHack.eye_terraincheck)
				{
					num11 |= 0x800000;
				}
				if (ConVar.AntiHack.eye_vehiclecheck)
				{
					num11 |= 0x8000000;
				}
				if (ConVar.AntiHack.eye_protection >= 2)
				{
					Vector3 center = player.eyes.center;
					Vector3 position = player.eyes.position;
					Vector3 vector = eyePos;
					if (!GamePhysics.LineOfSightRadius(center, position, num11, ConVar.AntiHack.eye_losradius) || !GamePhysics.LineOfSightRadius(position, vector, num11, ConVar.AntiHack.eye_losradius))
					{
						string shortPrefabName4 = base.ShortPrefabName;
						string[] obj2 = new string[8] { "Line of sight (", shortPrefabName4, " on attack) ", null, null, null, null, null };
						Vector3 vector2 = center;
						obj2[3] = vector2.ToString();
						obj2[4] = " ";
						vector2 = position;
						obj2[5] = vector2.ToString();
						obj2[6] = " ";
						vector2 = vector;
						obj2[7] = vector2.ToString();
						AntiHack.Log(player, AntiHackType.EyeHack, string.Concat(obj2));
						player.stats.combat.LogInvalid(player, this, "eye_los");
						flag = false;
					}
				}
				if (ConVar.AntiHack.eye_protection >= 4 && !player.HasParent())
				{
					Vector3 position2 = player.eyes.position;
					Vector3 vector3 = eyePos;
					float num12 = Vector3.Distance(position2, vector3);
					Collider collider;
					if (num12 > ConVar.AntiHack.eye_noclip_cutoff)
					{
						if (AntiHack.TestNoClipping(position2, vector3, player.NoClipRadius(ConVar.AntiHack.eye_noclip_margin), ConVar.AntiHack.eye_noclip_backtracking, ConVar.AntiHack.noclip_protection >= 2, out collider))
						{
							string shortPrefabName5 = base.ShortPrefabName;
							string[] obj3 = new string[6] { "NoClip (", shortPrefabName5, " on attack) ", null, null, null };
							Vector3 vector2 = position2;
							obj3[3] = vector2.ToString();
							obj3[4] = " ";
							vector2 = vector3;
							obj3[5] = vector2.ToString();
							AntiHack.Log(player, AntiHackType.EyeHack, string.Concat(obj3));
							player.stats.combat.LogInvalid(player, this, "eye_noclip");
							flag = false;
						}
					}
					else if (num12 > 0.01f && AntiHack.TestNoClipping(position2, vector3, 0.01f, ConVar.AntiHack.eye_noclip_backtracking, ConVar.AntiHack.noclip_protection >= 2, out collider))
					{
						string shortPrefabName6 = base.ShortPrefabName;
						string[] obj4 = new string[6] { "NoClip (", shortPrefabName6, " on attack) ", null, null, null };
						Vector3 vector2 = position2;
						obj4[3] = vector2.ToString();
						obj4[4] = " ";
						vector2 = vector3;
						obj4[5] = vector2.ToString();
						AntiHack.Log(player, AntiHackType.EyeHack, string.Concat(obj4));
						player.stats.combat.LogInvalid(player, this, "eye_noclip");
						flag = false;
					}
				}
			}
			if (!flag)
			{
				AntiHack.AddViolation(player, AntiHackType.EyeHack, ConVar.AntiHack.eye_penalty);
			}
			else if (ConVar.AntiHack.eye_protection >= 5 && !player.HasParent() && !player.isMounted)
			{
				player.eyeHistory.PushBack(eyePos);
			}
		}
		return flag;
	}

	public override void OnHeldChanged()
	{
		base.OnHeldChanged();
		StartAttackCooldown(deployDelay * 0.9f);
	}
}
