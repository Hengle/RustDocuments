#define UNITY_ASSERTIONS
using System;
using System.Collections.Generic;
using System.Linq;
using ConVar;
using Facepunch;
using Facepunch.Extend;
using Network;
using Oxide.Core;
using ProtoBuf;
using Rust;
using Rust.UI;
using UnityEngine;
using UnityEngine.Assertions;

public class MarketTerminal : StorageContainer
{
	public const Flags Flag_HasItems = Flags.Reserved1;

	public const Flags Flag_InventoryFull = Flags.Reserved2;

	[Header("Market Terminal")]
	public GameObjectRef menuPrefab;

	public ulong lockToCustomerDuration = 300uL;

	public ulong orderTimeout = 180uL;

	public ItemDefinition deliveryFeeCurrency;

	public int deliveryFeeAmount;

	public DeliveryDroneConfig config;

	public RustText userLabel;

	private ulong _customerSteamId;

	private string _customerName;

	private TimeUntil _timeUntilCustomerExpiry;

	private EntityRef<Marketplace> _marketplace;

	public List<ProtoBuf.MarketTerminal.PendingOrder> pendingOrders;

	public Action<BasePlayer, Item> _onCurrencyRemovedCached;

	public Action<BasePlayer, Item> _onItemPurchasedCached;

	private Action _checkForExpiredOrdersCached;

	private bool _transactionActive;

	private static readonly List<NetworkableId> _deliveryEligible = new List<NetworkableId>(128);

	private static RealTimeSince _deliveryEligibleLastCalculated;

	public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
	{
		using (TimeWarning.New("MarketTerminal.OnRpcMessage"))
		{
			if (rpc == 3793918752u && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log("SV_RPCMessage: " + player?.ToString() + " - Server_Purchase ");
				}
				using (TimeWarning.New("Server_Purchase"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.CallsPerSecond.Test(3793918752u, "Server_Purchase", this, player, 10uL))
						{
							return true;
						}
						if (!RPC_Server.IsVisible.Test(3793918752u, "Server_Purchase", this, player, 3f))
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
							Server_Purchase(msg2);
						}
					}
					catch (Exception exception)
					{
						Debug.LogException(exception);
						player.Kick("RPC Error in Server_Purchase");
					}
				}
				return true;
			}
			if (rpc == 1382511247 && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log("SV_RPCMessage: " + player?.ToString() + " - Server_TryOpenMarket ");
				}
				using (TimeWarning.New("Server_TryOpenMarket"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.CallsPerSecond.Test(1382511247u, "Server_TryOpenMarket", this, player, 3uL))
						{
							return true;
						}
						if (!RPC_Server.IsVisible.Test(1382511247u, "Server_TryOpenMarket", this, player, 3f))
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
							Server_TryOpenMarket(msg3);
						}
					}
					catch (Exception exception2)
					{
						Debug.LogException(exception2);
						player.Kick("RPC Error in Server_TryOpenMarket");
					}
				}
				return true;
			}
		}
		return base.OnRpcMessage(player, rpc, msg);
	}

	public bool CanPlayerAffordOrderAndDeliveryFee(BasePlayer player, ProtoBuf.VendingMachine.SellOrder sellOrder, int numberOfTransactions)
	{
		int num = player.inventory.FindItemsByItemID(deliveryFeeCurrency.itemid).Sum((Item i) => i.amount);
		int num2 = deliveryFeeAmount;
		if (num < num2)
		{
			return false;
		}
		if (sellOrder != null)
		{
			int num3 = sellOrder.currencyAmountPerItem * numberOfTransactions;
			if (sellOrder.currencyID == deliveryFeeCurrency.itemid && !sellOrder.currencyIsBP && num < num2 + num3)
			{
				return false;
			}
		}
		return true;
	}

	public bool HasPendingOrderFor(NetworkableId vendingMachineId)
	{
		return pendingOrders?.FindWith((ProtoBuf.MarketTerminal.PendingOrder o) => o.vendingMachineId, vendingMachineId) != null;
	}

	public bool CanPlayerInteract(BasePlayer player)
	{
		if (player == null)
		{
			return false;
		}
		if (_customerSteamId == 0L || (float)_timeUntilCustomerExpiry <= 0f)
		{
			return true;
		}
		return player.userID == _customerSteamId;
	}

	public override void Load(LoadInfo info)
	{
		base.Load(info);
		if (info.msg.marketTerminal == null)
		{
			return;
		}
		_customerSteamId = info.msg.marketTerminal.customerSteamId;
		_customerName = info.msg.marketTerminal.customerName;
		_timeUntilCustomerExpiry = info.msg.marketTerminal.timeUntilExpiry;
		_marketplace = new EntityRef<Marketplace>(info.msg.marketTerminal.marketplaceId);
		if (pendingOrders == null)
		{
			pendingOrders = Facepunch.Pool.GetList<ProtoBuf.MarketTerminal.PendingOrder>();
		}
		if (pendingOrders.Count > 0)
		{
			foreach (ProtoBuf.MarketTerminal.PendingOrder pendingOrder in pendingOrders)
			{
				ProtoBuf.MarketTerminal.PendingOrder obj = pendingOrder;
				Facepunch.Pool.Free(ref obj);
			}
			pendingOrders.Clear();
		}
		foreach (ProtoBuf.MarketTerminal.PendingOrder order in info.msg.marketTerminal.orders)
		{
			ProtoBuf.MarketTerminal.PendingOrder item = order.Copy();
			pendingOrders.Add(item);
		}
	}

	public void Setup(Marketplace marketplace)
	{
		_marketplace = new EntityRef<Marketplace>(marketplace.net.ID);
	}

	public override void ServerInit()
	{
		base.ServerInit();
		_onCurrencyRemovedCached = OnCurrencyRemoved;
		_onItemPurchasedCached = OnItemPurchased;
		_checkForExpiredOrdersCached = CheckForExpiredOrders;
	}

	private void RegisterOrder(BasePlayer player, VendingMachine vendingMachine)
	{
		if (pendingOrders == null)
		{
			pendingOrders = Facepunch.Pool.GetList<ProtoBuf.MarketTerminal.PendingOrder>();
		}
		if (HasPendingOrderFor(vendingMachine.net.ID))
		{
			return;
		}
		if (!_marketplace.TryGet(serverside: true, out var entity))
		{
			Debug.LogError("Marketplace is not set", this);
			return;
		}
		NetworkableId droneId = entity.SendDrone(player, this, vendingMachine);
		if (!droneId.IsValid)
		{
			Debug.LogError("Failed to spawn delivery drone");
			return;
		}
		ProtoBuf.MarketTerminal.PendingOrder pendingOrder = Facepunch.Pool.Get<ProtoBuf.MarketTerminal.PendingOrder>();
		pendingOrder.vendingMachineId = vendingMachine.net.ID;
		pendingOrder.timeUntilExpiry = orderTimeout;
		pendingOrder.droneId = droneId;
		pendingOrders.Add(pendingOrder);
		CheckForExpiredOrders();
		UpdateHasItems(sendNetworkUpdate: false);
		SendNetworkUpdateImmediate();
	}

	public void CompleteOrder(NetworkableId vendingMachineId)
	{
		if (pendingOrders != null)
		{
			int num = pendingOrders.FindIndexWith((ProtoBuf.MarketTerminal.PendingOrder o) => o.vendingMachineId, vendingMachineId);
			if (num < 0)
			{
				Debug.LogError("Completed market order that doesn't exist?");
				return;
			}
			pendingOrders[num].Dispose();
			pendingOrders.RemoveAt(num);
			CheckForExpiredOrders();
			UpdateHasItems(sendNetworkUpdate: false);
			SendNetworkUpdateImmediate();
		}
	}

	private void CheckForExpiredOrders()
	{
		if (pendingOrders != null && pendingOrders.Count > 0)
		{
			bool flag = false;
			float? num = null;
			for (int i = 0; i < pendingOrders.Count; i++)
			{
				ProtoBuf.MarketTerminal.PendingOrder pendingOrder = pendingOrders[i];
				if ((float)pendingOrder.timeUntilExpiry <= 0f)
				{
					if (new EntityRef<DeliveryDrone>(pendingOrder.droneId).TryGet(serverside: true, out var entity))
					{
						Debug.LogError("Delivery timed out waiting for drone (too slow speed?)", this);
						entity.Kill();
					}
					else
					{
						Debug.LogError("Delivery timed out waiting for drone, and the drone seems to have been destroyed?", this);
					}
					pendingOrders.RemoveAt(i);
					i--;
					flag = true;
				}
				else if (!num.HasValue || (float)pendingOrder.timeUntilExpiry < num.Value)
				{
					num = pendingOrder.timeUntilExpiry;
				}
			}
			if (flag)
			{
				UpdateHasItems(sendNetworkUpdate: false);
				SendNetworkUpdate();
			}
			if (num.HasValue)
			{
				Invoke(_checkForExpiredOrdersCached, num.Value);
			}
		}
		else
		{
			CancelInvoke(_checkForExpiredOrdersCached);
		}
	}

	private void RestrictToPlayer(BasePlayer player)
	{
		if (_customerSteamId == player.userID)
		{
			_timeUntilCustomerExpiry = lockToCustomerDuration;
			SendNetworkUpdate();
			return;
		}
		if (_customerSteamId != 0L)
		{
			Debug.LogError("Overwriting player restriction! It should be cleared first.", this);
		}
		_customerSteamId = player.userID;
		_customerName = player.displayName;
		_timeUntilCustomerExpiry = lockToCustomerDuration;
		SendNetworkUpdateImmediate();
		ClientRPC(null, "Client_CloseMarketUI", _customerSteamId);
		RemoveAnyLooters();
		if (IsOpen())
		{
			Debug.LogError("Market terminal's inventory is still open after removing looters!", this);
		}
	}

	private void ClearRestriction()
	{
		if (_customerSteamId != 0L)
		{
			_customerSteamId = 0uL;
			_customerName = null;
			_timeUntilCustomerExpiry = 0f;
			SendNetworkUpdateImmediate();
		}
	}

	[RPC_Server]
	[RPC_Server.IsVisible(3f)]
	[RPC_Server.CallsPerSecond(3uL)]
	public void Server_TryOpenMarket(RPCMessage msg)
	{
		if (!CanPlayerInteract(msg.player))
		{
			return;
		}
		if (!_marketplace.IsValid(serverside: true))
		{
			Debug.LogError("Marketplace is not set", this);
			return;
		}
		using EntityIdList entityIdList = Facepunch.Pool.Get<EntityIdList>();
		entityIdList.entityIds = Facepunch.Pool.GetList<NetworkableId>();
		GetDeliveryEligibleVendingMachines(entityIdList.entityIds);
		ClientRPCPlayer(null, msg.player, "Client_OpenMarket", entityIdList);
	}

	[RPC_Server.CallsPerSecond(10uL)]
	[RPC_Server.IsVisible(3f)]
	[RPC_Server]
	public void Server_Purchase(RPCMessage msg)
	{
		if (!CanPlayerInteract(msg.player))
		{
			return;
		}
		if (!_marketplace.IsValid(serverside: true))
		{
			Debug.LogError("Marketplace is not set", this);
			return;
		}
		NetworkableId networkableId = msg.read.EntityID();
		int num = msg.read.Int32();
		int num2 = msg.read.Int32();
		VendingMachine vendingMachine = BaseNetworkable.serverEntities.Find(networkableId) as VendingMachine;
		if (vendingMachine == null || !BaseNetworkableEx.IsValid(vendingMachine) || num < 0 || num >= vendingMachine.sellOrders.sellOrders.Count || num2 <= 0 || base.inventory.IsFull())
		{
			return;
		}
		GetDeliveryEligibleVendingMachines(null);
		if (_deliveryEligible == null || !_deliveryEligible.Contains(networkableId))
		{
			return;
		}
		try
		{
			_transactionActive = true;
			int num3 = deliveryFeeAmount;
			ProtoBuf.VendingMachine.SellOrder sellOrder = vendingMachine.sellOrders.sellOrders[num];
			if (!CanPlayerAffordOrderAndDeliveryFee(msg.player, sellOrder, num2))
			{
				return;
			}
			int num4 = msg.player.inventory.Take(null, deliveryFeeCurrency.itemid, num3);
			if (num4 != num3)
			{
				Debug.LogError($"Took an incorrect number of items for the delivery fee (took {num4}, should have taken {num3})");
			}
			ClientRPCPlayer(null, msg.player, "Client_ShowItemNotice", deliveryFeeCurrency.itemid, -num3, arg3: false);
			if (!vendingMachine.DoTransaction(msg.player, num, num2, base.inventory, _onCurrencyRemovedCached, _onItemPurchasedCached, this))
			{
				Item item = ItemManager.CreateByItemID(deliveryFeeCurrency.itemid, num3, 0uL);
				if (!msg.player.inventory.GiveItem(item))
				{
					item.Drop(msg.player.inventory.containerMain.dropPosition, msg.player.inventory.containerMain.dropVelocity);
				}
			}
			else
			{
				RestrictToPlayer(msg.player);
				RegisterOrder(msg.player, vendingMachine);
			}
		}
		finally
		{
			_transactionActive = false;
		}
	}

	private void UpdateHasItems(bool sendNetworkUpdate = true)
	{
		if (!Rust.Application.isLoadingSave)
		{
			bool flag = base.inventory.itemList.Count > 0;
			bool flag2 = pendingOrders != null && pendingOrders.Count != 0;
			SetFlag(Flags.Reserved1, flag && !flag2, recursive: false, sendNetworkUpdate);
			SetFlag(Flags.Reserved2, base.inventory.IsFull(), recursive: false, sendNetworkUpdate);
			if (!flag && !flag2)
			{
				ClearRestriction();
			}
		}
	}

	private void OnCurrencyRemoved(BasePlayer player, Item currencyItem)
	{
		if (player != null && currencyItem != null)
		{
			ClientRPCPlayer(null, player, "Client_ShowItemNotice", currencyItem.info.itemid, -currencyItem.amount, arg3: false);
		}
	}

	private void OnItemPurchased(BasePlayer player, Item purchasedItem)
	{
		if (player != null && purchasedItem != null)
		{
			ClientRPCPlayer(null, player, "Client_ShowItemNotice", purchasedItem.info.itemid, purchasedItem.amount, arg3: true);
		}
	}

	public override void Save(SaveInfo info)
	{
		base.Save(info);
		info.msg.marketTerminal = Facepunch.Pool.Get<ProtoBuf.MarketTerminal>();
		info.msg.marketTerminal.customerSteamId = _customerSteamId;
		info.msg.marketTerminal.customerName = _customerName;
		info.msg.marketTerminal.timeUntilExpiry = _timeUntilCustomerExpiry;
		info.msg.marketTerminal.marketplaceId = _marketplace.uid;
		info.msg.marketTerminal.orders = Facepunch.Pool.GetList<ProtoBuf.MarketTerminal.PendingOrder>();
		if (pendingOrders == null)
		{
			return;
		}
		foreach (ProtoBuf.MarketTerminal.PendingOrder pendingOrder in pendingOrders)
		{
			ProtoBuf.MarketTerminal.PendingOrder item = pendingOrder.Copy();
			info.msg.marketTerminal.orders.Add(item);
		}
	}

	public override bool ItemFilter(Item item, int targetSlot)
	{
		if (_transactionActive)
		{
			return true;
		}
		if (item.parent == null)
		{
			return true;
		}
		if (item.parent == base.inventory)
		{
			return true;
		}
		return false;
	}

	public override void OnItemAddedOrRemoved(Item item, bool added)
	{
		UpdateHasItems();
	}

	public override bool CanOpenLootPanel(BasePlayer player, string panelName)
	{
		if (CanPlayerInteract(player) && HasFlag(Flags.Reserved1))
		{
			return base.CanOpenLootPanel(player, panelName);
		}
		return false;
	}

	private void RemoveAnyLooters()
	{
		ItemContainer item = base.inventory;
		foreach (BasePlayer activePlayer in BasePlayer.activePlayerList)
		{
			if (!(activePlayer == null) && !(activePlayer.inventory == null) && !(activePlayer.inventory.loot == null) && activePlayer.inventory.loot.containers.Contains(item))
			{
				activePlayer.inventory.loot.Clear();
			}
		}
	}

	public void GetDeliveryEligibleVendingMachines(List<NetworkableId> vendingMachineIds)
	{
		if ((float)_deliveryEligibleLastCalculated < 5f)
		{
			if (vendingMachineIds == null)
			{
				return;
			}
			{
				foreach (NetworkableId item in _deliveryEligible)
				{
					vendingMachineIds.Add(item);
				}
				return;
			}
		}
		_deliveryEligibleLastCalculated = 0f;
		_deliveryEligible.Clear();
		foreach (MapMarker serverMapMarker in MapMarker.serverMapMarkers)
		{
			if (serverMapMarker is VendingMachineMapMarker vendingMachineMapMarker && !(vendingMachineMapMarker.server_vendingMachine == null))
			{
				VendingMachine server_vendingMachine = vendingMachineMapMarker.server_vendingMachine;
				if (!(server_vendingMachine == null) && (IsEligible(server_vendingMachine, config.vendingMachineOffset, 1) || IsEligible(server_vendingMachine, config.vendingMachineOffset + Vector3.forward * config.maxDistanceFromVendingMachine, 2)))
				{
					_deliveryEligible.Add(server_vendingMachine.net.ID);
				}
			}
		}
		if (vendingMachineIds == null)
		{
			return;
		}
		foreach (NetworkableId item2 in _deliveryEligible)
		{
			vendingMachineIds.Add(item2);
		}
		bool IsEligible(VendingMachine vendingMachine, Vector3 offset, int n)
		{
			object obj = Interface.CallHook("CanAccessVendingMachine", config, vendingMachine);
			if (obj is bool)
			{
				return (bool)obj;
			}
			if (vendingMachine is NPCVendingMachine)
			{
				return true;
			}
			if (!vendingMachine.IsBroadcasting())
			{
				return false;
			}
			if (!config.IsVendingMachineAccessible(vendingMachine, offset, out var _))
			{
				return false;
			}
			return true;
		}
	}
}
