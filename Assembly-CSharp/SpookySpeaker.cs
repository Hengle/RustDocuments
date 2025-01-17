#define UNITY_ASSERTIONS
using System;
using ConVar;
using Network;
using UnityEngine;
using UnityEngine.Assertions;

public class SpookySpeaker : IOEntity
{
	public SoundPlayer soundPlayer;

	public float soundSpacing = 12f;

	public float soundSpacingRand = 5f;

	public override bool OnRpcMessage(BasePlayer player, uint rpc, Message msg)
	{
		using (TimeWarning.New("SpookySpeaker.OnRpcMessage"))
		{
			if (rpc == 2523893445u && player != null)
			{
				Assert.IsTrue(player.isServer, "SV_RPC Message is using a clientside player!");
				if (Global.developer > 2)
				{
					Debug.Log("SV_RPCMessage: " + player?.ToString() + " - SetWantsOn ");
				}
				using (TimeWarning.New("SetWantsOn"))
				{
					using (TimeWarning.New("Conditions"))
					{
						if (!RPC_Server.IsVisible.Test(2523893445u, "SetWantsOn", this, player, 3f))
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
							RPCMessage wantsOn = rPCMessage;
							SetWantsOn(wantsOn);
						}
					}
					catch (Exception exception)
					{
						Debug.LogException(exception);
						player.Kick("RPC Error in SetWantsOn");
					}
				}
				return true;
			}
		}
		return base.OnRpcMessage(player, rpc, msg);
	}

	public override void PostServerLoad()
	{
		base.PostServerLoad();
		UpdateInvokes();
	}

	public override void UpdateHasPower(int inputAmount, int inputSlot)
	{
		base.UpdateHasPower(inputAmount, inputSlot);
		if (inputSlot == 1)
		{
			SetTargetState(state: false);
		}
		if (inputSlot == 0)
		{
			SetTargetState(state: true);
		}
	}

	private void SetTargetState(bool state)
	{
		SetFlag(Flags.On, state);
		UpdateInvokes();
	}

	[RPC_Server.IsVisible(3f)]
	[RPC_Server]
	public void SetWantsOn(RPCMessage msg)
	{
		bool targetState = msg.read.Bit();
		SetTargetState(targetState);
	}

	public void UpdateInvokes()
	{
		if (IsOn())
		{
			InvokeRandomized(SendPlaySound, soundSpacing, soundSpacing, soundSpacingRand);
			Invoke(DelayedOff, 7200f);
		}
		else
		{
			CancelInvoke(SendPlaySound);
			CancelInvoke(DelayedOff);
		}
	}

	public void SendPlaySound()
	{
		ClientRPC(null, "PlaySpookySound");
	}

	public void DelayedOff()
	{
		SetFlag(Flags.On, b: false);
	}
}
