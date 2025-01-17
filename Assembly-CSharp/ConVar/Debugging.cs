using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Facepunch;
using Facepunch.Unity;
using Rust;
using UnityEngine;

namespace ConVar;

[Factory("debug")]
public class Debugging : ConsoleSystem
{
	[ClientVar]
	[ServerVar]
	public static bool checktriggers = false;

	[ServerVar]
	public static bool checkparentingtriggers = true;

	[ServerVar]
	[ClientVar(Saved = false, Help = "Shows some debug info for dismount attempts.")]
	public static bool DebugDismounts = false;

	[ServerVar(Help = "Do not damage any items")]
	public static bool disablecondition = false;

	[ServerVar]
	[ClientVar]
	public static bool callbacks = false;

	[ServerVar]
	[ClientVar]
	public static bool log
	{
		get
		{
			return Debug.unityLogger.logEnabled;
		}
		set
		{
			Debug.unityLogger.logEnabled = value;
		}
	}

	[ClientVar]
	[ServerVar]
	public static void renderinfo(Arg arg)
	{
		RenderInfo.GenerateReport();
	}

	[ServerVar]
	public static void enable_player_movement(Arg arg)
	{
		if (arg.IsAdmin)
		{
			bool @bool = arg.GetBool(0, def: true);
			BasePlayer basePlayer = ArgEx.Player(arg);
			if (basePlayer == null)
			{
				arg.ReplyWith("Must be called from client with player model");
				return;
			}
			basePlayer.ClientRPCPlayer(null, basePlayer, "TogglePlayerMovement", @bool);
			arg.ReplyWith((@bool ? "enabled" : "disabled") + " player movement");
		}
	}

	[ClientVar]
	[ServerVar]
	public static void stall(Arg arg)
	{
		float num = Mathf.Clamp(arg.GetFloat(0), 0f, 1f);
		arg.ReplyWith("Stalling for " + num + " seconds...");
		Thread.Sleep(Mathf.RoundToInt(num * 1000f));
	}

	[ServerVar(Help = "Repair all items in inventory")]
	public static void repair_inventory(Arg args)
	{
		BasePlayer basePlayer = ArgEx.Player(args);
		if (!basePlayer)
		{
			return;
		}
		Item[] array = basePlayer.inventory.AllItems();
		foreach (Item item in array)
		{
			if (item != null)
			{
				item.maxCondition = item.info.condition.max;
				item.condition = item.maxCondition;
				item.MarkDirty();
			}
			if (item.contents == null)
			{
				continue;
			}
			foreach (Item item2 in item.contents.itemList)
			{
				item2.maxCondition = item2.info.condition.max;
				item2.condition = item2.maxCondition;
				item2.MarkDirty();
			}
		}
	}

	[ServerVar]
	public static void spawnParachuteTester(Arg arg)
	{
		float @float = arg.GetFloat(0, 50f);
		BasePlayer basePlayer = ArgEx.Player(arg);
		BasePlayer basePlayer2 = GameManager.server.CreateEntity("assets/prefabs/player/player.prefab", basePlayer.transform.position + Vector3.up * @float, Quaternion.LookRotation(basePlayer.eyes.BodyForward())) as BasePlayer;
		basePlayer2.Spawn();
		basePlayer2.eyes.rotation = basePlayer.eyes.rotation;
		basePlayer2.SendNetworkUpdate();
		Inventory.copyTo(basePlayer, basePlayer2);
		if (!basePlayer2.HasValidParachuteEquipped())
		{
			basePlayer2.inventory.containerWear.GiveItem(ItemManager.CreateByName("parachute", 1, 0uL));
		}
		basePlayer2.RequestParachuteDeploy();
	}

	[ServerVar]
	public static void deleteEntitiesByShortname(Arg arg)
	{
		string text = arg.GetString(0).ToLower();
		float @float = arg.GetFloat(1);
		BasePlayer basePlayer = ArgEx.Player(arg);
		List<BaseNetworkable> obj = Facepunch.Pool.GetList<BaseNetworkable>();
		foreach (BaseNetworkable serverEntity in BaseNetworkable.serverEntities)
		{
			if (serverEntity.ShortPrefabName == text && (@float == 0f || (basePlayer != null && basePlayer.Distance(serverEntity as BaseEntity) <= @float)))
			{
				obj.Add(serverEntity);
			}
		}
		Debug.Log($"Deleting {obj.Count} {text}...");
		foreach (BaseNetworkable item in obj)
		{
			item.Kill();
		}
		Facepunch.Pool.FreeList(ref obj);
	}

	[ServerVar(Help = "Takes you in and out of your current network group, causing you to delete and then download all entities in your PVS again")]
	public static void flushgroup(Arg arg)
	{
		BasePlayer basePlayer = ArgEx.Player(arg);
		if (!(basePlayer == null))
		{
			basePlayer.net.SwitchGroup(BaseNetworkable.LimboNetworkGroup);
			basePlayer.UpdateNetworkGroup();
		}
	}

	[ServerVar(Help = "Break the current held object")]
	public static void breakheld(Arg arg)
	{
		Item activeItem = ArgEx.Player(arg).GetActiveItem();
		activeItem?.LoseCondition(activeItem.condition * 2f);
	}

	[ServerVar(Help = "reset all puzzles")]
	public static void puzzlereset(Arg arg)
	{
		if (!(ArgEx.Player(arg) == null))
		{
			PuzzleReset[] array = UnityEngine.Object.FindObjectsOfType<PuzzleReset>();
			Debug.Log("iterating...");
			PuzzleReset[] array2 = array;
			foreach (PuzzleReset puzzleReset in array2)
			{
				Debug.Log("resetting puzzle at :" + puzzleReset.transform.position.ToString());
				puzzleReset.DoReset();
				puzzleReset.ResetTimer();
			}
		}
	}

	[ServerVar(EditorOnly = true, Help = "respawn all puzzles from their prefabs")]
	public static void puzzleprefabrespawn(Arg arg)
	{
		foreach (BaseNetworkable item in BaseNetworkable.serverEntities.Where((BaseNetworkable x) => x is IOEntity && PrefabAttribute.server.Find<Construction>(x.prefabID) == null).ToList())
		{
			item.Kill();
		}
		foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
		{
			GameObject gameObject = GameManager.server.FindPrefab(monument.gameObject.name);
			if (gameObject == null)
			{
				continue;
			}
			Dictionary<IOEntity, IOEntity> dictionary = new Dictionary<IOEntity, IOEntity>();
			IOEntity[] componentsInChildren = gameObject.GetComponentsInChildren<IOEntity>(includeInactive: true);
			foreach (IOEntity iOEntity in componentsInChildren)
			{
				Quaternion rot = monument.transform.rotation * iOEntity.transform.rotation;
				Vector3 pos = monument.transform.TransformPoint(iOEntity.transform.position);
				BaseEntity newEntity = GameManager.server.CreateEntity(iOEntity.PrefabName, pos, rot);
				IOEntity iOEntity2 = newEntity as IOEntity;
				if (!(iOEntity2 != null))
				{
					continue;
				}
				dictionary.Add(iOEntity, iOEntity2);
				DoorManipulator doorManipulator = newEntity as DoorManipulator;
				if (doorManipulator != null)
				{
					List<Door> obj = Facepunch.Pool.GetList<Door>();
					global::Vis.Entities(newEntity.transform.position, 10f, obj);
					Door door = obj.OrderBy((Door x) => x.Distance(newEntity.transform.position)).FirstOrDefault();
					if (door != null)
					{
						doorManipulator.targetDoor = door;
					}
					Facepunch.Pool.FreeList(ref obj);
				}
				CardReader cardReader = newEntity as CardReader;
				if (cardReader != null)
				{
					CardReader cardReader2 = iOEntity as CardReader;
					if (cardReader2 != null)
					{
						cardReader.accessLevel = cardReader2.accessLevel;
						cardReader.accessDuration = cardReader2.accessDuration;
					}
				}
				TimerSwitch timerSwitch = newEntity as TimerSwitch;
				if (timerSwitch != null)
				{
					TimerSwitch timerSwitch2 = iOEntity as TimerSwitch;
					if (timerSwitch2 != null)
					{
						timerSwitch.timerLength = timerSwitch2.timerLength;
					}
				}
			}
			foreach (KeyValuePair<IOEntity, IOEntity> item2 in dictionary)
			{
				IOEntity key = item2.Key;
				IOEntity value = item2.Value;
				for (int j = 0; j < key.outputs.Length; j++)
				{
					if (!(key.outputs[j].connectedTo.ioEnt == null))
					{
						value.outputs[j].connectedTo.ioEnt = dictionary[key.outputs[j].connectedTo.ioEnt];
						value.outputs[j].connectedToSlot = key.outputs[j].connectedToSlot;
					}
				}
			}
			foreach (IOEntity value2 in dictionary.Values)
			{
				value2.Spawn();
			}
		}
	}

	[ServerVar(Help = "Break all the items in your inventory whose name match the passed string")]
	public static void breakitem(Arg arg)
	{
		string @string = arg.GetString(0);
		foreach (Item item in ArgEx.Player(arg).inventory.containerMain.itemList)
		{
			if (item.info.shortname.Contains(@string, CompareOptions.IgnoreCase) && item.hasCondition)
			{
				item.LoseCondition(item.condition * 2f);
			}
		}
	}

	[ServerVar]
	public static void refillvitals(Arg arg)
	{
		AdjustHealth(ArgEx.Player(arg), 1000f);
		AdjustCalories(ArgEx.Player(arg), 1000f);
		AdjustHydration(ArgEx.Player(arg), 1000f);
		AdjustRadiation(ArgEx.Player(arg), -10000f);
	}

	[ServerVar]
	public static void heal(Arg arg)
	{
		AdjustHealth(ArgEx.Player(arg), arg.GetInt(0, 1));
	}

	[ServerVar]
	public static void hurt(Arg arg)
	{
		AdjustHealth(ArgEx.Player(arg), -arg.GetInt(0, 1), arg.GetString(1, string.Empty));
	}

	[ServerVar]
	public static void eat(Arg arg)
	{
		AdjustCalories(ArgEx.Player(arg), arg.GetInt(0, 1), arg.GetInt(1, 1));
	}

	[ServerVar]
	public static void drink(Arg arg)
	{
		AdjustHydration(ArgEx.Player(arg), arg.GetInt(0, 1), arg.GetInt(1, 1));
	}

	[ServerVar]
	public static void sethealth(Arg arg)
	{
		if (!arg.HasArgs())
		{
			arg.ReplyWith("Please enter an amount.");
			return;
		}
		int @int = arg.GetInt(0);
		BasePlayer usePlayer = GetUsePlayer(arg, 1);
		if ((bool)usePlayer)
		{
			usePlayer.SetHealth(@int);
		}
	}

	[ServerVar]
	public static void setdamage(Arg arg)
	{
		BasePlayer basePlayer = ArgEx.Player(arg);
		if (!arg.HasArgs())
		{
			arg.ReplyWith("Please enter an amount.");
			return;
		}
		int @int = arg.GetInt(0);
		BasePlayer usePlayer = GetUsePlayer(arg, 1);
		if ((bool)usePlayer)
		{
			float damageAmount = usePlayer.health - (float)@int;
			HitInfo info = new HitInfo(basePlayer, basePlayer, DamageType.Bullet, damageAmount);
			usePlayer.OnAttacked(info);
		}
	}

	[ServerVar]
	public static void setfood(Arg arg)
	{
		setattribute(arg, MetabolismAttribute.Type.Calories);
	}

	[ServerVar]
	public static void setwater(Arg arg)
	{
		setattribute(arg, MetabolismAttribute.Type.Hydration);
	}

	[ServerVar]
	public static void setradiation(Arg arg)
	{
		setattribute(arg, MetabolismAttribute.Type.Radiation);
	}

	private static void AdjustHealth(BasePlayer player, float amount, string bone = null)
	{
		HitInfo hitInfo = new HitInfo(player, player, DamageType.Bullet, 0f - amount);
		if (!string.IsNullOrEmpty(bone))
		{
			hitInfo.HitBone = StringPool.Get(bone);
		}
		player.OnAttacked(hitInfo);
	}

	private static void AdjustCalories(BasePlayer player, float amount, float time = 1f)
	{
		player.metabolism.ApplyChange(MetabolismAttribute.Type.Calories, amount, time);
	}

	private static void AdjustHydration(BasePlayer player, float amount, float time = 1f)
	{
		player.metabolism.ApplyChange(MetabolismAttribute.Type.Hydration, amount, time);
	}

	private static void AdjustRadiation(BasePlayer player, float amount, float time = 1f)
	{
		player.metabolism.SetAttribute(MetabolismAttribute.Type.Radiation, amount);
	}

	private static void setattribute(Arg arg, MetabolismAttribute.Type type)
	{
		if (!arg.HasArgs())
		{
			arg.ReplyWith("Please enter an amount.");
			return;
		}
		int @int = arg.GetInt(0);
		BasePlayer usePlayer = GetUsePlayer(arg, 1);
		if ((bool)usePlayer)
		{
			usePlayer.metabolism.SetAttribute(type, @int);
		}
	}

	private static BasePlayer GetUsePlayer(Arg arg, int playerArgument)
	{
		BasePlayer basePlayer = null;
		if (arg.HasArgs(playerArgument + 1))
		{
			BasePlayer player = ArgEx.GetPlayer(arg, playerArgument);
			if (!player)
			{
				return null;
			}
			return player;
		}
		return ArgEx.Player(arg);
	}

	[ServerVar]
	public static void ResetSleepingBagTimers(Arg arg)
	{
		SleepingBag.ResetTimersForPlayer(ArgEx.Player(arg));
	}

	[ServerVar(Help = "Spawn lots of IO entities to lag the server")]
	public static void bench_io(Arg arg)
	{
		BasePlayer basePlayer = ArgEx.Player(arg);
		if (basePlayer == null || !basePlayer.IsAdmin)
		{
			return;
		}
		int @int = arg.GetInt(0, 50);
		string name = arg.GetString(1, "water_catcher_small");
		List<IOEntity> list = new List<IOEntity>();
		WaterCatcher waterCatcher = null;
		Vector3 position = ArgEx.Player(arg).transform.position;
		string[] array = (from x in GameManifest.Current.entities
			where Path.GetFileNameWithoutExtension(x).Contains(name, CompareOptions.IgnoreCase)
			select x.ToLower()).ToArray();
		if (array.Length == 0)
		{
			arg.ReplyWith("Couldn't find io prefab \"" + array[0] + "\"");
			return;
		}
		if (array.Length > 1)
		{
			string text = array.FirstOrDefault((string x) => string.Compare(Path.GetFileNameWithoutExtension(x), name, StringComparison.OrdinalIgnoreCase) == 0);
			if (text == null)
			{
				Debug.Log($"{arg} failed to find io entity \"{name}\"");
				arg.ReplyWith("Unknown entity - could be:\n\n" + string.Join("\n", array.Select(Path.GetFileNameWithoutExtension).ToArray()));
				return;
			}
			array[0] = text;
		}
		for (int i = 0; i < @int; i++)
		{
			Vector3 pos = position + new Vector3(i * 5, 0f, 0f);
			Quaternion identity = Quaternion.identity;
			BaseEntity baseEntity = GameManager.server.CreateEntity(array[0], pos, identity);
			if (!baseEntity)
			{
				continue;
			}
			baseEntity.Spawn();
			WaterCatcher component = baseEntity.GetComponent<WaterCatcher>();
			if ((bool)component)
			{
				list.Add(component);
				if (waterCatcher != null)
				{
					Connect(waterCatcher, component);
				}
				if (i == @int - 1)
				{
					Connect(component, list.First());
				}
				waterCatcher = component;
			}
		}
		static void Connect(IOEntity InputIOEnt, IOEntity OutputIOEnt)
		{
			int num = 0;
			int num2 = 0;
			WireTool.WireColour wireColour = WireTool.WireColour.Default;
			IOEntity.IOSlot iOSlot = InputIOEnt.inputs[num];
			IOEntity.IOSlot obj = OutputIOEnt.outputs[num2];
			iOSlot.connectedTo.Set(OutputIOEnt);
			iOSlot.connectedToSlot = num2;
			iOSlot.wireColour = wireColour;
			iOSlot.connectedTo.Init();
			obj.connectedTo.Set(InputIOEnt);
			obj.connectedToSlot = num;
			obj.wireColour = wireColour;
			obj.connectedTo.Init();
			obj.linePoints = new Vector3[2]
			{
				Vector3.zero,
				OutputIOEnt.transform.InverseTransformPoint(InputIOEnt.transform.TransformPoint(iOSlot.handlePosition))
			};
			OutputIOEnt.MarkDirtyForceUpdateOutputs();
			OutputIOEnt.SendNetworkUpdate();
			InputIOEnt.SendNetworkUpdate();
			OutputIOEnt.SendChangedToRoot(forceUpdate: true);
		}
	}
}
