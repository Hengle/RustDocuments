namespace UnityEngine;

public static class ArgEx
{
	public static BasePlayer Player(this ConsoleSystem.Arg arg)
	{
		if (arg == null || arg.Connection == null)
		{
			return null;
		}
		return arg.Connection.player as BasePlayer;
	}

	public static BasePlayer GetPlayer(this ConsoleSystem.Arg arg, int iArgNum)
	{
		string @string = arg.GetString(iArgNum, null);
		if (@string == null)
		{
			return null;
		}
		return BasePlayer.Find(@string);
	}

	public static BasePlayer GetSleeper(this ConsoleSystem.Arg arg, int iArgNum)
	{
		string @string = arg.GetString(iArgNum);
		if (@string == null)
		{
			return null;
		}
		return BasePlayer.FindSleeping(@string);
	}

	public static BasePlayer GetPlayerOrSleeper(this ConsoleSystem.Arg arg, int iArgNum)
	{
		string @string = arg.GetString(iArgNum);
		if (@string == null)
		{
			return null;
		}
		return BasePlayer.FindAwakeOrSleeping(@string);
	}

	public static BasePlayer GetPlayerOrSleeperOrBot(this ConsoleSystem.Arg arg, int iArgNum)
	{
		if (arg.TryGetUInt(iArgNum, out var value))
		{
			return BasePlayer.FindBot(value);
		}
		return GetPlayerOrSleeper(arg, iArgNum);
	}

	public static NetworkableId GetEntityID(this ConsoleSystem.Arg arg, int iArg, NetworkableId def = default(NetworkableId))
	{
		return new NetworkableId(arg.GetUInt64(iArg, def.Value));
	}

	public static ItemId GetItemID(this ConsoleSystem.Arg arg, int iArg, ItemId def = default(ItemId))
	{
		return new ItemId(arg.GetUInt64(iArg, def.Value));
	}
}
