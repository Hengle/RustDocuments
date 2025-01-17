using System;
using System.Collections.Generic;
using Facepunch;
using Facepunch.Math;
using Facepunch.Nexus;
using Facepunch.Nexus.Models;
using Facepunch.Rust;
using Facepunch.Sqlite;
using ProtoBuf;
using UnityEngine;

public class UserPersistance : IDisposable
{
	public static Facepunch.Sqlite.Database blueprints;

	public static Facepunch.Sqlite.Database deaths;

	public static Facepunch.Sqlite.Database identities;

	public static Facepunch.Sqlite.Database tokens;

	public static Facepunch.Sqlite.Database playerState;

	public static Dictionary<ulong, string> nameCache;

	private static Dictionary<ulong, string> wipeIdCache;

	public static MruDictionary<ulong, (int Token, bool Locked)> tokenCache;

	public UserPersistance(string strFolder)
	{
		blueprints = new Facepunch.Sqlite.Database();
		BaseGameMode activeGameMode = BaseGameMode.GetActiveGameMode(serverside: true);
		string text = strFolder + "/player.blueprints.";
		if (activeGameMode != null && activeGameMode.wipeBpsOnProtocol)
		{
			text = text + 243 + ".";
		}
		blueprints.Open(text + 5 + ".db");
		if (!blueprints.TableExists("data"))
		{
			blueprints.Execute("CREATE TABLE data ( userid TEXT PRIMARY KEY, info BLOB, updated INTEGER )");
		}
		deaths = new Facepunch.Sqlite.Database();
		deaths.Open(strFolder + "/player.deaths." + 5 + ".db");
		if (!deaths.TableExists("data"))
		{
			deaths.Execute("CREATE TABLE data ( userid TEXT, born INTEGER, died INTEGER, info BLOB )");
			deaths.Execute("CREATE INDEX IF NOT EXISTS userindex ON data ( userid )");
			deaths.Execute("CREATE INDEX IF NOT EXISTS diedindex ON data ( died )");
		}
		identities = new Facepunch.Sqlite.Database();
		identities.Open(strFolder + "/player.identities." + 5 + ".db");
		if (!identities.TableExists("data"))
		{
			identities.Execute("CREATE TABLE data ( userid INT PRIMARY KEY, username TEXT )");
		}
		tokens = new Facepunch.Sqlite.Database();
		tokens.Open(strFolder + "/player.tokens.db");
		if (!tokens.TableExists("data"))
		{
			tokens.Execute("CREATE TABLE data ( userid INT PRIMARY KEY, token INT, locked BOOLEAN DEFAULT 0 )");
		}
		if (!tokens.ColumnExists("data", "locked"))
		{
			tokens.Execute("ALTER TABLE data ADD COLUMN locked BOOLEAN DEFAULT 0");
		}
		playerState = new Facepunch.Sqlite.Database();
		playerState.Open(strFolder + "/player.states." + 243 + ".db");
		if (!playerState.TableExists("data"))
		{
			playerState.Execute("CREATE TABLE data ( userid INT PRIMARY KEY, state BLOB )");
		}
		nameCache = new Dictionary<ulong, string>();
		tokenCache = new MruDictionary<ulong, (int, bool)>(500);
		wipeIdCache = new Dictionary<ulong, string>();
	}

	public virtual void Dispose()
	{
		if (blueprints != null)
		{
			blueprints.Close();
			blueprints = null;
		}
		if (deaths != null)
		{
			deaths.Close();
			deaths = null;
		}
		if (identities != null)
		{
			identities.Close();
			identities = null;
		}
		if (tokens != null)
		{
			tokens.Close();
			tokens = null;
		}
		if (playerState != null)
		{
			playerState.Close();
			playerState = null;
		}
	}

	public PersistantPlayer GetPlayerInfo(ulong playerID)
	{
		PersistantPlayer persistantPlayer = FetchFromDatabase(playerID);
		if (persistantPlayer == null)
		{
			persistantPlayer = Pool.Get<PersistantPlayer>();
		}
		if (persistantPlayer.unlockedItems == null)
		{
			persistantPlayer.unlockedItems = Pool.GetList<int>();
		}
		return persistantPlayer;
	}

	private PersistantPlayer FetchFromDatabase(ulong playerID)
	{
		try
		{
			byte[] array = null;
			NexusPlayer player;
			Variable variable;
			if (!NexusServer.Started)
			{
				array = blueprints.Query<byte[], ulong>("SELECT info FROM data WHERE userid = ?", playerID);
			}
			else if (NexusServer.TryGetPlayer(playerID, out player) && player.TryGetVariable(NexusVariables.Blueprints, out variable) && variable.Type == VariableType.Binary)
			{
				array = variable.GetAsBinary();
			}
			if (array != null)
			{
				return PersistantPlayer.Deserialize(array);
			}
		}
		catch (Exception ex)
		{
			Debug.LogError("Error loading player blueprints: (" + ex.Message + ")");
		}
		return null;
	}

	public void SetPlayerInfo(ulong playerID, PersistantPlayer info)
	{
		using (TimeWarning.New("SetPlayerInfo"))
		{
			byte[] array;
			using (TimeWarning.New("ToProtoBytes"))
			{
				array = info.ToProtoBytes();
			}
			NexusPlayer player;
			if (!NexusServer.Started)
			{
				blueprints.Execute("INSERT OR REPLACE INTO data ( userid, info, updated ) VALUES ( ?, ?, ? )", playerID, array, Epoch.Current);
			}
			else if (!NexusServer.TryGetPlayer(playerID, out player))
			{
				Debug.LogError($"Couldn't find NexusPlayer to save player info! {playerID}");
			}
			else
			{
				player.SetVariable(NexusVariables.Blueprints, array, isTransient: false);
			}
		}
	}

	public void AddLifeStory(ulong playerID, PlayerLifeStory lifeStory)
	{
		if (deaths == null || lifeStory == null)
		{
			return;
		}
		using (TimeWarning.New("AddLifeStory"))
		{
			byte[] arg;
			using (TimeWarning.New("ToProtoBytes"))
			{
				arg = lifeStory.ToProtoBytes();
			}
			deaths.Execute("INSERT INTO data ( userid, born, died, info ) VALUES ( ?, ?, ?, ? )", playerID, (int)lifeStory.timeBorn, (int)lifeStory.timeDied, arg);
		}
	}

	public PlayerLifeStory GetLastLifeStory(ulong playerID)
	{
		if (deaths == null)
		{
			return null;
		}
		using (TimeWarning.New("GetLastLifeStory"))
		{
			try
			{
				byte[] array = deaths.Query<byte[], ulong>("SELECT info FROM data WHERE userid = ? ORDER BY died DESC LIMIT 1", playerID);
				if (array == null)
				{
					return null;
				}
				PlayerLifeStory playerLifeStory = PlayerLifeStory.Deserialize(array);
				playerLifeStory.ShouldPool = false;
				return playerLifeStory;
			}
			catch (Exception ex)
			{
				Debug.LogError("Error loading lifestory from database: (" + ex.Message + ")");
			}
			return null;
		}
	}

	public string GetPlayerName(ulong playerID)
	{
		if (playerID == 0L)
		{
			return null;
		}
		if (nameCache.TryGetValue(playerID, out var value))
		{
			return value;
		}
		string text = identities.Query<string, ulong>("SELECT username FROM data WHERE userid = ?", playerID);
		nameCache[playerID] = text;
		return text;
	}

	public void SetPlayerName(ulong playerID, string name)
	{
		if (playerID != 0L && !string.IsNullOrEmpty(name))
		{
			if (string.IsNullOrEmpty(GetPlayerName(playerID)))
			{
				identities.Execute("INSERT INTO data ( userid, username ) VALUES ( ?, ? )", playerID, name);
			}
			else
			{
				identities.Execute("UPDATE data SET username = ? WHERE userid = ?", name, playerID);
			}
			nameCache[playerID] = name;
		}
	}

	public int GetOrGenerateAppToken(ulong playerID, out bool locked)
	{
		if (tokens == null)
		{
			locked = false;
			return 0;
		}
		using (TimeWarning.New("GetOrGenerateAppToken"))
		{
			if (tokenCache.TryGetValue(playerID, out (int, bool) value))
			{
				locked = value.Item2;
				return value.Item1;
			}
			int num = tokens.Query<int, ulong>("SELECT token FROM data WHERE userid = ?", playerID);
			if (num != 0)
			{
				bool flag = tokens.Query<int, ulong>("SELECT locked FROM data WHERE userid = ?", playerID) != 0;
				tokenCache.Add(playerID, (num, flag));
				locked = flag;
				return num;
			}
			int num2 = GenerateAppToken();
			tokens.Execute("INSERT INTO data ( userid, token ) VALUES ( ?, ? )", playerID, num2);
			tokenCache.Add(playerID, (num2, false));
			locked = false;
			return num2;
		}
	}

	public void RegenerateAppToken(ulong playerID)
	{
		if (tokens == null)
		{
			return;
		}
		using (TimeWarning.New("RegenerateAppToken"))
		{
			tokenCache.Remove(playerID);
			bool arg = tokens.Query<int, ulong>("SELECT locked FROM data WHERE userid = ?", playerID) != 0;
			int num = GenerateAppToken();
			tokens.Execute("INSERT OR REPLACE INTO data ( userid, token, locked ) VALUES ( ?, ?, ? )", playerID, num, arg);
			tokenCache.Add(playerID, (num, false));
		}
	}

	private static int GenerateAppToken()
	{
		int num = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
		if (num == 0)
		{
			num++;
		}
		return num;
	}

	public bool SetAppTokenLocked(ulong playerID, bool locked)
	{
		if (tokens == null)
		{
			return false;
		}
		GetOrGenerateAppToken(playerID, out var locked2);
		if (locked2 == locked)
		{
			return false;
		}
		tokens.Execute("UPDATE data SET locked = ? WHERE userid = ?", locked ? 1 : 0, playerID);
		tokenCache.Remove(playerID);
		return true;
	}

	public byte[] GetPlayerState(ulong playerID)
	{
		if (playerID == 0L)
		{
			return null;
		}
		return playerState.Query<byte[], ulong>("SELECT state FROM data WHERE userid = ?", playerID);
	}

	public void SetPlayerState(ulong playerID, byte[] state)
	{
		if (playerID != 0L && state != null)
		{
			playerState.Execute("INSERT OR REPLACE INTO data ( userid, state ) VALUES ( ?, ? )", playerID, state);
		}
	}

	public string GetUserWipeId(ulong playerID)
	{
		if (playerID <= 10000000)
		{
			return null;
		}
		if (wipeIdCache.TryGetValue(playerID, out var value))
		{
			return value;
		}
		value = (playerID + SaveRestore.WipeId).Sha256().HexString();
		wipeIdCache[playerID] = value;
		Analytics.Azure.OnPlayerInitializedWipeId(playerID, value);
		return value;
	}

	public void ResetPlayerState(ulong playerID)
	{
		if (playerID != 0L)
		{
			playerState.Execute("DELETE FROM data WHERE userid = ?", playerID);
		}
	}
}
