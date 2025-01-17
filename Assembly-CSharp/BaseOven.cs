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
using UnityEngine;
using UnityEngine.Assertions;

public class BaseOven : StorageContainer, ISplashable, IIndustrialStorage
{
	public enum TemperatureType
	{
		Normal = 0,
		Warming = 1,
		Cooking = 2,
		Smelting = 3,
		Fractioning = 4
	}

	public enum IndustrialSlotMode
	{
		Furnace = 0,
		LargeFurnace = 1,
		OilRefinery = 2,
		ElectricFurnace = 3
	}

	public struct MinMax
	{
		public int Min;

		public int Max;

		public MinMax(int min, int max)
		{
			Min = min;
			Max = max;
		}
	}

	public enum OvenItemType
	{
		Burnable = 0,
		Byproduct = 1,
		MaterialInput = 2,
		MaterialOutput = 3
	}

	private static Dictionary<float, HashSet<ItemDefinition>> _materialOutputCache;

	public TemperatureType temperature;

	public Menu.Option switchOnMenu;

	public Menu.Option switchOffMenu;

	public ItemAmount[] startupContents;

	public bool allowByproductCreation = true;

	public ItemDefinition fuelType;

	public bool canModFire;

	public bool disabledBySplash = true;

	public int smeltSpeed = 1;

	public int fuelSlots = 1;

	public int inputSlots = 1;

	public int outputSlots = 1;

	public IndustrialSlotMode IndustrialMode;

	public int _activeCookingSlot = -1;

	public int _inputSlotIndex;

	public int _outputSlotIndex;

	public const float UpdateRate = 0.5f;

	public virtual bool CanRunWithNoFuel => false;

	public ItemContainer Container => base.inventory;

	public BaseEntity IndustrialEntity => this;

	public float cookingTemperature => temperature switch
	{
		TemperatureType.Fractioning => 1500f, 
		TemperatureType.Cooking => 200f, 
		TemperatureType.Smelting => 1000f, 
		TemperatureType.Warming => 50f, 
		_ => 15f, 
	};

	public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
	{
		using (TimeWarning.New("BaseOven.OnRpcMessage"))
		{
			if (rpc == 4167839872u && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (Global.developer > 2)
				{
					Debug.Log("SV_RPCMessage: " + player?.ToString() + " - SVSwitch ");
				}
				using (TimeWarning.New("SVSwitch"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.MaxDistance.Test(4167839872u, "SVSwitch", this, player, 3f))
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
							SVSwitch(msg2);
						}
					}
					catch (Exception exception)
					{
						Debug.LogException(exception);
						player.Kick("RPC Error in SVSwitch");
					}
				}
				return true;
			}
		}
		return base.OnRpcMessage(player, rpc, msg);
	}

	public override void PreInitShared()
	{
		base.PreInitShared();
		_inputSlotIndex = fuelSlots;
		_outputSlotIndex = _inputSlotIndex + inputSlots;
		_activeCookingSlot = _inputSlotIndex;
	}

	public override void ServerInit()
	{
		inventorySlots = fuelSlots + inputSlots + outputSlots;
		base.ServerInit();
	}

	public override void PostServerLoad()
	{
		base.PostServerLoad();
		if (IsOn())
		{
			StartCooking();
		}
	}

	public override void Save(SaveInfo info)
	{
		base.Save(info);
		if (!info.forDisk)
		{
			info.msg.baseOven = Facepunch.Pool.Get<ProtoBuf.BaseOven>();
			info.msg.baseOven.cookSpeed = GetSmeltingSpeed();
		}
	}

	public override void OnInventoryFirstCreated(ItemContainer container)
	{
		base.OnInventoryFirstCreated(container);
		if (startupContents != null)
		{
			ItemAmount[] array = startupContents;
			foreach (ItemAmount itemAmount in array)
			{
				ItemManager.Create(itemAmount.itemDef, (int)itemAmount.amount, 0uL).MoveToContainer(container);
			}
		}
	}

	public override void OnItemAddedOrRemoved(Item item, bool bAdded)
	{
		base.OnItemAddedOrRemoved(item, bAdded);
		if (item != null)
		{
			ItemModCookable component = item.info.GetComponent<ItemModCookable>();
			if (component != null)
			{
				item.cookTimeLeft = component.cookTime;
			}
			if (item.HasFlag(Item.Flag.OnFire))
			{
				item.SetFlag(Item.Flag.OnFire, b: false);
				item.MarkDirty();
			}
			if (item.HasFlag(Item.Flag.Cooking))
			{
				item.SetFlag(Item.Flag.Cooking, b: false);
				item.MarkDirty();
			}
		}
	}

	public override bool ItemFilter(Item item, int targetSlot)
	{
		if (!base.ItemFilter(item, targetSlot))
		{
			return false;
		}
		if (targetSlot == -1)
		{
			return false;
		}
		if (IsOutputItem(item) && item.GetEntityOwner() != this)
		{
			BaseEntity entityOwner = item.GetEntityOwner();
			if (entityOwner != this && entityOwner != null)
			{
				return false;
			}
		}
		MinMax? allowedSlots = GetAllowedSlots(item);
		if (!allowedSlots.HasValue)
		{
			return false;
		}
		if (targetSlot >= allowedSlots.Value.Min)
		{
			return targetSlot <= allowedSlots.Value.Max;
		}
		return false;
	}

	public MinMax? GetAllowedSlots(Item item)
	{
		int num = 0;
		int num2 = 0;
		if (IsBurnableItem(item))
		{
			num2 = fuelSlots;
		}
		else if (IsOutputItem(item))
		{
			num = _outputSlotIndex;
			num2 = num + outputSlots;
		}
		else
		{
			if (!IsMaterialInput(item))
			{
				return null;
			}
			num = _inputSlotIndex;
			num2 = num + inputSlots;
		}
		return new MinMax(num, num2 - 1);
	}

	public MinMax GetOutputSlotRange()
	{
		return new MinMax(_outputSlotIndex, _outputSlotIndex + outputSlots - 1);
	}

	public override int GetIdealSlot(BasePlayer player, Item item)
	{
		MinMax? allowedSlots = GetAllowedSlots(item);
		if (!allowedSlots.HasValue)
		{
			return -1;
		}
		for (int i = allowedSlots.Value.Min; i <= allowedSlots.Value.Max; i++)
		{
			Item slot = base.inventory.GetSlot(i);
			if (slot == null || (slot.CanStack(item) && slot.amount < slot.MaxStackable()))
			{
				return i;
			}
		}
		return base.GetIdealSlot(player, item);
	}

	public void OvenFull()
	{
		StopCooking();
	}

	public int GetFuelRate()
	{
		return 1;
	}

	public int GetCharcoalRate()
	{
		return 1;
	}

	public void Cook()
	{
		Item item = FindBurnable();
		if (Interface.CallHook("OnOvenCook", this, item) != null)
		{
			return;
		}
		if (item == null && !CanRunWithNoFuel)
		{
			StopCooking();
			return;
		}
		foreach (Item item2 in base.inventory.itemList)
		{
			if (item2.position >= _inputSlotIndex && item2.position < _inputSlotIndex + inputSlots && !item2.HasFlag(Item.Flag.Cooking))
			{
				item2.SetFlag(Item.Flag.Cooking, b: true);
				item2.MarkDirty();
			}
		}
		IncreaseCookTime(0.5f * GetSmeltingSpeed());
		BaseEntity slot = GetSlot(Slot.FireMod);
		if ((bool)slot)
		{
			slot.SendMessage("Cook", 0.5f, SendMessageOptions.DontRequireReceiver);
		}
		if (item != null)
		{
			ItemModBurnable component = item.info.GetComponent<ItemModBurnable>();
			item.fuel -= 0.5f * (cookingTemperature / 200f);
			if (!item.HasFlag(Item.Flag.OnFire))
			{
				item.SetFlag(Item.Flag.OnFire, b: true);
				item.MarkDirty();
			}
			if (item.fuel <= 0f)
			{
				ConsumeFuel(item, component);
			}
		}
		OnCooked();
		Interface.CallHook("OnOvenCooked", this, item, slot);
	}

	protected virtual void OnCooked()
	{
	}

	public void ConsumeFuel(Item fuel, ItemModBurnable burnable)
	{
		if (Interface.CallHook("OnFuelConsume", this, fuel, burnable) != null)
		{
			return;
		}
		if (allowByproductCreation && burnable.byproductItem != null && UnityEngine.Random.Range(0f, 1f) > burnable.byproductChance)
		{
			Item item = ItemManager.Create(burnable.byproductItem, burnable.byproductAmount * GetCharcoalRate(), 0uL);
			if (!item.MoveToContainer(base.inventory))
			{
				OvenFull();
				item.Drop(base.inventory.dropPosition, base.inventory.dropVelocity);
			}
		}
		if (fuel.amount <= GetFuelRate())
		{
			fuel.Remove();
			return;
		}
		int fuelRate = GetFuelRate();
		fuel.UseItem(fuelRate);
		Facepunch.Rust.Analytics.Azure.AddPendingItems(this, fuel.info.shortname, fuelRate, "smelt");
		fuel.fuel = burnable.fuelAmount;
		fuel.MarkDirty();
		Interface.CallHook("OnFuelConsumed", this, fuel, burnable);
	}

	[RPC_Server.MaxDistance(3f)]
	[RPC_Server]
	protected virtual void SVSwitch(RPCMessage msg)
	{
		bool flag = msg.read.Bit();
		if (Interface.CallHook("OnOvenToggle", this, msg.player) == null && flag != IsOn() && (!needsBuildingPrivilegeToUse || msg.player.CanBuild()))
		{
			if (flag)
			{
				StartCooking();
			}
			else
			{
				StopCooking();
			}
		}
	}

	public float GetTemperature(int slot)
	{
		object obj = Interface.CallHook("OnOvenTemperature", this, slot);
		if (obj is float)
		{
			return (float)obj;
		}
		if (!HasFlag(Flags.On))
		{
			return 15f;
		}
		return cookingTemperature;
	}

	public void UpdateAttachmentTemperature()
	{
		BaseEntity slot = GetSlot(Slot.FireMod);
		if ((bool)slot)
		{
			slot.SendMessage("ParentTemperatureUpdate", base.inventory.temperature, SendMessageOptions.DontRequireReceiver);
		}
	}

	public virtual void StartCooking()
	{
		if (Interface.CallHook("OnOvenStart", this) == null && (FindBurnable() != null || CanRunWithNoFuel))
		{
			base.inventory.temperature = cookingTemperature;
			UpdateAttachmentTemperature();
			InvokeRepeating(Cook, 0.5f, 0.5f);
			SetFlag(Flags.On, b: true);
			Interface.CallHook("OnOvenStarted", this);
		}
	}

	public virtual void StopCooking()
	{
		UpdateAttachmentTemperature();
		if (base.inventory != null)
		{
			base.inventory.temperature = 15f;
			foreach (Item item in base.inventory.itemList)
			{
				if (item.HasFlag(Item.Flag.OnFire))
				{
					item.SetFlag(Item.Flag.OnFire, b: false);
					item.MarkDirty();
				}
				else if (item.HasFlag(Item.Flag.Cooking))
				{
					item.SetFlag(Item.Flag.Cooking, b: false);
					item.MarkDirty();
				}
			}
		}
		CancelInvoke(Cook);
		SetFlag(Flags.On, b: false);
	}

	public bool WantsSplash(ItemDefinition splashType, int amount)
	{
		if (!base.IsDestroyed && IsOn())
		{
			return disabledBySplash;
		}
		return false;
	}

	public int DoSplash(ItemDefinition splashType, int amount)
	{
		StopCooking();
		return Mathf.Min(200, amount);
	}

	public Item FindBurnable()
	{
		object obj = Interface.CallHook("OnFindBurnable", this);
		if (obj is Item)
		{
			return (Item)obj;
		}
		if (base.inventory == null)
		{
			return null;
		}
		foreach (Item item in base.inventory.itemList)
		{
			if (IsBurnableItem(item))
			{
				return item;
			}
		}
		return null;
	}

	public void IncreaseCookTime(float amount)
	{
		List<Item> obj = Facepunch.Pool.GetList<Item>();
		foreach (Item item in base.inventory.itemList)
		{
			if (item.HasFlag(Item.Flag.Cooking))
			{
				obj.Add(item);
			}
		}
		float delta = amount / (float)obj.Count;
		foreach (Item item2 in obj)
		{
			item2.OnCycle(delta);
		}
		Facepunch.Pool.FreeList(ref obj);
	}

	public Vector2i InputSlotRange(int slotIndex)
	{
		if (IndustrialMode == IndustrialSlotMode.LargeFurnace)
		{
			return new Vector2i(0, 6);
		}
		if (IndustrialMode == IndustrialSlotMode.OilRefinery)
		{
			return new Vector2i(0, 1);
		}
		if (IndustrialMode == IndustrialSlotMode.ElectricFurnace)
		{
			return new Vector2i(0, 1);
		}
		return new Vector2i(0, 2);
	}

	public Vector2i OutputSlotRange(int slotIndex)
	{
		if (IndustrialMode == IndustrialSlotMode.LargeFurnace)
		{
			return new Vector2i(7, 16);
		}
		if (IndustrialMode == IndustrialSlotMode.OilRefinery)
		{
			return new Vector2i(2, 4);
		}
		if (IndustrialMode == IndustrialSlotMode.ElectricFurnace)
		{
			return new Vector2i(2, 4);
		}
		return new Vector2i(3, 5);
	}

	public void OnStorageItemTransferBegin()
	{
	}

	public void OnStorageItemTransferEnd()
	{
	}

	public float GetSmeltingSpeed()
	{
		if (base.isServer)
		{
			return smeltSpeed;
		}
		throw new Exception("No way it should be able to get here?");
	}

	public bool IsBurnableItem(Item item)
	{
		if ((bool)item.info.GetComponent<ItemModBurnable>() && (fuelType == null || item.info == fuelType))
		{
			return true;
		}
		return false;
	}

	public bool IsBurnableByproduct(Item item)
	{
		ItemModBurnable itemModBurnable = fuelType?.GetComponent<ItemModBurnable>();
		if (itemModBurnable == null)
		{
			return false;
		}
		return item.info == itemModBurnable.byproductItem;
	}

	public bool IsMaterialInput(Item item)
	{
		ItemModCookable component = item.info.GetComponent<ItemModCookable>();
		if (component == null || (float)component.lowTemp > cookingTemperature || (float)component.highTemp < cookingTemperature)
		{
			return false;
		}
		return true;
	}

	public bool IsMaterialOutput(Item item)
	{
		if (_materialOutputCache == null)
		{
			BuildMaterialOutputCache();
		}
		if (!_materialOutputCache.TryGetValue(cookingTemperature, out var value))
		{
			Debug.LogError("Can't find smeltable item list for oven");
			return true;
		}
		return value.Contains(item.info);
	}

	public bool IsOutputItem(Item item)
	{
		if (!IsMaterialOutput(item))
		{
			return IsBurnableByproduct(item);
		}
		return true;
	}

	private void BuildMaterialOutputCache()
	{
		_materialOutputCache = new Dictionary<float, HashSet<ItemDefinition>>();
		float[] array = (from x in GameManager.server.preProcessed.prefabList.Values
			select x.GetComponent<BaseOven>() into x
			where x != null
			select x.cookingTemperature).Distinct().ToArray();
		foreach (float key in array)
		{
			HashSet<ItemDefinition> hashSet = new HashSet<ItemDefinition>();
			_materialOutputCache[key] = hashSet;
			foreach (ItemDefinition item in ItemManager.itemList)
			{
				ItemModCookable component = item.GetComponent<ItemModCookable>();
				if (!(component == null) && component.CanBeCookedByAtTemperature(key))
				{
					hashSet.Add(component.becomeOnCooked);
				}
			}
		}
	}

	public override bool HasSlot(Slot slot)
	{
		if (canModFire && slot == Slot.FireMod)
		{
			return true;
		}
		return base.HasSlot(slot);
	}

	public override bool SupportsChildDeployables()
	{
		return true;
	}

	public override bool CanPickup(BasePlayer player)
	{
		if (base.CanPickup(player))
		{
			return CanPickupOven();
		}
		return false;
	}

	protected virtual bool CanPickupOven()
	{
		return children.Count == 0;
	}
}
