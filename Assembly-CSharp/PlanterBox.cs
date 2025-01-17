#define UNITY_ASSERTIONS
using System;
using System.Collections.Generic;
using ConVar;
using Facepunch;
using Network;
using ProtoBuf;
using Rust;
using UnityEngine;
using UnityEngine.Assertions;

public class PlanterBox : StorageContainer, ISplashable
{
	public int soilSaturation;

	public int soilSaturationMax = 8000;

	public MeshRenderer soilRenderer;

	private static readonly float MinimumSaturationTriggerLevel = ConVar.Server.optimalPlanterQualitySaturation - 0.2f;

	private static readonly float MaximumSaturationTriggerLevel = ConVar.Server.optimalPlanterQualitySaturation + 0.1f;

	public TimeCachedValue<float> sunExposure;

	public TimeCachedValue<float> artificialLightExposure;

	public TimeCachedValue<float> plantTemperature;

	public TimeCachedValue<float> plantArtificalTemperature;

	private TimeSince lastSplashNetworkUpdate;

	private TimeSince lastRainCheck;

	public float soilSaturationFraction => (float)soilSaturation / (float)soilSaturationMax;

	public int availableIdealWaterCapacity => Mathf.Max(availableIdealWaterCapacity, Mathf.Max(idealSaturation - soilSaturation, 0));

	public int availableWaterCapacity => soilSaturationMax - soilSaturation;

	public int idealSaturation => Mathf.FloorToInt((float)soilSaturationMax * ConVar.Server.optimalPlanterQualitySaturation);

	public bool BelowMinimumSaturationTriggerLevel => soilSaturationFraction < MinimumSaturationTriggerLevel;

	public bool AboveMaximumSaturationTriggerLevel => soilSaturationFraction > MaximumSaturationTriggerLevel;

	public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
	{
		using (TimeWarning.New("PlanterBox.OnRpcMessage"))
		{
			if (rpc == 2965786167u && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log("SV_RPCMessage: " + player?.ToString() + " - RPC_RequestSaturationUpdate ");
				}
				using (TimeWarning.New("RPC_RequestSaturationUpdate"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.MaxDistance.Test(2965786167u, "RPC_RequestSaturationUpdate", this, player, 3f))
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
							RPC_RequestSaturationUpdate(msg2);
						}
					}
					catch (Exception exception)
					{
						Debug.LogException(exception);
						player.Kick("RPC Error in RPC_RequestSaturationUpdate");
					}
				}
				return true;
			}
		}
		return base.OnRpcMessage(player, rpc, msg);
	}

	public override void ServerInit()
	{
		base.ServerInit();
		base.inventory.onItemAddedRemoved = OnItemAddedOrRemoved;
		base.inventory.SetOnlyAllowedItem(allowedItem);
		ItemContainer itemContainer = base.inventory;
		itemContainer.canAcceptItem = (Func<Item, int, bool>)Delegate.Combine(itemContainer.canAcceptItem, new Func<Item, int, bool>(InventoryItemFilter));
		sunExposure = new TimeCachedValue<float>
		{
			refreshCooldown = 30f,
			refreshRandomRange = 5f,
			updateValue = CalculateSunExposure
		};
		artificialLightExposure = new TimeCachedValue<float>
		{
			refreshCooldown = 60f,
			refreshRandomRange = 5f,
			updateValue = CalculateArtificialLightExposure
		};
		plantTemperature = new TimeCachedValue<float>
		{
			refreshCooldown = 20f,
			refreshRandomRange = 5f,
			updateValue = CalculatePlantTemperature
		};
		plantArtificalTemperature = new TimeCachedValue<float>
		{
			refreshCooldown = 60f,
			refreshRandomRange = 5f,
			updateValue = CalculateArtificialTemperature
		};
		lastRainCheck = 0f;
		InvokeRandomized(CalculateRainFactor, 20f, 30f, 15f);
	}

	public override void OnItemAddedOrRemoved(Item item, bool added)
	{
		base.OnItemAddedOrRemoved(item, added);
		if (added && ItemIsFertilizer(item))
		{
			FertilizeGrowables();
		}
	}

	public bool InventoryItemFilter(Item item, int targetSlot)
	{
		if (item == null)
		{
			return false;
		}
		if (ItemIsFertilizer(item))
		{
			return true;
		}
		return false;
	}

	public override bool CanPickup(BasePlayer player)
	{
		if (base.CanPickup(player))
		{
			return !HasPlants();
		}
		return false;
	}

	private bool ItemIsFertilizer(Item item)
	{
		return item.info.shortname == "fertilizer";
	}

	public override void Save(SaveInfo info)
	{
		base.Save(info);
		info.msg.resource = Facepunch.Pool.Get<BaseResource>();
		info.msg.resource.stage = soilSaturation;
	}

	public override void Load(LoadInfo info)
	{
		base.Load(info);
		if (info.msg.resource != null)
		{
			soilSaturation = info.msg.resource.stage;
		}
	}

	public void FertilizeGrowables()
	{
		int num = GetFertilizerCount();
		if (num <= 0)
		{
			return;
		}
		foreach (BaseEntity child in children)
		{
			if (child == null)
			{
				continue;
			}
			GrowableEntity growableEntity = child as GrowableEntity;
			if (!(growableEntity == null) && !growableEntity.Fertilized && ConsumeFertilizer())
			{
				growableEntity.Fertilize();
				num--;
				if (num == 0)
				{
					break;
				}
			}
		}
	}

	public int GetFertilizerCount()
	{
		int num = 0;
		for (int i = 0; i < base.inventory.capacity; i++)
		{
			Item slot = base.inventory.GetSlot(i);
			if (slot != null && ItemIsFertilizer(slot))
			{
				num += slot.amount;
			}
		}
		return num;
	}

	public bool ConsumeFertilizer()
	{
		for (int i = 0; i < base.inventory.capacity; i++)
		{
			Item slot = base.inventory.GetSlot(i);
			if (slot != null && ItemIsFertilizer(slot))
			{
				int num = Mathf.Min(1, slot.amount);
				if (num > 0)
				{
					slot.UseItem(num);
					return true;
				}
			}
		}
		return false;
	}

	public int ConsumeWater(int amount, GrowableEntity ignoreEntity = null)
	{
		int num = Mathf.Min(amount, soilSaturation);
		soilSaturation -= num;
		RefreshGrowables(ignoreEntity);
		SendNetworkUpdate();
		return num;
	}

	public bool WantsSplash(ItemDefinition splashType, int amount)
	{
		if (base.IsDestroyed)
		{
			return false;
		}
		if (splashType == null || splashType.shortname == null)
		{
			return false;
		}
		if (!(splashType.shortname == "water.salt"))
		{
			return soilSaturation < soilSaturationMax;
		}
		return true;
	}

	public int DoSplash(ItemDefinition splashType, int amount)
	{
		if (splashType.shortname == "water.salt")
		{
			soilSaturation = 0;
			RefreshGrowables();
			if ((float)lastSplashNetworkUpdate > 60f)
			{
				SendNetworkUpdate();
				lastSplashNetworkUpdate = 0f;
			}
			return amount;
		}
		int num = Mathf.Min(availableWaterCapacity, amount);
		soilSaturation += num;
		RefreshGrowables();
		if ((float)lastSplashNetworkUpdate > 60f)
		{
			SendNetworkUpdate();
			lastSplashNetworkUpdate = 0f;
		}
		return num;
	}

	private void RefreshGrowables(GrowableEntity ignoreEntity = null)
	{
		if (children == null)
		{
			return;
		}
		foreach (BaseEntity child in children)
		{
			if (!(child == null) && !(child == ignoreEntity) && child is GrowableEntity growableEntity)
			{
				growableEntity.QueueForQualityUpdate();
			}
		}
	}

	public void ForceLightUpdate()
	{
		sunExposure?.ForceNextRun();
		artificialLightExposure?.ForceNextRun();
	}

	public void ForceTemperatureUpdate()
	{
		plantArtificalTemperature?.ForceNextRun();
	}

	public float GetSunExposure()
	{
		return sunExposure?.Get(force: false) ?? 0f;
	}

	private float CalculateSunExposure()
	{
		return GrowableEntity.SunRaycast(base.transform.position + new Vector3(0f, 1f, 0f));
	}

	public float GetArtificialLightExposure()
	{
		return artificialLightExposure?.Get(force: false) ?? 0f;
	}

	private float CalculateArtificialLightExposure()
	{
		return GrowableEntity.CalculateArtificialLightExposure(base.transform);
	}

	public float GetPlantTemperature()
	{
		return (plantTemperature?.Get(force: false) ?? 0f) + (plantArtificalTemperature?.Get(force: false) ?? 0f);
	}

	private float CalculatePlantTemperature()
	{
		return Mathf.Max(Climate.GetTemperature(base.transform.position), 15f);
	}

	private bool HasPlants()
	{
		foreach (BaseEntity child in children)
		{
			if (child is GrowableEntity)
			{
				return true;
			}
		}
		return false;
	}

	private void CalculateRainFactor()
	{
		if (sunExposure.Get(force: false) > 0f)
		{
			float rain = Climate.GetRain(base.transform.position);
			if (rain > 0f)
			{
				soilSaturation = Mathf.Clamp(soilSaturation + Mathf.RoundToInt(4f * rain * (float)lastRainCheck), 0, soilSaturationMax);
				RefreshGrowables();
				SendNetworkUpdate();
			}
		}
		lastRainCheck = 0f;
	}

	private float CalculateArtificialTemperature()
	{
		return GrowableEntity.CalculateArtificialTemperature(base.transform);
	}

	public void OnPlantInserted(GrowableEntity entity, BasePlayer byPlayer)
	{
		if (!Rust.GameInfo.HasAchievements)
		{
			return;
		}
		List<uint> obj = Facepunch.Pool.GetList<uint>();
		foreach (BaseEntity child in children)
		{
			if (child is GrowableEntity growableEntity && !obj.Contains(growableEntity.prefabID))
			{
				obj.Add(growableEntity.prefabID);
			}
		}
		if (obj.Count == 9)
		{
			byPlayer.GiveAchievement("HONEST_WORK");
		}
		Facepunch.Pool.FreeList(ref obj);
	}

	[RPC_Server]
	[RPC_Server.MaxDistance(3f)]
	private void RPC_RequestSaturationUpdate(RPCMessage msg)
	{
		if (msg.player != null)
		{
			ClientRPCPlayer(null, msg.player, "RPC_ReceiveSaturationUpdate", soilSaturation);
		}
	}

	public override bool SupportsChildDeployables()
	{
		return true;
	}
}
