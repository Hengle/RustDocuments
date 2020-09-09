#define UNITY_ASSERTIONS
using ConVar;
using Facepunch;
using Network;
using ProtoBuf;
using Rust;
using Rust.Modular;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

public class BaseVehicleModule : BaseCombatEntity, IPrefabPreProcess
{
	public enum VisualGroup
	{
		None,
		Engine,
		Cabin,
		Flatbed
	}

	[Serializable]
	public class LODLevel
	{
		public Renderer[] renderers;
	}

	public Item AssociatedItemInstance;

	private TimeSince timeSinceItemLockRefresh;

	private const float TIME_BETWEEN_LOCK_REFRESH = 1f;

	[Header("Vehicle Module")]
	[SerializeField]
	private Transform centreOfMassTransform;

	[SerializeField]
	private float mass = 100f;

	public VisualGroup visualGroup;

	[HideInInspector]
	[SerializeField]
	private ModularVehicleLight[] lights;

	public LODLevel[] lodRenderers;

	[SerializeField]
	[HideInInspector]
	private List<ConditionalObject> conditionals;

	[Header("Trigger Parent")]
	[SerializeField]
	private TriggerParent[] triggerParents;

	[Header("Sliding Components")]
	[SerializeField]
	private VehicleModuleSlidingComponent[] slidingComponents;

	[SerializeField]
	private VehicleModuleButtonComponent[] buttonComponents;

	[SerializeField]
	[HideInInspector]
	private DamageRenderer damageRenderer;

	private TimeSince TimeSinceAddedToVehicle;

	private float prevRefreshHealth = -1f;

	private bool prevRefreshVehicleIsDead;

	private bool prevRefreshVehicleIsLockable;

	public bool PropagateDamage
	{
		get;
		private set;
	} = true;


	public BaseModularVehicle Vehicle
	{
		get;
		private set;
	}

	public int FirstSocketIndex
	{
		get;
		private set;
	} = -1;


	public Vector3 CentreOfMass => centreOfMassTransform.localPosition;

	public float Mass => mass;

	public uint ID => net.ID;

	public bool IsOnAVehicle => Vehicle != null;

	public ItemDefinition AssociatedItemDef => repair.itemTarget;

	public virtual bool HasSeating => false;

	public virtual bool HasAnEngine => false;

	public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
	{
		using (TimeWarning.New("BaseVehicleModule.OnRpcMessage"))
		{
			if (rpc == 2683376664u && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log("SV_RPCMessage: " + player + " - RPC_Use ");
				}
				using (TimeWarning.New("RPC_Use"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.MaxDistance.Test(2683376664u, "RPC_Use", this, player, 3f))
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
							RPC_Use(msg2);
						}
					}
					catch (Exception exception)
					{
						Debug.LogException(exception);
						player.Kick("RPC Error in RPC_Use");
					}
				}
				return true;
			}
		}
		return base.OnRpcMessage(player, rpc, msg);
	}

	public virtual void NonUserSpawn()
	{
	}

	public virtual void PlayerServerInput(InputState inputState, BasePlayer player)
	{
	}

	public virtual void VehicleFixedUpdate(bool vehicleIsActive)
	{
		if (Vehicle.IsEditableNow && AssociatedItemInstance != null && (float)timeSinceItemLockRefresh > 1f)
		{
			AssociatedItemInstance.LockUnlock(!CanBeMovedNow());
			timeSinceItemLockRefresh = 0f;
		}
		for (int i = 0; i < slidingComponents.Length; i++)
		{
			slidingComponents[i].ServerUpdateTick(this);
		}
	}

	public override void Hurt(HitInfo info)
	{
		if (IsOnAVehicle)
		{
			Vehicle.ModuleHurt(this, info);
		}
		base.Hurt(info);
	}

	public override void OnHealthChanged(float oldValue, float newValue)
	{
		base.OnHealthChanged(oldValue, newValue);
		if (!base.isServer)
		{
			return;
		}
		if (IsOnAVehicle)
		{
			if (Vehicle.IsDead())
			{
				return;
			}
			if (AssociatedItemInstance != null)
			{
				AssociatedItemInstance.condition = Health();
			}
			if (newValue <= 0f)
			{
				Vehicle.ModuleReachedZeroHealth();
			}
		}
		RefreshConditionals(true);
	}

	public bool CanBeMovedNow()
	{
		if (IsOnAVehicle)
		{
			return CanBeMovedNowOnVehicle();
		}
		return true;
	}

	protected virtual bool CanBeMovedNowOnVehicle()
	{
		return true;
	}

	public virtual float GetAdjustedDriveForce(float absSpeed, float topSpeed)
	{
		return 0f;
	}

	public void AcceptPropagatedDamage(float amount, DamageType type, BaseEntity attacker = null, bool useProtection = true)
	{
		PropagateDamage = false;
		Hurt(amount, type, attacker, useProtection);
		PropagateDamage = true;
	}

	public virtual void AdminFixUp(int tier)
	{
		SetHealth(MaxHealth());
		SendNetworkUpdate();
	}

	[RPC_Server.MaxDistance(3f)]
	[RPC_Server]
	public void RPC_Use(RPCMessage msg)
	{
		BasePlayer player = msg.player;
		if (!CanBeUsedNowBy(player))
		{
			return;
		}
		string lookingAtColldierName = msg.read.String();
		VehicleModuleSlidingComponent[] array = slidingComponents;
		foreach (VehicleModuleSlidingComponent vehicleModuleSlidingComponent in array)
		{
			if (PlayerIsLookingAtUsable(lookingAtColldierName, vehicleModuleSlidingComponent.interactionColliderName))
			{
				vehicleModuleSlidingComponent.Use(this);
				break;
			}
		}
		VehicleModuleButtonComponent[] array2 = buttonComponents;
		int i = 0;
		VehicleModuleButtonComponent vehicleModuleButtonComponent;
		while (true)
		{
			if (i < array2.Length)
			{
				vehicleModuleButtonComponent = array2[i];
				if (PlayerIsLookingAtUsable(lookingAtColldierName, vehicleModuleButtonComponent.interactionColliderName))
				{
					break;
				}
				i++;
				continue;
			}
			return;
		}
		vehicleModuleButtonComponent.ServerUse(player, this);
	}

	public virtual void ScaleDamageForPlayer(BasePlayer player, HitInfo info)
	{
	}

	public override void AdminKill()
	{
		if (IsOnAVehicle)
		{
			Vehicle.AdminKill();
		}
	}

	public virtual void OnPlayerDismountedVehicle(BasePlayer player)
	{
	}

	public override void Save(SaveInfo info)
	{
		base.Save(info);
		info.msg.vehicleModule = Facepunch.Pool.Get<VehicleModule>();
		info.msg.vehicleModule.socketIndex = FirstSocketIndex;
	}

	public override void PreProcess(IPrefabProcessor process, GameObject rootObj, string name, bool serverside, bool clientside, bool bundling)
	{
		base.PreProcess(process, rootObj, name, serverside, clientside, bundling);
		damageRenderer = GetComponent<DamageRenderer>();
		RefreshParameters();
		lights = GetComponentsInChildren<ModularVehicleLight>();
	}

	public void RefreshParameters()
	{
		for (int num = conditionals.Count - 1; num >= 0; num--)
		{
			ConditionalObject conditionalObject = conditionals[num];
			if (conditionalObject.gameObject == null)
			{
				conditionals.RemoveAt(num);
			}
			else if (conditionalObject.restrictOnHealth)
			{
				conditionalObject.healthRestrictionMin = Mathf.Clamp01(conditionalObject.healthRestrictionMin);
				conditionalObject.healthRestrictionMax = Mathf.Clamp01(conditionalObject.healthRestrictionMax);
			}
			conditionalObject.gibId = -1;
			if (conditionalObject.gameObject != null)
			{
				Gibbable component = conditionalObject.gameObject.GetComponent<Gibbable>();
				if (component != null)
				{
					component.uniqueId = num;
					conditionalObject.gibId = num;
				}
			}
		}
	}

	public virtual void ModuleAdded(BaseModularVehicle vehicle, int firstSocketIndex)
	{
		Vehicle = vehicle;
		FirstSocketIndex = firstSocketIndex;
		TimeSinceAddedToVehicle = 0f;
		if (base.isServer)
		{
			TriggerParent[] array = triggerParents;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].associatedMountable = vehicle;
			}
			SendNetworkUpdate();
		}
		RefreshConditionals(false);
	}

	public virtual void ModuleRemoved()
	{
		Vehicle = null;
		FirstSocketIndex = -1;
		if (base.isServer)
		{
			TriggerParent[] array = triggerParents;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].associatedMountable = null;
			}
			SendNetworkUpdate();
		}
	}

	public void OtherVehicleModulesChanged()
	{
		RefreshConditionals(false);
	}

	public virtual void OnEngineStateChanged(ModularCar.EngineState oldState, ModularCar.EngineState newState)
	{
	}

	public override float MaxHealth()
	{
		if (AssociatedItemDef != null)
		{
			return AssociatedItemDef.condition.max;
		}
		return base.MaxHealth();
	}

	public override float StartHealth()
	{
		return MaxHealth();
	}

	public int GetNumSocketsTaken()
	{
		if (AssociatedItemDef == null)
		{
			return 1;
		}
		return AssociatedItemDef.GetComponent<ItemModVehicleModule>().socketsTaken;
	}

	public List<ConditionalObject> GetConditionals()
	{
		List<ConditionalObject> list = new List<ConditionalObject>();
		foreach (ConditionalObject conditional in conditionals)
		{
			if (conditional.gameObject != null)
			{
				list.Add(conditional);
			}
		}
		return list;
	}

	public virtual float GetMaxDriveForce()
	{
		return 0f;
	}

	public void RefreshConditionals(bool canGib)
	{
		if (IsOnAVehicle && Vehicle.HasInited)
		{
			foreach (ConditionalObject conditional in conditionals)
			{
				RefreshConditional(conditional, canGib);
			}
			prevRefreshHealth = Health();
			prevRefreshVehicleIsDead = Vehicle.IsDead();
			prevRefreshVehicleIsLockable = Vehicle.IsLockable;
		}
	}

	private void RefreshConditional(ConditionalObject conditional, bool canGib)
	{
		if (conditional.gameObject == null)
		{
			return;
		}
		bool flag = true;
		if (conditional.restrictOnHealth)
		{
			flag = ((!Mathf.Approximately(conditional.healthRestrictionMin, conditional.healthRestrictionMax)) ? (base.healthFraction > conditional.healthRestrictionMin && base.healthFraction <= conditional.healthRestrictionMax) : Mathf.Approximately(base.healthFraction, conditional.healthRestrictionMin));
			if (!canGib)
			{
			}
		}
		if (flag && IsOnAVehicle && conditional.restrictOnLockable)
		{
			flag = (Vehicle.IsLockable == conditional.lockableRestriction);
		}
		if (flag && conditional.restrictOnAdjacent)
		{
			bool flag2 = false;
			bool flag3 = false;
			BaseVehicleModule result;
			if (TryGetAdjacentModuleInFront(out result))
			{
				flag2 = InSameVisualGroupAs(result);
			}
			if (TryGetAdjacentModuleBehind(out result))
			{
				flag3 = InSameVisualGroupAs(result);
			}
			switch (conditional.adjacentRestriction)
			{
			case ConditionalObject.AdjacentCondition.BothDifferent:
				flag = (!flag2 && !flag3);
				break;
			case ConditionalObject.AdjacentCondition.SameInFront:
				flag = flag2;
				break;
			case ConditionalObject.AdjacentCondition.SameBehind:
				flag = flag3;
				break;
			case ConditionalObject.AdjacentCondition.DifferentInFront:
				flag = !flag2;
				break;
			case ConditionalObject.AdjacentCondition.DifferentBehind:
				flag = !flag3;
				break;
			case ConditionalObject.AdjacentCondition.BothSame:
				flag = (flag2 && flag3);
				break;
			}
		}
		if (flag)
		{
			if (!IsOnAVehicle)
			{
				for (int i = 0; i < conditional.socketSettings.Length; i++)
				{
					flag = !conditional.socketSettings[i].HasSocketRestrictions;
					if (!flag)
					{
						break;
					}
				}
			}
			else
			{
				for (int j = 0; j < conditional.socketSettings.Length; j++)
				{
					flag = Vehicle.GetSocket(FirstSocketIndex + j).ShouldBeActive(conditional.socketSettings[j]);
					if (!flag)
					{
						break;
					}
				}
			}
		}
		bool activeInHierarchy = conditional.gameObject.activeInHierarchy;
		conditional.SetActive(flag);
	}

	private bool TryGetAdjacentModuleInFront(out BaseVehicleModule result)
	{
		if (!IsOnAVehicle)
		{
			result = null;
			return false;
		}
		int socketIndex = FirstSocketIndex - 1;
		return Vehicle.TryGetModuleAt(socketIndex, out result);
	}

	private bool TryGetAdjacentModuleBehind(out BaseVehicleModule result)
	{
		if (!IsOnAVehicle)
		{
			result = null;
			return false;
		}
		int num = FirstSocketIndex + GetNumSocketsTaken() - 1;
		return Vehicle.TryGetModuleAt(num + 1, out result);
	}

	private bool InSameVisualGroupAs(BaseVehicleModule moduleEntity)
	{
		if (visualGroup == VisualGroup.None || moduleEntity == null)
		{
			return false;
		}
		if (moduleEntity.prefabID != prefabID)
		{
			return moduleEntity.visualGroup == visualGroup;
		}
		return true;
	}

	private bool CanBeUsedNowBy(BasePlayer player)
	{
		if (!IsOnAVehicle || player == null)
		{
			return false;
		}
		if (IsOnAVehicle && !Vehicle.IsEditableNow && !Vehicle.IsDead())
		{
			return player.GetMountedVehicle() == Vehicle;
		}
		return false;
	}

	public bool PlayerIsLookingAtUsable(string lookingAtColldierName, string usableColliderName)
	{
		return string.Compare(lookingAtColldierName, usableColliderName, true) == 0;
	}

	public override void Load(LoadInfo info)
	{
		base.Load(info);
		if (info.msg.vehicleModule != null && FirstSocketIndex < 0 && info.msg.vehicleModule.socketIndex >= 0)
		{
			FirstSocketIndex = info.msg.vehicleModule.socketIndex;
		}
	}
}