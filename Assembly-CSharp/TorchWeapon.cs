#define UNITY_ASSERTIONS
using System;
using ConVar;
using Network;
using Rust;
using UnityEngine;
using UnityEngine.Assertions;

public class TorchWeapon : BaseMelee
{
	[NonSerialized]
	public const float FuelTickAmount = 1f / 12f;

	[Header("TorchWeapon")]
	public AnimatorOverrideController LitHoldAnimationOverride;

	public bool ExtinguishUnderwater = true;

	public bool UseTurnOnOffAnimations;

	public GameObjectRef litStrikeFX;

	public const Flags IsInHolder = Flags.Reserved1;

	public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
	{
		using (TimeWarning.New("TorchWeapon.OnRpcMessage"))
		{
			if (rpc == 2235491565u && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log("SV_RPCMessage: " + player?.ToString() + " - Extinguish ");
				}
				using (TimeWarning.New("Extinguish"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.IsActiveItem.Test(2235491565u, "Extinguish", this, player))
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
							Extinguish(msg2);
						}
					}
					catch (Exception exception)
					{
						Debug.LogException(exception);
						player.Kick("RPC Error in Extinguish");
					}
				}
				return true;
			}
			if (rpc == 3010584743u && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (ConVar.Global.developer > 2)
				{
					Debug.Log("SV_RPCMessage: " + player?.ToString() + " - Ignite ");
				}
				using (TimeWarning.New("Ignite"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.IsActiveItem.Test(3010584743u, "Ignite", this, player))
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
							Ignite(msg3);
						}
					}
					catch (Exception exception2)
					{
						Debug.LogException(exception2);
						player.Kick("RPC Error in Ignite");
					}
				}
				return true;
			}
		}
		return base.OnRpcMessage(player, rpc, msg);
	}

	public override void GetAttackStats(HitInfo info)
	{
		base.GetAttackStats(info);
		if (HasFlag(Flags.On))
		{
			info.damageTypes.Add(DamageType.Heat, 1f);
		}
	}

	public override float GetConditionLoss()
	{
		return base.GetConditionLoss() + (HasFlag(Flags.On) ? 6f : 0f);
	}

	public void SetIsOn(bool isOn)
	{
		if (isOn)
		{
			SetFlag(Flags.On, b: true);
			InvokeRepeating(UseFuel, 1f, 1f);
		}
		else
		{
			SetFlag(Flags.On, b: false);
			CancelInvoke(UseFuel);
		}
	}

	[RPC_Server.IsActiveItem]
	[RPC_Server]
	private void Ignite(RPCMessage msg)
	{
		if (msg.player.CanInteract())
		{
			SetIsOn(isOn: true);
		}
	}

	[RPC_Server.IsActiveItem]
	[RPC_Server]
	private void Extinguish(RPCMessage msg)
	{
		if (msg.player.CanInteract())
		{
			SetIsOn(isOn: false);
		}
	}

	public void UseFuel()
	{
		GetOwnerItem()?.LoseCondition(1f / 12f);
	}

	public override void OnHeldChanged()
	{
		if (IsDisabled())
		{
			SetFlag(Flags.On, b: false);
			CancelInvoke(UseFuel);
		}
	}

	public override string GetStrikeEffectPath(string materialName)
	{
		for (int i = 0; i < materialStrikeFX.Count; i++)
		{
			if (materialStrikeFX[i].materialName == materialName && materialStrikeFX[i].fx.isValid)
			{
				return materialStrikeFX[i].fx.resourcePath;
			}
		}
		if (HasFlag(Flags.On) && litStrikeFX.isValid)
		{
			return litStrikeFX.resourcePath;
		}
		return strikeFX.resourcePath;
	}
}
