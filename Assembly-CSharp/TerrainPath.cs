using System.Collections.Generic;
using UnityEngine;

public class TerrainPath : TerrainExtension
{
	public List<PathList> Roads = new List<PathList>();

	public List<PathList> Rails = new List<PathList>();

	public List<PathList> Rivers = new List<PathList>();

	public List<PathList> Powerlines = new List<PathList>();

	public List<LandmarkInfo> Landmarks = new List<LandmarkInfo>();

	public List<MonumentInfo> Monuments = new List<MonumentInfo>();

	public List<RiverInfo> RiverObjs = new List<RiverInfo>();

	public List<LakeInfo> LakeObjs = new List<LakeInfo>();

	public GameObject DungeonGridRoot;

	public List<DungeonGridInfo> DungeonGridEntrances = new List<DungeonGridInfo>();

	public List<DungeonGridCell> DungeonGridCells = new List<DungeonGridCell>();

	public GameObject DungeonBaseRoot;

	public List<DungeonBaseInfo> DungeonBaseEntrances = new List<DungeonBaseInfo>();

	internal List<DungeonBaseLink> DungeonBaseLinks = new List<DungeonBaseLink>();

	public List<Vector3> OceanPatrolClose = new List<Vector3>();

	public List<Vector3> OceanPatrolFar = new List<Vector3>();

	public Dictionary<string, List<PowerlineNode>> wires = new Dictionary<string, List<PowerlineNode>>();

	public override void PostSetup()
	{
		foreach (PathList road in Roads)
		{
			road.ProcgenStartNode = null;
			road.ProcgenEndNode = null;
		}
		foreach (PathList rail in Rails)
		{
			rail.ProcgenStartNode = null;
			rail.ProcgenEndNode = null;
		}
		foreach (PathList river in Rivers)
		{
			river.ProcgenStartNode = null;
			river.ProcgenEndNode = null;
		}
		foreach (PathList powerline in Powerlines)
		{
			powerline.ProcgenStartNode = null;
			powerline.ProcgenEndNode = null;
		}
	}

	public void Clear()
	{
		Roads.Clear();
		Rails.Clear();
		Rivers.Clear();
		Powerlines.Clear();
	}

	public T FindClosest<T>(List<T> list, Vector3 pos) where T : MonoBehaviour
	{
		T result = null;
		float num = float.MaxValue;
		foreach (T item in list)
		{
			float num2 = Vector3Ex.Distance2D(item.transform.position, pos);
			if (!(num2 >= num))
			{
				result = item;
				num = num2;
			}
		}
		return result;
	}

	public static int[,] CreatePowerlineCostmap(ref uint seed)
	{
		float radius = 5f;
		int num = (int)((float)World.Size / 7.5f);
		TerrainPlacementMap placementMap = TerrainMeta.PlacementMap;
		TerrainHeightMap heightMap = TerrainMeta.HeightMap;
		TerrainTopologyMap topologyMap = TerrainMeta.TopologyMap;
		int[,] array = new int[num, num];
		for (int i = 0; i < num; i++)
		{
			float normZ = ((float)i + 0.5f) / (float)num;
			for (int j = 0; j < num; j++)
			{
				float normX = ((float)j + 0.5f) / (float)num;
				float slope = heightMap.GetSlope(normX, normZ);
				int topology = topologyMap.GetTopology(normX, normZ, radius);
				int num2 = 2295172;
				int num3 = 1628160;
				int num4 = 514;
				if ((topology & num2) != 0)
				{
					array[j, i] = int.MaxValue;
				}
				else if ((topology & num3) != 0 || placementMap.GetBlocked(normX, normZ, radius))
				{
					array[j, i] = 2500;
				}
				else if ((topology & num4) != 0)
				{
					array[j, i] = 1000;
				}
				else
				{
					array[j, i] = 1 + (int)(slope * slope * 10f);
				}
			}
		}
		return array;
	}

	public static int[,] CreateRoadCostmap(ref uint seed)
	{
		float radius = 5f;
		float radius2 = 15f;
		int num = (int)((float)World.Size / 7.5f);
		TerrainPlacementMap placementMap = TerrainMeta.PlacementMap;
		TerrainHeightMap heightMap = TerrainMeta.HeightMap;
		TerrainTopologyMap topologyMap = TerrainMeta.TopologyMap;
		int[,] array = new int[num, num];
		for (int i = 0; i < num; i++)
		{
			float normZ = ((float)i + 0.5f) / (float)num;
			for (int j = 0; j < num; j++)
			{
				float normX = ((float)j + 0.5f) / (float)num;
				int num2 = SeedRandom.Range(ref seed, 100, 200);
				float slope = heightMap.GetSlope(normX, normZ);
				int topology = topologyMap.GetTopology(normX, normZ, radius);
				int topology2 = topologyMap.GetTopology(normX, normZ, radius2);
				int num3 = 196996;
				int num4 = 2098176;
				int num5 = 49666;
				if (slope > 20f || (topology & num3) != 0 || (topology2 & num4) != 0)
				{
					array[j, i] = int.MaxValue;
				}
				else if ((topology & num5) != 0 || placementMap.GetBlocked(normX, normZ, radius))
				{
					array[j, i] = 5000;
				}
				else
				{
					array[j, i] = 1 + (int)(slope * slope * 10f) + num2;
				}
			}
		}
		return array;
	}

	public static int[,] CreateRailCostmap(ref uint seed)
	{
		float radius = 5f;
		float radius2 = 25f;
		int num = (int)((float)World.Size / 7.5f);
		TerrainPlacementMap placementMap = TerrainMeta.PlacementMap;
		TerrainHeightMap heightMap = TerrainMeta.HeightMap;
		TerrainTopologyMap topologyMap = TerrainMeta.TopologyMap;
		int[,] array = new int[num, num];
		for (int i = 0; i < num; i++)
		{
			float normZ = ((float)i + 0.5f) / (float)num;
			for (int j = 0; j < num; j++)
			{
				float normX = ((float)j + 0.5f) / (float)num;
				float slope = heightMap.GetSlope(normX, normZ);
				int topology = topologyMap.GetTopology(normX, normZ, radius);
				int topology2 = topologyMap.GetTopology(normX, normZ, radius2);
				int num2 = 196996;
				int num3 = 2098176;
				int num4 = 49666;
				if (slope > 30f || (topology & num2) != 0 || (topology2 & num3) != 0)
				{
					array[j, i] = int.MaxValue;
				}
				else if (slope > 20f || (topology & num4) != 0 || placementMap.GetBlocked(normX, normZ, radius))
				{
					array[j, i] = 5000;
				}
				else if (slope > 10f)
				{
					array[j, i] = 1500;
				}
				else
				{
					array[j, i] = 1000;
				}
			}
		}
		return array;
	}

	public static int[,] CreateBoatCostmap(float depth)
	{
		TerrainHeightMap heightMap = TerrainMeta.HeightMap;
		TerrainWaterMap waterMap = TerrainMeta.WaterMap;
		int num = (int)((float)World.Size / 7.5f);
		int[,] array = new int[num, num];
		for (int i = 0; i < num; i++)
		{
			float normZ = ((float)i + 0.5f) / (float)num;
			for (int j = 0; j < num; j++)
			{
				float normX = ((float)j + 0.5f) / (float)num;
				float height = heightMap.GetHeight(normX, normZ);
				if (waterMap.GetHeight(normX, normZ) - height < depth)
				{
					array[j, i] = int.MaxValue;
				}
				else
				{
					array[j, i] = 1;
				}
			}
		}
		return array;
	}

	public void AddWire(PowerlineNode node)
	{
		string key = node.transform.root.name;
		if (!wires.ContainsKey(key))
		{
			wires.Add(key, new List<PowerlineNode>());
		}
		wires[key].Add(node);
	}

	public void CreateWires()
	{
		List<GameObject> list = new List<GameObject>();
		int num = 0;
		GameObjectRef gameObjectRef = null;
		foreach (KeyValuePair<string, List<PowerlineNode>> wire in wires)
		{
			foreach (PowerlineNode item in wire.Value)
			{
				PowerLineWireConnectionHelper component = item.GetComponent<PowerLineWireConnectionHelper>();
				if (!component)
				{
					continue;
				}
				if (list.Count == 0)
				{
					gameObjectRef = item.WirePrefab;
					num = component.connections.Count;
				}
				else
				{
					GameObject gameObject = list[list.Count - 1];
					if (item.WirePrefab.guid != gameObjectRef?.guid || component.connections.Count != num || (gameObject.transform.position - item.transform.position).sqrMagnitude > item.MaxDistance * item.MaxDistance)
					{
						CreateWire(wire.Key, list, gameObjectRef);
						list.Clear();
					}
				}
				list.Add(item.gameObject);
			}
			CreateWire(wire.Key, list, gameObjectRef);
			list.Clear();
		}
	}

	private void CreateWire(string name, List<GameObject> objects, GameObjectRef wirePrefab)
	{
		if (objects.Count >= 3 && wirePrefab != null && wirePrefab.isValid)
		{
			PowerLineWire powerLineWire = PowerLineWire.Create(null, objects, wirePrefab, "Powerline Wires", null, 1f, 0.1f);
			if ((bool)powerLineWire)
			{
				powerLineWire.enabled = false;
				GameObjectEx.SetHierarchyGroup(powerLineWire.gameObject, name);
			}
		}
	}

	public MonumentInfo FindMonumentWithBoundsOverlap(Vector3 position)
	{
		if (TerrainMeta.Path.Monuments == null)
		{
			return null;
		}
		foreach (MonumentInfo monument in TerrainMeta.Path.Monuments)
		{
			if (monument != null && monument.IsInBounds(position))
			{
				return monument;
			}
		}
		return null;
	}
}
