using System;
using UnityEngine;

[ExecuteInEditMode]
public class WaterBody : MonoBehaviour
{
	[Flags]
	public enum FishingTag
	{
		MoonPool = 1,
		River = 2,
		Ocean = 4,
		Swamp = 8
	}

	public WaterBodyType Type = WaterBodyType.Lake;

	public Renderer Renderer;

	public Collider[] Triggers;

	public bool IsOcean;

	public FishingTag FishingType;

	public Transform Transform { get; private set; }

	private void Awake()
	{
		Transform = base.transform;
	}

	private void OnEnable()
	{
		WaterSystem.RegisterBody(this);
	}

	private void OnDisable()
	{
		WaterSystem.UnregisterBody(this);
	}

	public void OnOceanLevelChanged(float newLevel)
	{
		if (!IsOcean || Triggers == null)
		{
			return;
		}
		Collider[] triggers = Triggers;
		foreach (Collider collider in triggers)
		{
			if (!(collider == null))
			{
				Vector3 position = collider.transform.position;
				position.y = newLevel;
				collider.transform.position = position;
			}
		}
	}
}
