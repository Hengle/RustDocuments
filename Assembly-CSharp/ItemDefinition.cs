using System;
using System.Collections.Generic;
using System.Linq;
using Rust;
using UnityEngine;

public class ItemDefinition : MonoBehaviour
{
	[Serializable]
	public struct Condition
	{
		[Serializable]
		public class WorldSpawnCondition
		{
			public float fractionMin = 1f;

			public float fractionMax = 1f;
		}

		public bool enabled;

		[Tooltip("The maximum condition this item type can have, new items will start with this value")]
		public float max;

		[Tooltip("If false then item will destroy when condition reaches 0")]
		public bool repairable;

		[Tooltip("If true, never lose max condition when repaired")]
		public bool maintainMaxCondition;

		public bool ovenCondition;

		public WorldSpawnCondition foundCondition;
	}

	[Serializable]
	public struct OverrideWorldModel
	{
		public GameObjectRef worldModel;

		public int minStackSize;
	}

	public enum RedirectVendingBehaviour
	{
		NoListing = 0,
		ListAsUniqueItem = 1
	}

	[Flags]
	public enum Flag
	{
		NoDropping = 1,
		NotStraightToBelt = 2,
		NotAllowedInBelt = 4,
		Backpack = 8
	}

	public enum AmountType
	{
		Count = 0,
		Millilitre = 1,
		Feet = 2,
		Genetics = 3,
		OxygenSeconds = 4,
		Frequency = 5,
		Generic = 6,
		BagLimit = 7
	}

	[ReadOnly]
	[Header("Item")]
	public int itemid;

	[Tooltip("The shortname should be unique. A hash will be generated from it to identify the item type. If this name changes at any point it will make all saves incompatible")]
	public string shortname;

	[Header("Appearance")]
	public Translate.Phrase displayName;

	public Translate.Phrase displayDescription;

	public Sprite iconSprite;

	public ItemCategory category;

	public ItemSelectionPanel selectionPanel;

	[Header("Containment")]
	public int maxDraggable;

	public ItemContainer.ContentsType itemType = ItemContainer.ContentsType.Generic;

	public AmountType amountType;

	[InspectorFlags]
	public ItemSlot occupySlots = ItemSlot.None;

	public int stackable;

	public bool quickDespawn;

	[Header("Spawn Tables")]
	[Tooltip("How rare this item is and how much it costs to research")]
	public Rarity rarity;

	public Rarity despawnRarity;

	public bool spawnAsBlueprint;

	[Header("Sounds")]
	public SoundDefinition inventoryGrabSound;

	public SoundDefinition inventoryDropSound;

	public SoundDefinition physImpactSoundDef;

	public Condition condition;

	[Header("Misc")]
	public bool hidden;

	[InspectorFlags]
	public Flag flags;

	[Tooltip("User can craft this item on any server if they have this steam item")]
	public SteamInventoryItem steamItem;

	[Tooltip("User can craft this item if they have this DLC purchased")]
	public SteamDLCItem steamDlc;

	[Tooltip("Can only craft this item if the parent is craftable (tech tree)")]
	public ItemDefinition Parent;

	public GameObjectRef worldModelPrefab;

	public OverrideWorldModel[] worldModelOverrides;

	public ItemDefinition isRedirectOf;

	public RedirectVendingBehaviour redirectVendingBehaviour;

	[NonSerialized]
	public ItemMod[] itemMods;

	public BaseEntity.TraitFlag Traits;

	[NonSerialized]
	public ItemSkinDirectory.Skin[] skins;

	[NonSerialized]
	public IPlayerItemDefinition[] _skins2;

	[Tooltip("Panel to show in the inventory menu when selected")]
	public GameObject panel;

	[NonSerialized]
	public ItemDefinition[] Children = new ItemDefinition[0];

	public IPlayerItemDefinition[] skins2
	{
		get
		{
			if (_skins2 != null)
			{
				return _skins2;
			}
			if (PlatformService.Instance.IsValid && PlatformService.Instance.ItemDefinitions != null)
			{
				string prefabname = base.name;
				_skins2 = PlatformService.Instance.ItemDefinitions.Where((IPlayerItemDefinition x) => (x.ItemShortName == shortname || x.ItemShortName == prefabname) && x.WorkshopId != 0).ToArray();
			}
			return _skins2;
		}
	}

	public ItemBlueprint Blueprint => GetComponent<ItemBlueprint>();

	public int craftingStackable => Mathf.Max(10, stackable);

	public bool isWearable => ItemModWearable != null;

	public ItemModWearable ItemModWearable { get; private set; }

	public bool isHoldable { get; private set; }

	public bool isUsable { get; private set; }

	public bool HasSkins
	{
		get
		{
			if (skins2 != null && skins2.Length != 0)
			{
				return true;
			}
			if (skins != null && skins.Length != 0)
			{
				return true;
			}
			return false;
		}
	}

	public bool CraftableWithSkin { get; private set; }

	public void InvalidateWorkshopSkinCache()
	{
		_skins2 = null;
	}

	public static ulong FindSkin(int itemID, int skinID)
	{
		ItemDefinition itemDefinition = ItemManager.FindItemDefinition(itemID);
		if (itemDefinition == null)
		{
			return 0uL;
		}
		IPlayerItemDefinition itemDefinition2 = PlatformService.Instance.GetItemDefinition(skinID);
		if (itemDefinition2 != null)
		{
			ulong workshopDownload = itemDefinition2.WorkshopDownload;
			if (workshopDownload != 0L)
			{
				string itemShortName = itemDefinition2.ItemShortName;
				if (itemShortName == itemDefinition.shortname || itemShortName == itemDefinition.name)
				{
					return workshopDownload;
				}
			}
		}
		for (int i = 0; i < itemDefinition.skins.Length; i++)
		{
			if (itemDefinition.skins[i].id == skinID)
			{
				return (ulong)skinID;
			}
		}
		return 0uL;
	}

	public bool HasFlag(Flag f)
	{
		return (flags & f) == f;
	}

	public void Initialize(List<ItemDefinition> itemList)
	{
		if (itemMods != null)
		{
			Debug.LogError("Item Definition Initializing twice: " + base.name);
		}
		skins = ItemSkinDirectory.ForItem(this);
		itemMods = GetComponentsInChildren<ItemMod>(includeInactive: true);
		ItemMod[] array = itemMods;
		for (int i = 0; i < array.Length; i++)
		{
			array[i].ModInit();
		}
		Children = itemList.Where((ItemDefinition x) => x.Parent == this).ToArray();
		ItemModWearable = GetComponent<ItemModWearable>();
		isHoldable = GetComponent<ItemModEntity>() != null;
		isUsable = GetComponent<ItemModEntity>() != null || GetComponent<ItemModConsume>() != null;
	}

	public GameObjectRef GetWorldModel(int amount)
	{
		if (worldModelOverrides == null || worldModelOverrides.Length == 0)
		{
			return worldModelPrefab;
		}
		for (int num = worldModelOverrides.Length - 1; num >= 0; num--)
		{
			if (amount >= worldModelOverrides[num].minStackSize)
			{
				return worldModelOverrides[num].worldModel;
			}
		}
		return worldModelPrefab;
	}

	public int GetWorldModelIndex(int amount)
	{
		if (worldModelOverrides == null || worldModelOverrides.Length == 0)
		{
			return -1;
		}
		for (int num = worldModelOverrides.Length - 1; num >= 0; num--)
		{
			if (amount >= worldModelOverrides[num].minStackSize)
			{
				return num;
			}
		}
		return -1;
	}
}
