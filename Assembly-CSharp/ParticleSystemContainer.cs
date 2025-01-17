using System;
using System.Collections.Generic;
using UnityEngine;

public class ParticleSystemContainer : MonoBehaviour, IPrefabPreProcess
{
	[Serializable]
	public struct ParticleSystemGroup
	{
		public ParticleSystem system;

		public LODComponentParticleSystem[] lodComponents;
	}

	public bool precached;

	public bool includeLights;

	[HideInInspector]
	[SerializeField]
	private ParticleSystemGroup[] particleGroups;

	[SerializeField]
	[HideInInspector]
	private Light[] lights;

	public void Play()
	{
	}

	public void Pause()
	{
	}

	public void Stop()
	{
	}

	public void Clear()
	{
	}

	private void SetLights(bool on)
	{
		Light[] array = ((!precached) ? GetComponentsInChildren<Light>() : lights);
		Light[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i].enabled = on;
		}
	}

	public void PreProcess(IPrefabProcessor preProcess, GameObject rootObj, string name, bool serverside, bool clientside, bool bundling)
	{
		if (precached && clientside)
		{
			List<ParticleSystemGroup> list = new List<ParticleSystemGroup>();
			ParticleSystem[] componentsInChildren = GetComponentsInChildren<ParticleSystem>();
			foreach (ParticleSystem particleSystem in componentsInChildren)
			{
				LODComponentParticleSystem[] components = particleSystem.GetComponents<LODComponentParticleSystem>();
				ParticleSystemGroup particleSystemGroup = default(ParticleSystemGroup);
				particleSystemGroup.system = particleSystem;
				particleSystemGroup.lodComponents = components;
				ParticleSystemGroup item = particleSystemGroup;
				list.Add(item);
			}
			particleGroups = list.ToArray();
			if (includeLights)
			{
				lights = GetComponentsInChildren<Light>();
			}
		}
	}
}
