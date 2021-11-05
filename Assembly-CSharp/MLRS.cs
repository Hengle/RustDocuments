#define UNITY_ASSERTIONS
using System;
using ConVar;
using Facepunch;
using Network;
using ProtoBuf;
using Rust;
using UnityEngine;
using UnityEngine.Assertions;

public class MLRS : BaseMountable, TimedExplosive.IPreventSticking
{
	[Serializable]
	public class RocketTube
	{
		public Vector3 firingOffset;

		public Transform hinge;

		public Renderer rocket;
	}

	private struct TheoreticalProjectile
	{
		public Vector3 pos;

		public Vector3 forward;

		public float gravityMult;

		public TheoreticalProjectile(Vector3 pos, Vector3 forward, float gravityMult)
		{
			this.pos = pos;
			this.forward = forward;
			this.gravityMult = gravityMult;
		}
	}

	private float leftRightInput;

	private float upDownInput;

	private Vector3 lastSentTargetHitPos;

	private Vector3 lastSentTrueHitPos;

	private int nextRocketIndex;

	private EntityRef rocketOwnerRef;

	private TimeSince timeSinceBroken;

	private int radiusModIndex;

	private float[] radiusMods = new float[6]
	{
		0.0833333358f,
		355f / (678f * (float)Math.PI),
		0.333333343f,
		0.5f,
		2f / 3f,
		0.75f
	};

	private Vector3 trueTargetHitPos;

	[Header("MLRS Components")]
	[SerializeField]
	private GameObjectRef rocketStoragePrefab;

	[SerializeField]
	private Transform rocketStoragePoint;

	[SerializeField]
	private GameObjectRef dashboardStoragePrefab;

	[SerializeField]
	private Transform dashboardStoragePoint;

	[SerializeField]
	private Collider[] movingColliders;

	[Header("MLRS Rotation")]
	[SerializeField]
	private Transform hRotator;

	[SerializeField]
	private float hRotSpeed = 25f;

	[SerializeField]
	private Transform vRotator;

	[SerializeField]
	private float vRotSpeed = 10f;

	[SerializeField]
	[Range(50f, 90f)]
	private float vRotMax = 85f;

	[SerializeField]
	private Transform hydraulics;

	[Header("MLRS Weaponry")]
	[Tooltip("Minimum distance from the MLRS to a targeted hit point. In metres.")]
	[SerializeField]
	public float minRange = 200f;

	[Tooltip("The size of the area the the rockets may hit, at the target point.")]
	[SerializeField]
	public float targetAreaRadius = 10f;

	[SerializeField]
	private GameObjectRef mlrsRocket;

	[SerializeField]
	public Transform firingPoint;

	[SerializeField]
	private RocketTube[] rocketTubes;

	[Header("MLRS Dashboard/FX")]
	[SerializeField]
	private GameObject screensChild;

	[SerializeField]
	private Transform leftHandGrip;

	[SerializeField]
	private Transform leftJoystick;

	[SerializeField]
	private Transform rightHandGrip;

	[SerializeField]
	private Transform rightJoystick;

	[SerializeField]
	private Transform controlKnobHeight;

	[SerializeField]
	private Transform controlKnobAngle;

	[SerializeField]
	private GameObjectRef uiDialogPrefab;

	[SerializeField]
	private Light fireButtonLight;

	[SerializeField]
	private GameObject brokenDownEffect;

	[SerializeField]
	private ParticleSystem topScreenShutdown;

	[SerializeField]
	private ParticleSystem bottomScreenShutdown;

	[ServerVar(Help = "How many minutes before the MLRS recovers from use and can be used again")]
	public static float brokenDownMinutes = 10f;

	public const Flags FLAG_FIRING_ROCKETS = Flags.Reserved6;

	public const Flags FLAG_HAS_AIMING_MODULE = Flags.Reserved8;

	private EntityRef rocketStorageInstance;

	private EntityRef dashboardStorageInstance;

	private float rocketBaseGravity;

	private float rocketSpeed;

	private float rocketDamageRadius;

	private bool isInitialLoad = true;

	public Vector3 UserTargetHitPos { get; private set; }

	public Vector3 TrueHitPos { get; private set; }

	public bool HasAimingModule => HasFlag(Flags.Reserved8);

	private bool CanBeUsed
	{
		get
		{
			if (HasAimingModule)
			{
				return !IsBroken();
			}
			return false;
		}
	}

	private bool CanFire
	{
		get
		{
			if (CanBeUsed && RocketAmmoCount > 0 && !IsFiringRockets)
			{
				return !IsRealigning;
			}
			return false;
		}
	}

	private float HRotation
	{
		get
		{
			return hRotator.eulerAngles.y;
		}
		set
		{
			Vector3 eulerAngles = hRotator.eulerAngles;
			eulerAngles.y = value;
			hRotator.eulerAngles = eulerAngles;
		}
	}

	private float VRotation
	{
		get
		{
			return vRotator.localEulerAngles.x;
		}
		set
		{
			Vector3 localEulerAngles = vRotator.localEulerAngles;
			if (value < 0f)
			{
				localEulerAngles.x = Mathf.Clamp(value, 0f - vRotMax, 0f);
			}
			else if (value > 0f)
			{
				localEulerAngles.x = Mathf.Clamp(value, 360f - vRotMax, 360f);
			}
			vRotator.localEulerAngles = localEulerAngles;
		}
	}

	public float CurGravityMultiplier { get; private set; }

	public int RocketAmmoCount { get; private set; }

	public bool IsRealigning { get; private set; }

	public bool IsFiringRockets => HasFlag(Flags.Reserved6);

	public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
	{
		using (TimeWarning.New("MLRS.OnRpcMessage"))
		{
			RPCMessage rPCMessage;
			if (rpc == 455279877 && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log(string.Concat("SV_RPCMessage: ", player, " - RPC_Fire_Rockets "));
				}
				using (TimeWarning.New("RPC_Fire_Rockets"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.MaxDistance.Test(455279877u, "RPC_Fire_Rockets", this, player, 3f))
						{
							return true;
						}
					}
					try
					{
						using (TimeWarning.New("Call"))
						{
							rPCMessage = default(RPCMessage);
							rPCMessage.connection = msg.connection;
							rPCMessage.player = player;
							rPCMessage.read = msg.read;
							RPCMessage msg2 = rPCMessage;
							RPC_Fire_Rockets(msg2);
						}
					}
					catch (Exception exception)
					{
						Debug.LogException(exception);
						player.Kick("RPC Error in RPC_Fire_Rockets");
					}
				}
				return true;
			}
			if (rpc == 751446792 && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log(string.Concat("SV_RPCMessage: ", player, " - RPC_Open_Dashboard "));
				}
				using (TimeWarning.New("RPC_Open_Dashboard"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.MaxDistance.Test(751446792u, "RPC_Open_Dashboard", this, player, 3f))
						{
							return true;
						}
					}
					try
					{
						using (TimeWarning.New("Call"))
						{
							rPCMessage = default(RPCMessage);
							rPCMessage.connection = msg.connection;
							rPCMessage.player = player;
							rPCMessage.read = msg.read;
							RPCMessage msg3 = rPCMessage;
							RPC_Open_Dashboard(msg3);
						}
					}
					catch (Exception exception2)
					{
						Debug.LogException(exception2);
						player.Kick("RPC Error in RPC_Open_Dashboard");
					}
				}
				return true;
			}
			if (rpc == 1311007340 && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log(string.Concat("SV_RPCMessage: ", player, " - RPC_Open_Rockets "));
				}
				using (TimeWarning.New("RPC_Open_Rockets"))
				{
					try
					{
						using (TimeWarning.New("Call"))
						{
							rPCMessage = default(RPCMessage);
							rPCMessage.connection = msg.connection;
							rPCMessage.player = player;
							rPCMessage.read = msg.read;
							RPCMessage msg4 = rPCMessage;
							RPC_Open_Rockets(msg4);
						}
					}
					catch (Exception exception3)
					{
						Debug.LogException(exception3);
						player.Kick("RPC Error in RPC_Open_Rockets");
					}
				}
				return true;
			}
			if (rpc == 858951307 && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log(string.Concat("SV_RPCMessage: ", player, " - RPC_SetTargetHitPos "));
				}
				using (TimeWarning.New("RPC_SetTargetHitPos"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.MaxDistance.Test(858951307u, "RPC_SetTargetHitPos", this, player, 3f))
						{
							return true;
						}
					}
					try
					{
						using (TimeWarning.New("Call"))
						{
							rPCMessage = default(RPCMessage);
							rPCMessage.connection = msg.connection;
							rPCMessage.player = player;
							rPCMessage.read = msg.read;
							RPCMessage msg5 = rPCMessage;
							RPC_SetTargetHitPos(msg5);
						}
					}
					catch (Exception exception4)
					{
						Debug.LogException(exception4);
						player.Kick("RPC Error in RPC_SetTargetHitPos");
					}
				}
				return true;
			}
		}
		return base.OnRpcMessage(player, rpc, msg);
	}

	public override void Spawn()
	{
		base.Spawn();
		if (!Rust.Application.isLoadingSave)
		{
			if (!rocketStorageInstance.IsValid(base.isServer))
			{
				BaseEntity baseEntity = GameManager.server.CreateEntity(rocketStoragePrefab.resourcePath, rocketStoragePoint.localPosition, rocketStoragePoint.localRotation);
				rocketStorageInstance.Set(baseEntity);
				baseEntity.SetParent(this);
				baseEntity.Spawn();
			}
			if (!dashboardStorageInstance.IsValid(base.isServer))
			{
				BaseEntity baseEntity2 = GameManager.server.CreateEntity(dashboardStoragePrefab.resourcePath, rocketStoragePoint.localPosition, rocketStoragePoint.localRotation);
				dashboardStorageInstance.Set(baseEntity2);
				baseEntity2.SetParent(this);
				baseEntity2.Spawn();
			}
		}
	}

	public override void VehicleFixedUpdate()
	{
		base.VehicleFixedUpdate();
		Item item;
		if (IsBroken())
		{
			if (!((float)timeSinceBroken >= brokenDownMinutes * 60f))
			{
				SetFlag(Flags.Reserved8, TryGetAimingModule(out item));
				return;
			}
			SetRepaired();
		}
		bool b = TryGetAimingModule(out item);
		SetFlag(Flags.Reserved8, b);
		int rocketAmmoCount = RocketAmmoCount;
		RocketAmmoCount = GetRocketContainer().inventory.GetAmmoAmount(AmmoTypes.MLRS_ROCKET);
		if (CanBeUsed && IsMounted())
		{
			Vector3 userTargetHitPos = UserTargetHitPos;
			userTargetHitPos += Vector3.forward * upDownInput * 75f * UnityEngine.Time.fixedDeltaTime;
			userTargetHitPos += Vector3.right * leftRightInput * 75f * UnityEngine.Time.fixedDeltaTime;
			SetUserTargetHitPos(userTargetHitPos);
		}
		if (!IsFiringRockets)
		{
			float hRot;
			float vRot;
			float g;
			HitPosToRotation(trueTargetHitPos, out hRot, out vRot, out g);
			float num = g / (0f - UnityEngine.Physics.gravity.y);
			IsRealigning = Mathf.DeltaAngle(VRotation, vRot) > 0.1f || Mathf.DeltaAngle(HRotation, hRot) > 0.1f || !Mathf.Approximately(CurGravityMultiplier, num);
			if (IsRealigning)
			{
				if (isInitialLoad)
				{
					VRotation = vRot;
					HRotation = hRot;
					isInitialLoad = false;
				}
				else
				{
					VRotation = Mathf.MoveTowardsAngle(VRotation, vRot, UnityEngine.Time.deltaTime * vRotSpeed);
					HRotation = Mathf.MoveTowardsAngle(HRotation, hRot, UnityEngine.Time.deltaTime * hRotSpeed);
				}
				CurGravityMultiplier = num;
				TrueHitPos = GetTrueHitPos();
			}
		}
		if (UserTargetHitPos != lastSentTargetHitPos || TrueHitPos != lastSentTrueHitPos || RocketAmmoCount != rocketAmmoCount)
		{
			SendNetworkUpdate();
		}
	}

	private Vector3 GetTrueHitPos()
	{
		TheoreticalProjectile projectile = new TheoreticalProjectile(firingPoint.position, firingPoint.forward.normalized * rocketSpeed, CurGravityMultiplier);
		int num = 0;
		float dt = ((projectile.forward.y > 0f) ? 3f : 2f);
		while (!NextRayHitSomething(ref projectile, dt) && (float)num < 128f)
		{
			num++;
		}
		return projectile.pos;
	}

	private bool NextRayHitSomething(ref TheoreticalProjectile projectile, float dt)
	{
		float num = UnityEngine.Physics.gravity.y * projectile.gravityMult;
		Vector3 pos = projectile.pos;
		float num2 = projectile.forward.MagnitudeXZ() * dt;
		float y = projectile.forward.y * dt + num * dt * dt * 0.5f;
		Vector2 vector = projectile.forward.XZ2D().normalized * num2;
		Vector3 vector2 = new Vector3(vector.x, y, vector.y);
		projectile.pos += vector2;
		float y2 = projectile.forward.y + num * dt;
		projectile.forward.y = y2;
		RaycastHit hitInfo;
		if (UnityEngine.Physics.Linecast(pos, projectile.pos, out hitInfo, 1084293393, QueryTriggerInteraction.Ignore))
		{
			projectile.pos = hitInfo.point;
			BaseEntity entity = RaycastHitEx.GetEntity(hitInfo);
			bool num3 = entity != null && entity.EqualNetID(this);
			if (num3)
			{
				projectile.pos += projectile.forward * 1f;
			}
			return !num3;
		}
		return false;
	}

	private float GetSurfaceHeight(Vector3 pos)
	{
		float height = TerrainMeta.HeightMap.GetHeight(pos);
		float height2 = TerrainMeta.WaterMap.GetHeight(pos);
		return Mathf.Max(height, height2);
	}

	private void SetRepaired()
	{
		SetFlag(Flags.Broken, false);
	}

	public override void PlayerServerInput(InputState inputState, BasePlayer player)
	{
		if (inputState.IsDown(BUTTON.FORWARD))
		{
			upDownInput = 1f;
		}
		else if (inputState.IsDown(BUTTON.BACKWARD))
		{
			upDownInput = -1f;
		}
		else
		{
			upDownInput = 0f;
		}
		if (inputState.IsDown(BUTTON.LEFT))
		{
			leftRightInput = -1f;
		}
		else if (inputState.IsDown(BUTTON.RIGHT))
		{
			leftRightInput = 1f;
		}
		else
		{
			leftRightInput = 0f;
		}
	}

	public override void Save(SaveInfo info)
	{
		base.Save(info);
		info.msg.mlrs = Facepunch.Pool.Get<ProtoBuf.MLRS>();
		info.msg.mlrs.targetPos = UserTargetHitPos;
		info.msg.mlrs.curHitPos = TrueHitPos;
		info.msg.mlrs.rocketStorageID = rocketStorageInstance.uid;
		info.msg.mlrs.dashboardStorageID = dashboardStorageInstance.uid;
		info.msg.mlrs.ammoCount = (uint)RocketAmmoCount;
		lastSentTargetHitPos = UserTargetHitPos;
		lastSentTrueHitPos = TrueHitPos;
	}

	public bool AdminFixUp()
	{
		if (IsDead() || IsFiringRockets)
		{
			return false;
		}
		StorageContainer dashboardContainer = GetDashboardContainer();
		if (!HasAimingModule)
		{
			dashboardContainer.inventory.AddItem(ItemManager.FindItemDefinition("aiming.module.mlrs"), 1, 0uL);
		}
		StorageContainer rocketContainer = GetRocketContainer();
		ItemDefinition itemDefinition = ItemManager.FindItemDefinition("ammo.rocket.mlrs");
		if (RocketAmmoCount < rocketContainer.inventory.capacity * itemDefinition.stackable)
		{
			int num = itemDefinition.stackable * rocketContainer.inventory.capacity - RocketAmmoCount;
			while (num > 0)
			{
				int num2 = Mathf.Min(num, itemDefinition.stackable);
				rocketContainer.inventory.AddItem(itemDefinition, itemDefinition.stackable, 0uL);
				num -= num2;
			}
		}
		SetRepaired();
		SendNetworkUpdate();
		return true;
	}

	private void Fire(BasePlayer owner)
	{
		if (CanFire && !IsFiringRockets && !(_mounted == null))
		{
			nextRocketIndex = Mathf.Min(RocketAmmoCount - 1, rocketTubes.Length - 1);
			rocketOwnerRef.Set(owner);
			SetFlag(Flags.Reserved6, true);
			radiusModIndex = 0;
			InvokeRepeating(FireNextRocket, 0f, 0.5f);
		}
	}

	private void EndFiring()
	{
		CancelInvoke(FireNextRocket);
		rocketOwnerRef.Set(null);
		Item item;
		if (TryGetAimingModule(out item))
		{
			item.LoseCondition(1f);
		}
		SetFlag(Flags.Reserved6, false, false, false);
		SetFlag(Flags.Broken, true, false, false);
		SendNetworkUpdate_Flags();
		timeSinceBroken = 0f;
	}

	private void FireNextRocket()
	{
		if (nextRocketIndex < 0)
		{
			EndFiring();
			return;
		}
		StorageContainer rocketContainer = GetRocketContainer();
		Vector3 firingPos = firingPoint.position + firingPoint.rotation * rocketTubes[nextRocketIndex].firingOffset;
		float num = 1f;
		if (radiusModIndex < radiusMods.Length)
		{
			num = radiusMods[radiusModIndex];
		}
		radiusModIndex++;
		Vector2 vector = UnityEngine.Random.insideUnitCircle * (targetAreaRadius - rocketDamageRadius) * num;
		Vector3 targetPos = TrueHitPos + new Vector3(vector.x, 0f, vector.y);
		float g;
		Vector3 aimToTarget = GetAimToTarget(targetPos, out g);
		ServerProjectile projectile;
		if (BaseMountable.TryFireProjectile(rocketContainer, AmmoTypes.MLRS_ROCKET, firingPos, aimToTarget, _mounted, 0f, 0f, out projectile))
		{
			projectile.gravityModifier = g / (0f - UnityEngine.Physics.gravity.y);
			nextRocketIndex--;
		}
		else
		{
			EndFiring();
		}
	}

	private bool TryGetAimingModule(out Item item)
	{
		ItemContainer inventory = GetDashboardContainer().inventory;
		if (!inventory.IsEmpty())
		{
			item = inventory.itemList[0];
			return true;
		}
		item = null;
		return false;
	}

	[RPC_Server]
	[RPC_Server.MaxDistance(3f)]
	public void RPC_SetTargetHitPos(RPCMessage msg)
	{
		BasePlayer player = msg.player;
		if (PlayerIsMounted(player))
		{
			SetUserTargetHitPos(msg.read.Vector3());
		}
	}

	[RPC_Server]
	[RPC_Server.MaxDistance(3f)]
	public void RPC_Fire_Rockets(RPCMessage msg)
	{
		BasePlayer player = msg.player;
		if (PlayerIsMounted(player))
		{
			Fire(player);
		}
	}

	[RPC_Server]
	public void RPC_Open_Rockets(RPCMessage msg)
	{
		BasePlayer player = msg.player;
		if (!(player == null) && CanBeLooted(player))
		{
			IItemContainerEntity rocketContainer = GetRocketContainer();
			if (!ObjectEx.IsUnityNull(rocketContainer))
			{
				rocketContainer.PlayerOpenLoot(player, "", false);
			}
			else
			{
				Debug.LogError(GetType().Name + ": No container component found.");
			}
		}
	}

	[RPC_Server]
	[RPC_Server.MaxDistance(3f)]
	public void RPC_Open_Dashboard(RPCMessage msg)
	{
		BasePlayer player = msg.player;
		if (!(player == null) && CanBeLooted(player))
		{
			IItemContainerEntity dashboardContainer = GetDashboardContainer();
			if (!ObjectEx.IsUnityNull(dashboardContainer))
			{
				dashboardContainer.PlayerOpenLoot(player);
			}
			else
			{
				Debug.LogError(GetType().Name + ": No container component found.");
			}
		}
	}

	public override void InitShared()
	{
		base.InitShared();
		GameObject obj = mlrsRocket.Get();
		ServerProjectile component = obj.GetComponent<ServerProjectile>();
		rocketBaseGravity = (0f - UnityEngine.Physics.gravity.y) * component.gravityModifier;
		rocketSpeed = component.speed;
		TimedExplosive component2 = obj.GetComponent<TimedExplosive>();
		rocketDamageRadius = component2.explosionRadius;
	}

	public override void Load(LoadInfo info)
	{
		base.Load(info);
		if (info.msg.mlrs != null)
		{
			SetUserTargetHitPos(info.msg.mlrs.targetPos);
			TrueHitPos = info.msg.mlrs.curHitPos;
			float hRot;
			float vRot;
			float g;
			HitPosToRotation(TrueHitPos, out hRot, out vRot, out g);
			CurGravityMultiplier = g / (0f - UnityEngine.Physics.gravity.y);
			if (base.isServer)
			{
				HRotation = hRot;
				VRotation = vRot;
			}
			rocketStorageInstance.uid = info.msg.mlrs.rocketStorageID;
			dashboardStorageInstance.uid = info.msg.mlrs.dashboardStorageID;
			RocketAmmoCount = (int)info.msg.mlrs.ammoCount;
		}
	}

	public override bool CanBeLooted(BasePlayer player)
	{
		return !IsFiringRockets;
	}

	public bool CanStickTo(Collider collider)
	{
		for (int i = 0; i < movingColliders.Length; i++)
		{
			if (movingColliders[i] == collider)
			{
				return false;
			}
		}
		return true;
	}

	private void SetUserTargetHitPos(Vector3 worldPos)
	{
		if (UserTargetHitPos == worldPos)
		{
			return;
		}
		if (base.isServer)
		{
			Vector3 position = TerrainMeta.Position;
			Vector3 vector = position + TerrainMeta.Size;
			worldPos.x = Mathf.Clamp(worldPos.x, position.x, vector.x);
			worldPos.z = Mathf.Clamp(worldPos.z, position.z, vector.z);
			worldPos.y = GetSurfaceHeight(worldPos);
		}
		UserTargetHitPos = worldPos;
		if (!base.isServer)
		{
			return;
		}
		trueTargetHitPos = UserTargetHitPos;
		foreach (TriggerSafeZone allSafeZone in TriggerSafeZone.allSafeZones)
		{
			Vector3 vector2 = allSafeZone.transform.position + ColliderEx.GetLocalCentre(allSafeZone.triggerCollider);
			vector2.y = 0f;
			float num = ColliderEx.GetRadius(allSafeZone.triggerCollider, allSafeZone.transform.localScale) + targetAreaRadius;
			trueTargetHitPos.y = 0f;
			if (Vector3.Distance(vector2, trueTargetHitPos) < num)
			{
				Vector3 vector3 = trueTargetHitPos - vector2;
				trueTargetHitPos = vector2 + vector3.normalized * num;
				trueTargetHitPos.y = GetSurfaceHeight(trueTargetHitPos);
				break;
			}
		}
	}

	private StorageContainer GetRocketContainer()
	{
		BaseEntity baseEntity = rocketStorageInstance.Get(base.isServer);
		if (baseEntity != null && BaseEntityEx.IsValid(baseEntity))
		{
			return baseEntity as StorageContainer;
		}
		return null;
	}

	private StorageContainer GetDashboardContainer()
	{
		BaseEntity baseEntity = dashboardStorageInstance.Get(base.isServer);
		if (baseEntity != null && BaseEntityEx.IsValid(baseEntity))
		{
			return baseEntity as StorageContainer;
		}
		return null;
	}

	private void HitPosToRotation(Vector3 hitPos, out float hRot, out float vRot, out float g)
	{
		Vector3 aimToTarget = GetAimToTarget(hitPos, out g);
		Vector3 eulerAngles = Quaternion.LookRotation(aimToTarget, Vector3.up).eulerAngles;
		vRot = eulerAngles.x - 360f;
		aimToTarget.y = 0f;
		hRot = eulerAngles.y;
	}

	private Vector3 GetAimToTarget(Vector3 targetPos, out float g)
	{
		g = rocketBaseGravity;
		float num = rocketSpeed;
		Vector3 vector = targetPos - firingPoint.position;
		float num2 = vector.Magnitude2D();
		float y = vector.y;
		float num3 = Mathf.Sqrt(num * num * num * num - g * (g * (num2 * num2) + 2f * y * num * num));
		float num4 = Mathf.Atan((num * num + num3) / (g * num2)) * 57.29578f;
		float num5 = Mathf.Clamp(num4, 0f, 90f);
		if (float.IsNaN(num4))
		{
			num5 = 45f;
			g = ProjectileDistToGravity(num2, y, num5, num);
		}
		else if (num4 > vRotMax)
		{
			num5 = vRotMax;
			g = ProjectileDistToGravity(Mathf.Max(num2, minRange), y, num5, num);
		}
		vector.Normalize();
		vector.y = 0f;
		Vector3 axis = Vector3.Cross(vector, Vector3.up);
		vector = Quaternion.AngleAxis(num5, axis) * vector;
		return vector;
	}

	private static float ProjectileDistToSpeed(float x, float y, float angle, float g, float fallbackV)
	{
		float num = angle * ((float)Math.PI / 180f);
		float num2 = Mathf.Sqrt(x * x * g / (x * Mathf.Sin(2f * num) - 2f * y * Mathf.Cos(num) * Mathf.Cos(num)));
		if (float.IsNaN(num2) || num2 < 1f)
		{
			num2 = fallbackV;
		}
		return num2;
	}

	private static float ProjectileDistToGravity(float x, float y, float θ, float v)
	{
		float num = θ * ((float)Math.PI / 180f);
		float num2 = (v * v * x * Mathf.Sin(2f * num) - 2f * v * v * y * Mathf.Cos(num) * Mathf.Cos(num)) / (x * x);
		if (float.IsNaN(num2) || num2 < 0.01f)
		{
			num2 = 0f - UnityEngine.Physics.gravity.y;
		}
		return num2;
	}
}
