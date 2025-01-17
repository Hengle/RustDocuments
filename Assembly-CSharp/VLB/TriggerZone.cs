#define UNITY_ASSERTIONS
using UnityEngine;

namespace VLB;

[HelpURL("http://saladgamer.com/vlb-doc/comp-triggerzone/")]
[DisallowMultipleComponent]
[RequireComponent(typeof(VolumetricLightBeam))]
public class TriggerZone : MonoBehaviour
{
	public bool setIsTrigger = true;

	public float rangeMultiplier = 1f;

	private const int kMeshColliderNumSides = 8;

	private Mesh m_Mesh;

	private void Update()
	{
		VolumetricLightBeam component = GetComponent<VolumetricLightBeam>();
		if ((bool)component)
		{
			MeshCollider orAddComponent = Utils.GetOrAddComponent<MeshCollider>(base.gameObject);
			Debug.Assert(orAddComponent);
			float lengthZ = component.fadeEnd * rangeMultiplier;
			float radiusEnd = Mathf.LerpUnclamped(component.coneRadiusStart, component.coneRadiusEnd, rangeMultiplier);
			m_Mesh = MeshGenerator.GenerateConeZ_Radius(lengthZ, component.coneRadiusStart, radiusEnd, 8, 0, cap: false);
			m_Mesh.hideFlags = Consts.ProceduralObjectsHideFlags;
			orAddComponent.sharedMesh = m_Mesh;
			if (setIsTrigger)
			{
				orAddComponent.convex = true;
				orAddComponent.isTrigger = true;
			}
			Object.Destroy(this);
		}
	}
}
