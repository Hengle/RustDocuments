using UnityEngine;

public class SocketMod_SphereCheck : SocketMod
{
	public float sphereRadius = 1f;

	public LayerMask layerMask;

	public bool wantsCollide;

	public static Translate.Phrase Error_WantsCollideConstruction = new Translate.Phrase("error_wantsconstruction", "Must be placed on construction");

	public static Translate.Phrase Error_DoesNotWantCollideConstruction = new Translate.Phrase("error_doesnotwantconstruction", "Cannot be placed on construction");

	private void OnDrawGizmosSelected()
	{
		Gizmos.matrix = base.transform.localToWorldMatrix;
		Gizmos.color = (wantsCollide ? new Color(0f, 1f, 0f, 0.7f) : new Color(1f, 0f, 0f, 0.7f));
		Gizmos.DrawSphere(Vector3.zero, sphereRadius);
	}

	public override bool DoCheck(Construction.Placement place)
	{
		Vector3 position = place.position + place.rotation * worldPosition;
		bool flag = wantsCollide == GamePhysics.CheckSphere(position, sphereRadius, layerMask.value);
		if (!flag)
		{
			bool flag2 = false;
			Construction.lastPlacementError = "Failed Check: Sphere Test (" + hierachyName + ")";
			if ((int)layerMask == 2097152 && wantsCollide)
			{
				Construction.lastPlacementError = Error_WantsCollideConstruction.translated;
				if (flag2)
				{
					Construction.lastPlacementError = Construction.lastPlacementError + " (" + hierachyName + ")";
				}
			}
			else if (!wantsCollide && ((int)layerMask & 0x200000) == 2097152)
			{
				Construction.lastPlacementError = Error_DoesNotWantCollideConstruction.translated;
				if (flag2)
				{
					Construction.lastPlacementError = Construction.lastPlacementError + " (" + hierachyName + ")";
				}
			}
			else
			{
				Construction.lastPlacementError = "Failed Check: Sphere Test (" + hierachyName + ")";
			}
		}
		else if (wantsCollide && ((int)layerMask & 0x8000000) == 0)
		{
			flag = !GamePhysics.CheckSphere(place.position, 5f, 134217728);
			if (!flag)
			{
				Construction.lastPlacementError = "Failed Check: Sphere Test (" + hierachyName + ") Vehicle_Large test";
			}
		}
		if (flag)
		{
			return true;
		}
		return false;
	}
}
