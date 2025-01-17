#define UNITY_ASSERTIONS
using System;
using System.Collections.Generic;
using System.Linq;
using ConVar;
using Facepunch;
using Facepunch.Rust;
using Network;
using Oxide.Core;
using ProtoBuf;
using Rust;
using Rust.Ai;
using UnityEngine;
using UnityEngine.Assertions;

public class BaseProjectile : AttackEntity
{
	[Serializable]
	public class Magazine
	{
		[Serializable]
		public struct Definition
		{
			[Tooltip("Set to 0 to not use inbuilt mag")]
			public int builtInSize;

			[InspectorFlags]
			[Tooltip("If using inbuilt mag, will accept these types of ammo")]
			public AmmoTypes ammoTypes;
		}

		public Definition definition;

		public int capacity;

		public int contents;

		[ItemSelector(ItemCategory.All)]
		public ItemDefinition ammoType;

		public void ServerInit()
		{
			if (definition.builtInSize > 0)
			{
				capacity = definition.builtInSize;
			}
		}

		public ProtoBuf.Magazine Save()
		{
			ProtoBuf.Magazine magazine = Facepunch.Pool.Get<ProtoBuf.Magazine>();
			if (ammoType == null)
			{
				magazine.capacity = capacity;
				magazine.contents = 0;
				magazine.ammoType = 0;
			}
			else
			{
				magazine.capacity = capacity;
				magazine.contents = contents;
				magazine.ammoType = ammoType.itemid;
			}
			return magazine;
		}

		public void Load(ProtoBuf.Magazine mag)
		{
			contents = mag.contents;
			capacity = mag.capacity;
			ammoType = ItemManager.FindItemDefinition(mag.ammoType);
		}

		public bool CanReload(IAmmoContainer ammoSource)
		{
			if (contents >= capacity)
			{
				return false;
			}
			return ammoSource.HasAmmo(definition.ammoTypes);
		}
	}

	public static class BaseProjectileFlags
	{
		public const Flags BurstToggle = Flags.Reserved6;
	}

	[Header("NPC Info")]
	public float NoiseRadius = 100f;

	[Header("Projectile")]
	public float damageScale = 1f;

	public float distanceScale = 1f;

	public float projectileVelocityScale = 1f;

	public bool automatic;

	public bool usableByTurret = true;

	[Tooltip("Final damage is scaled by this amount before being applied to a target when this weapon is mounted to a turret")]
	public float turretDamageScale = 0.35f;

	[Header("Effects")]
	public GameObjectRef attackFX;

	public GameObjectRef silencedAttack;

	public GameObjectRef muzzleBrakeAttack;

	public Transform MuzzlePoint;

	[Header("Reloading")]
	public float reloadTime = 1f;

	public bool canUnloadAmmo = true;

	public Magazine primaryMagazine;

	public bool fractionalReload;

	public float reloadStartDuration;

	public float reloadFractionDuration;

	public float reloadEndDuration;

	[Header("Recoil")]
	public float aimSway = 3f;

	public float aimSwaySpeed = 1f;

	public RecoilProperties recoil;

	[Header("Aim Cone")]
	public AnimationCurve aimconeCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));

	public float aimCone;

	public float hipAimCone = 1.8f;

	public float aimconePenaltyPerShot;

	public float aimConePenaltyMax;

	public float aimconePenaltyRecoverTime = 0.1f;

	public float aimconePenaltyRecoverDelay = 0.1f;

	public float stancePenaltyScale = 1f;

	[Header("Iconsights")]
	public bool hasADS = true;

	public bool noAimingWhileCycling;

	public bool manualCycle;

	[NonSerialized]
	protected bool needsCycle;

	[NonSerialized]
	protected bool isCycling;

	[NonSerialized]
	public bool aiming;

	[Header("ViewModel")]
	public bool useEmptyAmmoState;

	[Header("Burst Information")]
	public bool isBurstWeapon;

	public bool canChangeFireModes = true;

	public bool defaultOn = true;

	public float internalBurstRecoilScale = 0.8f;

	public float internalBurstFireRateScale = 0.8f;

	public float internalBurstAimConeScale = 0.8f;

	public Translate.Phrase Toast_BurstDisabled = new Translate.Phrase("burst_disabled", "Burst Disabled");

	public Translate.Phrase Toast_BurstEnabled = new Translate.Phrase("burst enabled", "Burst Enabled");

	public float resetDuration = 0.3f;

	public int numShotsFired;

	public const float maxDistance = 300f;

	[NonSerialized]
	private float nextReloadTime = float.NegativeInfinity;

	[NonSerialized]
	private float startReloadTime = float.NegativeInfinity;

	private float lastReloadTime = -10f;

	private bool modsChangedInitialized;

	private float stancePenalty;

	private float aimconePenalty;

	private uint cachedModHash;

	private float sightAimConeScale = 1f;

	private float sightAimConeOffset;

	private float hipAimConeScale = 1f;

	private float hipAimConeOffset;

	protected bool reloadStarted;

	protected bool reloadFinished;

	private int fractionalInsertCounter;

	private static readonly Effect reusableInstance = new Effect();

	public RecoilProperties recoilProperties
	{
		get
		{
			if (!(recoil == null))
			{
				return recoil.GetRecoil();
			}
			return null;
		}
	}

	public bool isSemiAuto => !automatic;

	public override Transform MuzzleTransform => MuzzlePoint;

	public override bool IsUsableByTurret => usableByTurret;

	protected virtual bool CanRefundAmmo => true;

	protected virtual ItemDefinition PrimaryMagazineAmmo => primaryMagazine.ammoType;

	public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
	{
		using (TimeWarning.New("BaseProjectile.OnRpcMessage"))
		{
			if (rpc == 3168282921u && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log("SV_RPCMessage: " + player?.ToString() + " - CLProject ");
				}
				using (TimeWarning.New("CLProject"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.FromOwner.Test(3168282921u, "CLProject", this, player))
						{
							return true;
						}
						if (!RPC_Server.IsActiveItem.Test(3168282921u, "CLProject", this, player))
						{
							return true;
						}
					}
					try
					{
						using (TimeWarning.New("Call"))
						{
							RPCMessage rPCMessage = default(RPCMessage);
							rPCMessage.connection = msg.connection;
							rPCMessage.player = player;
							rPCMessage.read = msg.read;
							RPCMessage msg2 = rPCMessage;
							CLProject(msg2);
						}
					}
					catch (Exception exception)
					{
						Debug.LogException(exception);
						player.Kick("RPC Error in CLProject");
					}
				}
				return true;
			}
			if (rpc == 1720368164 && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log("SV_RPCMessage: " + player?.ToString() + " - Reload ");
				}
				using (TimeWarning.New("Reload"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.IsActiveItem.Test(1720368164u, "Reload", this, player))
						{
							return true;
						}
					}
					try
					{
						using (TimeWarning.New("Call"))
						{
							RPCMessage rPCMessage = default(RPCMessage);
							rPCMessage.connection = msg.connection;
							rPCMessage.player = player;
							rPCMessage.read = msg.read;
							RPCMessage msg3 = rPCMessage;
							Reload(msg3);
						}
					}
					catch (Exception exception2)
					{
						Debug.LogException(exception2);
						player.Kick("RPC Error in Reload");
					}
				}
				return true;
			}
			if (rpc == 240404208 && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log("SV_RPCMessage: " + player?.ToString() + " - ServerFractionalReloadInsert ");
				}
				using (TimeWarning.New("ServerFractionalReloadInsert"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.IsActiveItem.Test(240404208u, "ServerFractionalReloadInsert", this, player))
						{
							return true;
						}
					}
					try
					{
						using (TimeWarning.New("Call"))
						{
							RPCMessage rPCMessage = default(RPCMessage);
							rPCMessage.connection = msg.connection;
							rPCMessage.player = player;
							rPCMessage.read = msg.read;
							RPCMessage msg4 = rPCMessage;
							ServerFractionalReloadInsert(msg4);
						}
					}
					catch (Exception exception3)
					{
						Debug.LogException(exception3);
						player.Kick("RPC Error in ServerFractionalReloadInsert");
					}
				}
				return true;
			}
			if (rpc == 555589155 && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log("SV_RPCMessage: " + player?.ToString() + " - StartReload ");
				}
				using (TimeWarning.New("StartReload"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.IsActiveItem.Test(555589155u, "StartReload", this, player))
						{
							return true;
						}
					}
					try
					{
						using (TimeWarning.New("Call"))
						{
							RPCMessage rPCMessage = default(RPCMessage);
							rPCMessage.connection = msg.connection;
							rPCMessage.player = player;
							rPCMessage.read = msg.read;
							RPCMessage msg5 = rPCMessage;
							StartReload(msg5);
						}
					}
					catch (Exception exception4)
					{
						Debug.LogException(exception4);
						player.Kick("RPC Error in StartReload");
					}
				}
				return true;
			}
			if (rpc == 1918419884 && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log("SV_RPCMessage: " + player?.ToString() + " - SwitchAmmoTo ");
				}
				using (TimeWarning.New("SwitchAmmoTo"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.IsActiveItem.Test(1918419884u, "SwitchAmmoTo", this, player))
						{
							return true;
						}
					}
					try
					{
						using (TimeWarning.New("Call"))
						{
							RPCMessage rPCMessage = default(RPCMessage);
							rPCMessage.connection = msg.connection;
							rPCMessage.player = player;
							rPCMessage.read = msg.read;
							RPCMessage msg6 = rPCMessage;
							SwitchAmmoTo(msg6);
						}
					}
					catch (Exception exception5)
					{
						Debug.LogException(exception5);
						player.Kick("RPC Error in SwitchAmmoTo");
					}
				}
				return true;
			}
			if (rpc == 3327286961u && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log("SV_RPCMessage: " + player?.ToString() + " - ToggleFireMode ");
				}
				using (TimeWarning.New("ToggleFireMode"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.CallsPerSecond.Test(3327286961u, "ToggleFireMode", this, player, 2uL))
						{
							return true;
						}
						if (!RPC_Server.IsActiveItem.Test(3327286961u, "ToggleFireMode", this, player))
						{
							return true;
						}
					}
					try
					{
						using (TimeWarning.New("Call"))
						{
							RPCMessage rPCMessage = default(RPCMessage);
							rPCMessage.connection = msg.connection;
							rPCMessage.player = player;
							rPCMessage.read = msg.read;
							RPCMessage msg7 = rPCMessage;
							ToggleFireMode(msg7);
						}
					}
					catch (Exception exception6)
					{
						Debug.LogException(exception6);
						player.Kick("RPC Error in ToggleFireMode");
					}
				}
				return true;
			}
		}
		return base.OnRpcMessage(player, rpc, msg);
	}

	protected bool TryReload(IAmmoContainer ammoSource, int desiredAmount, bool canRefundAmmo = true)
	{
		List<Item> list = ammoSource.FindItemsByItemID(primaryMagazine.ammoType.itemid).ToList();
		if (list.Count == 0)
		{
			List<Item> list2 = new List<Item>();
			ammoSource.FindAmmo(list2, primaryMagazine.definition.ammoTypes);
			if (list2.Count == 0)
			{
				return false;
			}
			list = ammoSource.FindItemsByItemID(list2[0].info.itemid).ToList();
			if (list == null || list.Count == 0)
			{
				return false;
			}
			if (primaryMagazine.contents > 0)
			{
				if (canRefundAmmo)
				{
					ammoSource.GiveItem(ItemManager.CreateByItemID(primaryMagazine.ammoType.itemid, primaryMagazine.contents, 0uL));
				}
				SetAmmoCount(0);
			}
			primaryMagazine.ammoType = list[0].info;
		}
		int num = desiredAmount;
		if (num == -1)
		{
			num = primaryMagazine.capacity - primaryMagazine.contents;
		}
		foreach (Item item in list)
		{
			_ = item.amount;
			int num2 = Mathf.Min(num, item.amount);
			item.UseItem(num2);
			ModifyAmmoCount(num2);
			num -= num2;
			if (num <= 0)
			{
				break;
			}
		}
		return true;
	}

	public void SwitchAmmoTypesIfNeeded(IAmmoContainer ammoSource)
	{
		List<Item> list = ammoSource.FindItemsByItemID(primaryMagazine.ammoType.itemid).ToList();
		if (list.Count != 0)
		{
			return;
		}
		List<Item> list2 = new List<Item>();
		ammoSource.FindAmmo(list2, primaryMagazine.definition.ammoTypes);
		if (list2.Count == 0)
		{
			return;
		}
		list = ammoSource.FindItemsByItemID(list2[0].info.itemid).ToList();
		if (list != null && list.Count != 0)
		{
			if (primaryMagazine.contents > 0)
			{
				ammoSource.GiveItem(ItemManager.CreateByItemID(primaryMagazine.ammoType.itemid, primaryMagazine.contents, 0uL));
				SetAmmoCount(0);
			}
			primaryMagazine.ammoType = list[0].info;
		}
	}

	public void SetAmmoCount(int newCount)
	{
		primaryMagazine.contents = newCount;
		Item item = GetItem();
		if (item != null)
		{
			item.ammoCount = newCount;
			item.MarkDirty();
		}
	}

	public void ModifyAmmoCount(int amount)
	{
		SetAmmoCount(primaryMagazine.contents + amount);
	}

	public override Vector3 GetInheritedVelocity(BasePlayer player, Vector3 direction)
	{
		return player.GetInheritedProjectileVelocity(direction);
	}

	public virtual float GetDamageScale(bool getMax = false)
	{
		return damageScale;
	}

	public virtual float GetDistanceScale(bool getMax = false)
	{
		return distanceScale;
	}

	public virtual float GetProjectileVelocityScale(bool getMax = false)
	{
		return projectileVelocityScale;
	}

	protected void StartReloadCooldown(float cooldown)
	{
		nextReloadTime = CalculateCooldownTime(nextReloadTime, cooldown, catchup: false);
		startReloadTime = nextReloadTime - cooldown;
	}

	protected void ResetReloadCooldown()
	{
		nextReloadTime = float.NegativeInfinity;
	}

	protected bool HasReloadCooldown()
	{
		return UnityEngine.Time.time < nextReloadTime;
	}

	protected float GetReloadCooldown()
	{
		return Mathf.Max(nextReloadTime - UnityEngine.Time.time, 0f);
	}

	protected float GetReloadIdle()
	{
		return Mathf.Max(UnityEngine.Time.time - nextReloadTime, 0f);
	}

	private void OnDrawGizmos()
	{
		if (base.isClient && MuzzlePoint != null)
		{
			Gizmos.color = Color.blue;
			Gizmos.DrawLine(MuzzlePoint.position, MuzzlePoint.position + MuzzlePoint.forward * 10f);
			BasePlayer ownerPlayer = GetOwnerPlayer();
			if ((bool)ownerPlayer)
			{
				Gizmos.color = Color.cyan;
				Gizmos.DrawLine(MuzzlePoint.position, MuzzlePoint.position + ownerPlayer.eyes.rotation * Vector3.forward * 10f);
			}
		}
	}

	public virtual RecoilProperties GetRecoil()
	{
		return recoilProperties;
	}

	public override float AmmoFraction()
	{
		return (float)primaryMagazine.contents / (float)primaryMagazine.capacity;
	}

	public virtual void DidAttackServerside()
	{
	}

	public override bool ServerIsReloading()
	{
		return UnityEngine.Time.time < lastReloadTime + reloadTime;
	}

	public override bool CanReload()
	{
		return primaryMagazine.contents < primaryMagazine.capacity;
	}

	public override void TopUpAmmo()
	{
		SetAmmoCount(primaryMagazine.capacity);
	}

	public override void ServerReload()
	{
		if (!ServerIsReloading())
		{
			lastReloadTime = UnityEngine.Time.time;
			StartAttackCooldown(reloadTime);
			BasePlayer ownerPlayer = GetOwnerPlayer();
			if (ownerPlayer != null)
			{
				ownerPlayer.SignalBroadcast(Signal.Reload);
			}
			SetAmmoCount(primaryMagazine.capacity);
		}
	}

	public override bool ServerTryReload(IAmmoContainer ammoSource)
	{
		if (ServerIsReloading())
		{
			return false;
		}
		if (TryReloadMagazine(ammoSource))
		{
			BasePlayer ownerPlayer = GetOwnerPlayer();
			if (ownerPlayer != null)
			{
				ownerPlayer.SignalBroadcast(Signal.Reload);
			}
			lastReloadTime = UnityEngine.Time.time;
			StartAttackCooldown(reloadTime);
			return true;
		}
		return false;
	}

	public override Vector3 ModifyAIAim(Vector3 eulerInput, float swayModifier = 1f)
	{
		float num = UnityEngine.Time.time * (aimSwaySpeed * 1f + aiAimSwayOffset);
		float num2 = Mathf.Sin(UnityEngine.Time.time * 2f);
		float num3 = ((num2 < 0f) ? (1f - Mathf.Clamp(Mathf.Abs(num2) / 1f, 0f, 1f)) : 1f);
		float num4 = (false ? 0.6f : 1f);
		float num5 = (aimSway * 1f + aiAimSwayOffset) * num4 * num3 * swayModifier;
		eulerInput.y += (Mathf.PerlinNoise(num, num) - 0.5f) * num5 * UnityEngine.Time.deltaTime;
		eulerInput.x += (Mathf.PerlinNoise(num + 0.1f, num + 0.2f) - 0.5f) * num5 * UnityEngine.Time.deltaTime;
		return eulerInput;
	}

	public float GetAIAimcone()
	{
		NPCPlayer nPCPlayer = GetOwnerPlayer() as NPCPlayer;
		if ((bool)nPCPlayer)
		{
			return nPCPlayer.GetAimConeScale() * aiAimCone;
		}
		return aiAimCone;
	}

	public override void ServerUse()
	{
		ServerUse(1f);
	}

	public override void ServerUse(float damageModifier, Transform originOverride = null)
	{
		if (base.isClient || HasAttackCooldown())
		{
			return;
		}
		BasePlayer ownerPlayer = GetOwnerPlayer();
		bool flag = ownerPlayer != null;
		if (primaryMagazine.contents <= 0)
		{
			SignalBroadcast(Signal.DryFire);
			StartAttackCooldownRaw(1f);
			return;
		}
		ModifyAmmoCount(-1);
		if (primaryMagazine.contents < 0)
		{
			SetAmmoCount(0);
		}
		bool flag2 = flag && ownerPlayer.IsNpc;
		if (flag2 && (ownerPlayer.isMounted || ownerPlayer.GetParentEntity() != null))
		{
			NPCPlayer nPCPlayer = ownerPlayer as NPCPlayer;
			if (nPCPlayer != null)
			{
				nPCPlayer.SetAimDirection(nPCPlayer.GetAimDirection());
			}
		}
		StartAttackCooldownRaw(repeatDelay);
		Vector3 vector = (flag ? ownerPlayer.eyes.position : MuzzlePoint.transform.position);
		Vector3 inputVec = MuzzlePoint.transform.forward;
		if (originOverride != null)
		{
			vector = originOverride.position;
			inputVec = originOverride.forward;
		}
		ItemModProjectile component = primaryMagazine.ammoType.GetComponent<ItemModProjectile>();
		SignalBroadcast(Signal.Attack, string.Empty);
		Projectile component2 = component.projectileObject.Get().GetComponent<Projectile>();
		BaseEntity baseEntity = null;
		if (flag)
		{
			inputVec = ownerPlayer.eyes.BodyForward();
		}
		for (int i = 0; i < component.numProjectiles; i++)
		{
			Vector3 modifiedAimConeDirection = AimConeUtil.GetModifiedAimConeDirection(component.projectileSpread + GetAimCone() + GetAIAimcone() * 1f, inputVec);
			List<RaycastHit> obj = Facepunch.Pool.GetList<RaycastHit>();
			GamePhysics.TraceAll(new Ray(vector, modifiedAimConeDirection), 0f, obj, 300f, 1220225793);
			for (int j = 0; j < obj.Count; j++)
			{
				RaycastHit hit = obj[j];
				BaseEntity entity = RaycastHitEx.GetEntity(hit);
				if ((entity != null && (entity == this || entity.EqualNetID(this))) || (entity != null && entity.isClient))
				{
					continue;
				}
				ColliderInfo component3 = hit.collider.GetComponent<ColliderInfo>();
				if (component3 != null && !component3.HasFlag(ColliderInfo.Flags.Shootable))
				{
					continue;
				}
				BaseCombatEntity baseCombatEntity = entity as BaseCombatEntity;
				if ((!(entity != null && entity.IsNpc && flag2) || baseCombatEntity.GetFaction() == BaseCombatEntity.Faction.Horror || entity is BasePet) && baseCombatEntity != null && (baseEntity == null || entity == baseEntity || entity.EqualNetID(baseEntity)))
				{
					HitInfo hitInfo = new HitInfo();
					AssignInitiator(hitInfo);
					hitInfo.Weapon = this;
					hitInfo.WeaponPrefab = base.gameManager.FindPrefab(base.PrefabName).GetComponent<AttackEntity>();
					hitInfo.IsPredicting = false;
					hitInfo.DoHitEffects = component2.doDefaultHitEffects;
					hitInfo.DidHit = true;
					hitInfo.ProjectileVelocity = modifiedAimConeDirection * 300f;
					hitInfo.PointStart = MuzzlePoint.position;
					hitInfo.PointEnd = hit.point;
					hitInfo.HitPositionWorld = hit.point;
					hitInfo.HitNormalWorld = hit.normal;
					hitInfo.HitEntity = entity;
					hitInfo.UseProtection = true;
					component2.CalculateDamage(hitInfo, GetProjectileModifier(), 1f);
					hitInfo.damageTypes.ScaleAll(GetDamageScale() * damageModifier * (flag2 ? npcDamageScale : turretDamageScale));
					baseCombatEntity.OnAttacked(hitInfo);
					component.ServerProjectileHit(hitInfo);
					if (entity is BasePlayer || entity is BaseNpc)
					{
						hitInfo.HitPositionLocal = entity.transform.InverseTransformPoint(hitInfo.HitPositionWorld);
						hitInfo.HitNormalLocal = entity.transform.InverseTransformDirection(hitInfo.HitNormalWorld);
						hitInfo.HitMaterial = StringPool.Get("Flesh");
						Effect.server.ImpactEffect(hitInfo);
					}
				}
				if (!(entity != null) || entity.ShouldBlockProjectiles())
				{
					break;
				}
			}
			Facepunch.Pool.FreeList(ref obj);
			Vector3 vector2 = ((flag && ownerPlayer.isMounted) ? (modifiedAimConeDirection * 6f) : Vector3.zero);
			CreateProjectileEffectClientside(component.projectileObject.resourcePath, vector + vector2, modifiedAimConeDirection * component.projectileVelocity, UnityEngine.Random.Range(1, 100), null, IsSilenced(), forceClientsideEffects: true);
		}
	}

	private void AssignInitiator(HitInfo info)
	{
		info.Initiator = GetOwnerPlayer();
		if (info.Initiator == null)
		{
			info.Initiator = GetParentEntity();
		}
	}

	public override void ServerInit()
	{
		base.ServerInit();
		primaryMagazine.ServerInit();
		Invoke(DelayedModSetup, 0.1f);
	}

	public void DelayedModSetup()
	{
		if (!modsChangedInitialized)
		{
			Item item = GetCachedItem();
			if (item != null && item.contents != null)
			{
				ItemContainer contents = item.contents;
				contents.onItemAddedRemoved = (Action<Item, bool>)Delegate.Combine(contents.onItemAddedRemoved, new Action<Item, bool>(ModsChanged));
				modsChangedInitialized = true;
			}
		}
	}

	public override void DestroyShared()
	{
		if (base.isServer)
		{
			Item item = GetCachedItem();
			if (item != null && item.contents != null)
			{
				ItemContainer contents = item.contents;
				contents.onItemAddedRemoved = (Action<Item, bool>)Delegate.Remove(contents.onItemAddedRemoved, new Action<Item, bool>(ModsChanged));
				modsChangedInitialized = false;
			}
		}
		base.DestroyShared();
	}

	public void ModsChanged(Item item, bool added)
	{
		Invoke(DelayedModsChanged, 0.1f);
	}

	public void ForceModsChanged()
	{
		Invoke(DelayedModSetup, 0f);
		Invoke(DelayedModsChanged, 0.2f);
	}

	public void DelayedModsChanged()
	{
		if (Interface.CallHook("OnWeaponModChange", this, GetOwnerPlayer()) != null)
		{
			return;
		}
		int num = Mathf.CeilToInt(ProjectileWeaponMod.Mult(this, (ProjectileWeaponMod x) => x.magazineCapacity, (ProjectileWeaponMod.Modifier y) => y.scalar, 1f) * (float)primaryMagazine.definition.builtInSize);
		if (num == primaryMagazine.capacity)
		{
			return;
		}
		if (primaryMagazine.contents > 0 && primaryMagazine.contents > num)
		{
			_ = primaryMagazine.ammoType;
			int contents = primaryMagazine.contents;
			BasePlayer ownerPlayer = GetOwnerPlayer();
			ItemContainer itemContainer = null;
			if (ownerPlayer != null)
			{
				itemContainer = ownerPlayer.inventory.containerMain;
			}
			else if (GetCachedItem() != null)
			{
				itemContainer = GetCachedItem().parent;
			}
			SetAmmoCount(0);
			if (itemContainer != null)
			{
				Item item = ItemManager.Create(primaryMagazine.ammoType, contents, 0uL);
				if (!item.MoveToContainer(itemContainer))
				{
					Vector3 vPos = base.transform.position;
					if (itemContainer.entityOwner != null)
					{
						vPos = itemContainer.entityOwner.transform.position + Vector3.up * 0.25f;
					}
					item.Drop(vPos, Vector3.up * 5f);
				}
			}
		}
		primaryMagazine.capacity = num;
		SendNetworkUpdate();
	}

	public override void ServerCommand(Item item, string command, BasePlayer player)
	{
		if (item != null && command == "unload_ammo" && !HasReloadCooldown())
		{
			UnloadAmmo(item, player);
		}
	}

	public void UnloadAmmo(Item item, BasePlayer player)
	{
		BaseProjectile component = item.GetHeldEntity().GetComponent<BaseProjectile>();
		if (!component.canUnloadAmmo || Interface.CallHook("OnAmmoUnload", component, item, player) != null || !component)
		{
			return;
		}
		int contents = component.primaryMagazine.contents;
		if (contents > 0)
		{
			component.SetAmmoCount(0);
			item.MarkDirty();
			SendNetworkUpdateImmediate();
			Item item2 = ItemManager.Create(component.primaryMagazine.ammoType, contents, 0uL);
			if (!item2.MoveToContainer(player.inventory.containerMain))
			{
				item2.Drop(player.GetDropPosition(), player.GetDropVelocity());
			}
		}
	}

	public override void CollectedForCrafting(Item item, BasePlayer crafter)
	{
		if (!(crafter == null) && item != null)
		{
			UnloadAmmo(item, crafter);
		}
	}

	public override void ReturnedFromCancelledCraft(Item item, BasePlayer crafter)
	{
		if (!(crafter == null) && item != null)
		{
			BaseProjectile component = item.GetHeldEntity().GetComponent<BaseProjectile>();
			if ((bool)component)
			{
				component.SetAmmoCount(0);
			}
		}
	}

	public override void SetLightsOn(bool isOn)
	{
		base.SetLightsOn(isOn);
		UpdateAttachmentsState();
	}

	public void UpdateAttachmentsState()
	{
		_ = flags;
		bool b = ShouldLightsBeOn();
		if (children == null)
		{
			return;
		}
		foreach (ProjectileWeaponMod item in from ProjectileWeaponMod x in children
			where x != null && x.isLight
			select x)
		{
			item.SetFlag(Flags.On, b);
		}
	}

	private bool ShouldLightsBeOn()
	{
		if (LightsOn())
		{
			if (!IsDeployed())
			{
				return parentEntity.Get(base.isServer) is AutoTurret;
			}
			return true;
		}
		return false;
	}

	protected override void OnChildRemoved(BaseEntity child)
	{
		base.OnChildRemoved(child);
		if (child is ProjectileWeaponMod { isLight: not false })
		{
			child.SetFlag(Flags.On, b: false);
			SetLightsOn(isOn: false);
		}
	}

	public bool CanAiAttack()
	{
		return true;
	}

	public virtual float GetAimCone()
	{
		uint num = 0u;
		foreach (BaseEntity child in children)
		{
			num += (uint)(int)child.net.ID.Value;
			num += (uint)child.flags;
		}
		uint num2 = CRC.Compute32(0u, num);
		if (num2 != cachedModHash)
		{
			sightAimConeScale = ProjectileWeaponMod.Mult(this, (ProjectileWeaponMod x) => x.sightAimCone, (ProjectileWeaponMod.Modifier y) => y.scalar, 1f);
			sightAimConeOffset = ProjectileWeaponMod.Sum(this, (ProjectileWeaponMod x) => x.sightAimCone, (ProjectileWeaponMod.Modifier y) => y.offset, 0f);
			hipAimConeScale = ProjectileWeaponMod.Mult(this, (ProjectileWeaponMod x) => x.hipAimCone, (ProjectileWeaponMod.Modifier y) => y.scalar, 1f);
			hipAimConeOffset = ProjectileWeaponMod.Sum(this, (ProjectileWeaponMod x) => x.hipAimCone, (ProjectileWeaponMod.Modifier y) => y.offset, 0f);
			cachedModHash = num2;
		}
		float num3 = aimCone;
		num3 *= (UsingInternalBurstMode() ? internalBurstAimConeScale : 1f);
		if (recoilProperties != null && recoilProperties.overrideAimconeWithCurve && primaryMagazine.capacity > 0)
		{
			num3 += recoilProperties.aimconeCurve.Evaluate((float)numShotsFired / (float)primaryMagazine.capacity % 1f) * recoilProperties.aimconeCurveScale;
			aimconePenalty = 0f;
		}
		if (aiming || base.isServer)
		{
			return (num3 + aimconePenalty + stancePenalty * stancePenaltyScale) * sightAimConeScale + sightAimConeOffset;
		}
		return (num3 + aimconePenalty + stancePenalty * stancePenaltyScale) * sightAimConeScale + sightAimConeOffset + hipAimCone * hipAimConeScale + hipAimConeOffset;
	}

	public float ScaleRepeatDelay(float delay)
	{
		float num = ProjectileWeaponMod.Average(this, (ProjectileWeaponMod x) => x.repeatDelay, (ProjectileWeaponMod.Modifier y) => y.scalar, 1f);
		float num2 = ProjectileWeaponMod.Sum(this, (ProjectileWeaponMod x) => x.repeatDelay, (ProjectileWeaponMod.Modifier y) => y.offset, 0f);
		float num3 = (UsingInternalBurstMode() ? internalBurstFireRateScale : 1f);
		return delay * num * num3 + num2;
	}

	public Projectile.Modifier GetProjectileModifier()
	{
		Projectile.Modifier result = default(Projectile.Modifier);
		result.damageOffset = ProjectileWeaponMod.Sum(this, (ProjectileWeaponMod x) => x.projectileDamage, (ProjectileWeaponMod.Modifier y) => y.offset, 0f);
		result.damageScale = ProjectileWeaponMod.Average(this, (ProjectileWeaponMod x) => x.projectileDamage, (ProjectileWeaponMod.Modifier y) => y.scalar, 1f) * GetDamageScale();
		result.distanceOffset = ProjectileWeaponMod.Sum(this, (ProjectileWeaponMod x) => x.projectileDistance, (ProjectileWeaponMod.Modifier y) => y.offset, 0f);
		result.distanceScale = ProjectileWeaponMod.Average(this, (ProjectileWeaponMod x) => x.projectileDistance, (ProjectileWeaponMod.Modifier y) => y.scalar, 1f) * GetDistanceScale();
		return result;
	}

	public bool UsingBurstMode()
	{
		if (IsBurstDisabled())
		{
			return false;
		}
		if (!isBurstWeapon)
		{
			if (children != null)
			{
				return (from ProjectileWeaponMod x in children
					where x != null && x.burstCount > 0
					select x).FirstOrDefault() != null;
			}
			return false;
		}
		return true;
	}

	public bool UsingInternalBurstMode()
	{
		if (IsBurstDisabled())
		{
			return false;
		}
		return isBurstWeapon;
	}

	public bool IsBurstEligable()
	{
		if (!isBurstWeapon)
		{
			if (children != null)
			{
				return (from ProjectileWeaponMod x in children
					where x != null && x.burstCount > 0
					select x).FirstOrDefault() != null;
			}
			return false;
		}
		return true;
	}

	public float TimeBetweenBursts()
	{
		return repeatDelay * 3f;
	}

	public virtual bool CanAttack()
	{
		if (ProjectileWeaponMod.HasBrokenWeaponMod(this))
		{
			return false;
		}
		return true;
	}

	public float GetReloadDuration()
	{
		if (fractionalReload)
		{
			int num = Mathf.Min(primaryMagazine.capacity - primaryMagazine.contents, GetAvailableAmmo());
			return reloadStartDuration + reloadEndDuration + reloadFractionDuration * (float)num;
		}
		return reloadTime;
	}

	public int GetAvailableAmmo()
	{
		BasePlayer ownerPlayer = GetOwnerPlayer();
		if (ownerPlayer == null)
		{
			return primaryMagazine.contents;
		}
		List<Item> obj = Facepunch.Pool.GetList<Item>();
		ownerPlayer.inventory.FindAmmo(obj, primaryMagazine.definition.ammoTypes);
		int num = 0;
		if (obj.Count != 0)
		{
			for (int i = 0; i < obj.Count; i++)
			{
				Item item = obj[i];
				if (item.info == primaryMagazine.ammoType)
				{
					num += item.amount;
				}
			}
		}
		Facepunch.Pool.FreeList(ref obj);
		return num;
	}

	public bool IsBurstDisabled()
	{
		return HasFlag(Flags.Reserved6) == defaultOn;
	}

	[RPC_Server]
	[RPC_Server.CallsPerSecond(2uL)]
	[RPC_Server.IsActiveItem]
	private void ToggleFireMode(RPCMessage msg)
	{
		if (canChangeFireModes && IsBurstEligable())
		{
			SetFlag(Flags.Reserved6, !HasFlag(Flags.Reserved6));
			SendNetworkUpdate_Flags();
			Facepunch.Rust.Analytics.Azure.OnBurstModeToggled(msg.player, this, HasFlag(Flags.Reserved6));
			BasePlayer ownerPlayer = GetOwnerPlayer();
			if (!ownerPlayer.IsNpc && ownerPlayer.IsConnected)
			{
				ownerPlayer.ShowToast(GameTip.Styles.Blue_Short, IsBurstDisabled() ? Toast_BurstDisabled : Toast_BurstEnabled);
			}
		}
	}

	public virtual bool TryReloadMagazine(IAmmoContainer ammoSource, int desiredAmount = -1)
	{
		object obj = Interface.CallHook("OnMagazineReload", this, ammoSource, GetOwnerPlayer());
		if (obj is bool)
		{
			return (bool)obj;
		}
		if (!TryReload(ammoSource, desiredAmount))
		{
			return false;
		}
		SendNetworkUpdateImmediate();
		ItemManager.DoRemoves();
		BasePlayer ownerPlayer = GetOwnerPlayer();
		if (ownerPlayer != null)
		{
			ownerPlayer.inventory.ServerUpdate(0f);
		}
		return true;
	}

	[RPC_Server.IsActiveItem]
	[RPC_Server]
	private void SwitchAmmoTo(RPCMessage msg)
	{
		BasePlayer ownerPlayer = GetOwnerPlayer();
		if (!ownerPlayer)
		{
			return;
		}
		int num = msg.read.Int32();
		if (num == primaryMagazine.ammoType.itemid)
		{
			return;
		}
		ItemDefinition itemDefinition = ItemManager.FindItemDefinition(num);
		if (itemDefinition == null)
		{
			return;
		}
		ItemModProjectile component = itemDefinition.GetComponent<ItemModProjectile>();
		if ((bool)component && component.IsAmmo(primaryMagazine.definition.ammoTypes) && Interface.CallHook("OnAmmoSwitch", this, ownerPlayer, itemDefinition) == null)
		{
			if (primaryMagazine.contents > 0)
			{
				ownerPlayer.GiveItem(ItemManager.CreateByItemID(primaryMagazine.ammoType.itemid, primaryMagazine.contents, 0uL));
				SetAmmoCount(0);
			}
			primaryMagazine.ammoType = itemDefinition;
			SendNetworkUpdateImmediate();
			ItemManager.DoRemoves();
			ownerPlayer.inventory.ServerUpdate(0f);
		}
	}

	public override void OnHeldChanged()
	{
		base.OnHeldChanged();
		reloadStarted = false;
		reloadFinished = false;
		fractionalInsertCounter = 0;
		UpdateAttachmentsState();
	}

	[RPC_Server.IsActiveItem]
	[RPC_Server]
	private void StartReload(RPCMessage msg)
	{
		BasePlayer player = msg.player;
		if (!VerifyClientRPC(player))
		{
			SendNetworkUpdate();
			reloadStarted = false;
			reloadFinished = false;
		}
		else if (Interface.CallHook("OnWeaponReload", this, player) == null)
		{
			reloadFinished = false;
			reloadStarted = true;
			fractionalInsertCounter = 0;
			if (CanRefundAmmo)
			{
				SwitchAmmoTypesIfNeeded(player.inventory);
			}
			OnReloadStarted();
			StartReloadCooldown(GetReloadDuration());
		}
	}

	protected virtual void OnReloadStarted()
	{
	}

	[RPC_Server]
	[RPC_Server.IsActiveItem]
	private void ServerFractionalReloadInsert(RPCMessage msg)
	{
		BasePlayer player = msg.player;
		if (!VerifyClientRPC(player))
		{
			SendNetworkUpdate();
			reloadStarted = false;
			reloadFinished = false;
			return;
		}
		if (!fractionalReload)
		{
			AntiHack.Log(player, AntiHackType.ReloadHack, "Fractional reload not allowed (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "reload_type");
			return;
		}
		if (!reloadStarted)
		{
			AntiHack.Log(player, AntiHackType.ReloadHack, "Fractional reload request skipped (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "reload_skip");
			reloadStarted = false;
			reloadFinished = false;
			return;
		}
		if (GetReloadIdle() > 3f)
		{
			AntiHack.Log(player, AntiHackType.ReloadHack, "T+" + GetReloadIdle() + "s (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "reload_time");
			reloadStarted = false;
			reloadFinished = false;
			return;
		}
		if (UnityEngine.Time.time < startReloadTime + reloadStartDuration)
		{
			AntiHack.Log(player, AntiHackType.ReloadHack, "Fractional reload too early (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "reload_fraction_too_early");
			reloadStarted = false;
			reloadFinished = false;
		}
		if (UnityEngine.Time.time < startReloadTime + reloadStartDuration + (float)fractionalInsertCounter * reloadFractionDuration)
		{
			AntiHack.Log(player, AntiHackType.ReloadHack, "Fractional reload rate too high (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "reload_fraction_rate");
			reloadStarted = false;
			reloadFinished = false;
		}
		else
		{
			fractionalInsertCounter++;
			if (primaryMagazine.contents < primaryMagazine.capacity)
			{
				TryReloadMagazine(player.inventory, 1);
			}
		}
	}

	[RPC_Server.IsActiveItem]
	[RPC_Server]
	private void Reload(RPCMessage msg)
	{
		BasePlayer player = msg.player;
		if (!VerifyClientRPC(player))
		{
			SendNetworkUpdate();
			reloadStarted = false;
			reloadFinished = false;
			return;
		}
		if (!reloadStarted)
		{
			AntiHack.Log(player, AntiHackType.ReloadHack, "Request skipped (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "reload_skip");
			reloadStarted = false;
			reloadFinished = false;
			return;
		}
		if (!fractionalReload)
		{
			if (GetReloadCooldown() > 1f)
			{
				AntiHack.Log(player, AntiHackType.ReloadHack, "T-" + GetReloadCooldown() + "s (" + base.ShortPrefabName + ")");
				player.stats.combat.LogInvalid(player, this, "reload_time");
				reloadStarted = false;
				reloadFinished = false;
				return;
			}
			if (GetReloadIdle() > 1.5f)
			{
				AntiHack.Log(player, AntiHackType.ReloadHack, "T+" + GetReloadIdle() + "s (" + base.ShortPrefabName + ")");
				player.stats.combat.LogInvalid(player, this, "reload_time");
				reloadStarted = false;
				reloadFinished = false;
				return;
			}
		}
		if (fractionalReload)
		{
			ResetReloadCooldown();
		}
		reloadStarted = false;
		reloadFinished = true;
		if (!fractionalReload)
		{
			TryReloadMagazine(player.inventory);
		}
	}

	[RPC_Server]
	[RPC_Server.FromOwner]
	[RPC_Server.IsActiveItem]
	private void CLProject(RPCMessage msg)
	{
		BasePlayer player = msg.player;
		if (!VerifyClientAttack(player))
		{
			SendNetworkUpdate();
			return;
		}
		if (reloadFinished && HasReloadCooldown())
		{
			AntiHack.Log(player, AntiHackType.ProjectileHack, "Reloading (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "reload_cooldown");
			return;
		}
		reloadStarted = false;
		reloadFinished = false;
		if (primaryMagazine.contents <= 0 && !base.UsingInfiniteAmmoCheat)
		{
			AntiHack.Log(player, AntiHackType.ProjectileHack, "Magazine empty (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "ammo_missing");
			return;
		}
		ItemDefinition primaryMagazineAmmo = PrimaryMagazineAmmo;
		ProjectileShoot projectileShoot = ProjectileShoot.Deserialize(msg.read);
		if (primaryMagazineAmmo.itemid != projectileShoot.ammoType)
		{
			AntiHack.Log(player, AntiHackType.ProjectileHack, "Ammo mismatch (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "ammo_mismatch");
			return;
		}
		if (!base.UsingInfiniteAmmoCheat)
		{
			ModifyAmmoCount(-1);
		}
		ItemModProjectile component = primaryMagazineAmmo.GetComponent<ItemModProjectile>();
		if (component == null)
		{
			AntiHack.Log(player, AntiHackType.ProjectileHack, "Item mod not found (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "mod_missing");
			return;
		}
		if (projectileShoot.projectiles.Count > component.numProjectiles)
		{
			AntiHack.Log(player, AntiHackType.ProjectileHack, "Count mismatch (" + base.ShortPrefabName + ")");
			player.stats.combat.LogInvalid(player, this, "count_mismatch");
			return;
		}
		Interface.CallHook("OnWeaponFired", this, msg.player, component, projectileShoot);
		if (player.InGesture)
		{
			return;
		}
		SignalBroadcast(Signal.Attack, string.Empty, msg.connection);
		player.CleanupExpiredProjectiles();
		Guid projectileGroupId = Guid.NewGuid();
		foreach (ProjectileShoot.Projectile projectile in projectileShoot.projectiles)
		{
			if (player.HasFiredProjectile(projectile.projectileID))
			{
				AntiHack.Log(player, AntiHackType.ProjectileHack, "Duplicate ID (" + projectile.projectileID + ")");
				player.stats.combat.LogInvalid(player, this, "duplicate_id");
				continue;
			}
			Vector3 positionOffset = Vector3.zero;
			if (ConVar.AntiHack.projectile_positionoffset && (player.isMounted || player.HasParent()))
			{
				if (!ValidateEyePos(player, projectile.startPos, checkLineOfSight: false))
				{
					continue;
				}
				Vector3 position = player.eyes.position;
				positionOffset = position - projectile.startPos;
				projectile.startPos = position;
			}
			else if (!ValidateEyePos(player, projectile.startPos))
			{
				continue;
			}
			player.NoteFiredProjectile(projectile.projectileID, projectile.startPos, projectile.startVel, this, primaryMagazineAmmo, projectileGroupId, positionOffset);
			if (!player.limitNetworking)
			{
				CreateProjectileEffectClientside(component.projectileObject.resourcePath, projectile.startPos, projectile.startVel, projectile.seed, msg.connection, IsSilenced());
			}
		}
		player.MakeNoise(player.transform.position, BaseCombatEntity.ActionVolume.Loud);
		player.stats.Add(component.category + "_fired", projectileShoot.projectiles.Count(), (Stats)5);
		player.LifeStoryShotFired(this);
		StartAttackCooldown(ScaleRepeatDelay(repeatDelay) + animationDelay);
		player.MarkHostileFor();
		UpdateItemCondition();
		DidAttackServerside();
		float num = 0f;
		if (component.projectileObject != null)
		{
			GameObject gameObject = component.projectileObject.Get();
			if (gameObject != null)
			{
				Projectile component2 = gameObject.GetComponent<Projectile>();
				if (component2 != null)
				{
					foreach (DamageTypeEntry damageType in component2.damageTypes)
					{
						num += damageType.amount;
					}
				}
			}
		}
		float num2 = NoiseRadius;
		if (IsSilenced())
		{
			num2 *= AI.npc_gun_noise_silencer_modifier;
		}
		Sensation sensation = default(Sensation);
		sensation.Type = SensationType.Gunshot;
		sensation.Position = player.transform.position;
		sensation.Radius = num2;
		sensation.DamagePotential = num;
		sensation.InitiatorPlayer = player;
		sensation.Initiator = player;
		Sense.Stimulate(sensation);
		BaseMountable mounted = player.GetMounted();
		if (mounted != null)
		{
			mounted.OnWeaponFired(this);
		}
		EACServer.LogPlayerUseWeapon(player, this);
	}

	public void CreateProjectileEffectClientside(string prefabName, Vector3 pos, Vector3 velocity, int seed, Connection sourceConnection, bool silenced = false, bool forceClientsideEffects = false)
	{
		if (Interface.CallHook("OnClientProjectileEffectCreate", sourceConnection, this, prefabName) == null)
		{
			Effect effect = reusableInstance;
			effect.Clear();
			effect.Init(Effect.Type.Projectile, pos, velocity, sourceConnection);
			effect.scale = (silenced ? 0f : 1f);
			if (forceClientsideEffects)
			{
				effect.scale = 2f;
			}
			effect.pooledString = prefabName;
			effect.number = seed;
			EffectNetwork.Send(effect);
		}
	}

	public void UpdateItemCondition()
	{
		Item ownerItem = GetOwnerItem();
		if (ownerItem == null)
		{
			return;
		}
		float barrelConditionLoss = primaryMagazine.ammoType.GetComponent<ItemModProjectile>().barrelConditionLoss;
		float num = 0.25f;
		bool usingInfiniteAmmoCheat = base.UsingInfiniteAmmoCheat;
		if (!usingInfiniteAmmoCheat)
		{
			ownerItem.LoseCondition(num + barrelConditionLoss);
		}
		if (ownerItem.contents == null || ownerItem.contents.itemList == null)
		{
			return;
		}
		for (int num2 = ownerItem.contents.itemList.Count - 1; num2 >= 0; num2--)
		{
			Item item = ownerItem.contents.itemList[num2];
			if (item != null && !usingInfiniteAmmoCheat)
			{
				item.LoseCondition(num + barrelConditionLoss);
			}
		}
	}

	public bool IsSilenced()
	{
		if (children != null)
		{
			foreach (BaseEntity child in children)
			{
				ProjectileWeaponMod projectileWeaponMod = child as ProjectileWeaponMod;
				if (projectileWeaponMod != null && projectileWeaponMod.isSilencer && !projectileWeaponMod.IsBroken())
				{
					return true;
				}
			}
		}
		return false;
	}

	public override bool CanUseNetworkCache(Connection sendingTo)
	{
		Connection ownerConnection = GetOwnerConnection();
		if (sendingTo == null || ownerConnection == null)
		{
			return true;
		}
		return sendingTo != ownerConnection;
	}

	public override void Save(SaveInfo info)
	{
		base.Save(info);
		info.msg.baseProjectile = Facepunch.Pool.Get<ProtoBuf.BaseProjectile>();
		if (info.forDisk || info.SendingTo(GetOwnerConnection()) || ForceSendMagazine(info))
		{
			info.msg.baseProjectile.primaryMagazine = primaryMagazine.Save();
		}
	}

	public virtual bool ForceSendMagazine(SaveInfo saveInfo)
	{
		BasePlayer ownerPlayer = GetOwnerPlayer();
		if ((bool)ownerPlayer && ownerPlayer.IsBeingSpectated)
		{
			foreach (BaseEntity child in ownerPlayer.children)
			{
				if (child.net != null && child.net.connection == saveInfo.forConnection)
				{
					return true;
				}
			}
		}
		return false;
	}

	public override void Load(LoadInfo info)
	{
		base.Load(info);
		if (info.msg.baseProjectile != null && info.msg.baseProjectile.primaryMagazine != null)
		{
			primaryMagazine.Load(info.msg.baseProjectile.primaryMagazine);
		}
	}
}
