using System.Collections.Generic;
using Facepunch;
using Rust;
using UnityEngine;

public class TriggerHurtNotChild : TriggerBase, IServerComponent, IHurtTrigger
{
	public interface IHurtTriggerUser
	{
		BasePlayer GetPlayerDamageInitiator();

		float GetDamageMultiplier(BaseEntity ent);

		void OnHurtTriggerOccupant(BaseEntity hurtEntity, DamageType damageType, float damageTotal);
	}

	public float DamagePerSecond = 1f;

	public float DamageTickRate = 4f;

	public float DamageDelay;

	public DamageType damageType;

	public bool ignoreNPC = true;

	public float npcMultiplier = 1f;

	public float resourceMultiplier = 1f;

	public bool triggerHitImpacts = true;

	public bool RequireUpAxis;

	public BaseEntity SourceEntity;

	public bool UseSourceEntityDamageMultiplier = true;

	public bool ignoreAllVehicleMounted;

	public float activationDelay;

	private Dictionary<BaseEntity, float> entryTimes;

	private TimeSince timeSinceAcivation;

	private IHurtTriggerUser hurtTiggerUser;

	public override GameObject InterestedInObject(GameObject obj)
	{
		obj = base.InterestedInObject(obj);
		if (obj == null)
		{
			return null;
		}
		BaseEntity baseEntity = GameObjectEx.ToBaseEntity(obj);
		if (baseEntity == null)
		{
			return null;
		}
		if (baseEntity.isClient)
		{
			return null;
		}
		if (ignoreNPC && baseEntity.IsNpc)
		{
			return null;
		}
		return baseEntity.gameObject;
	}

	public override void OnObjects()
	{
		InvokeRepeating(OnTick, 0f, 1f / DamageTickRate);
	}

	internal override void OnEntityEnter(BaseEntity ent)
	{
		base.OnEntityEnter(ent);
		if (ent != null && DamageDelay > 0f)
		{
			if (entryTimes == null)
			{
				entryTimes = new Dictionary<BaseEntity, float>();
			}
			entryTimes.Add(ent, Time.time);
		}
	}

	internal override void OnEntityLeave(BaseEntity ent)
	{
		if (ent != null && entryTimes != null)
		{
			entryTimes.Remove(ent);
		}
		base.OnEntityLeave(ent);
	}

	public override void OnEmpty()
	{
		CancelInvoke(OnTick);
	}

	protected void OnEnable()
	{
		timeSinceAcivation = 0f;
		hurtTiggerUser = SourceEntity as IHurtTriggerUser;
	}

	public new void OnDisable()
	{
		CancelInvoke(OnTick);
		base.OnDisable();
	}

	private bool IsInterested(BaseEntity ent)
	{
		if ((float)timeSinceAcivation < activationDelay)
		{
			return false;
		}
		BasePlayer basePlayer = ent.ToPlayer();
		if (basePlayer != null)
		{
			if (basePlayer.isMounted)
			{
				BaseVehicle mountedVehicle = basePlayer.GetMountedVehicle();
				if (SourceEntity != null && mountedVehicle == SourceEntity)
				{
					return false;
				}
				if (ignoreAllVehicleMounted && mountedVehicle != null)
				{
					return false;
				}
			}
			if (SourceEntity != null && BaseEntityEx.HasEntityInParents(basePlayer, SourceEntity))
			{
				return false;
			}
		}
		return true;
	}

	private void OnTick()
	{
		if (CollectionEx.IsNullOrEmpty(entityContents))
		{
			return;
		}
		List<BaseEntity> obj = Pool.GetList<BaseEntity>();
		obj.AddRange(entityContents);
		foreach (BaseEntity item in obj)
		{
			if (BaseNetworkableEx.IsValid(item) && IsInterested(item) && (!(DamageDelay > 0f) || entryTimes == null || !entryTimes.TryGetValue(item, out var value) || !(value + DamageDelay > Time.time)) && (!RequireUpAxis || !(Vector3.Dot(item.transform.up, base.transform.up) < 0f)))
			{
				float num = DamagePerSecond * 1f / DamageTickRate;
				if (UseSourceEntityDamageMultiplier && hurtTiggerUser != null)
				{
					num *= hurtTiggerUser.GetDamageMultiplier(item);
				}
				if (item.IsNpc)
				{
					num *= npcMultiplier;
				}
				if (item is ResourceEntity)
				{
					num *= resourceMultiplier;
				}
				Vector3 vector = item.transform.position + Vector3.up * 1f;
				bool flag = item is BasePlayer || item is BaseNpc;
				BaseEntity baseEntity = null;
				BaseEntity weaponPrefab = null;
				if (hurtTiggerUser != null)
				{
					baseEntity = hurtTiggerUser.GetPlayerDamageInitiator();
					weaponPrefab = SourceEntity.LookupPrefab();
				}
				if (baseEntity == null)
				{
					baseEntity = ((!(SourceEntity != null)) ? GameObjectEx.ToBaseEntity(base.gameObject) : SourceEntity);
				}
				HitInfo hitInfo = new HitInfo
				{
					DoHitEffects = true,
					HitEntity = item,
					HitPositionWorld = vector,
					HitPositionLocal = item.transform.InverseTransformPoint(vector),
					HitNormalWorld = Vector3.up,
					HitMaterial = (flag ? StringPool.Get("Flesh") : 0u),
					WeaponPrefab = weaponPrefab,
					Initiator = baseEntity
				};
				hitInfo.damageTypes = new DamageTypeList();
				hitInfo.damageTypes.Set(damageType, num);
				item.OnAttacked(hitInfo);
				if (hurtTiggerUser != null)
				{
					hurtTiggerUser.OnHurtTriggerOccupant(item, damageType, num);
				}
				if (triggerHitImpacts)
				{
					Effect.server.ImpactEffect(hitInfo);
				}
			}
		}
		Pool.FreeList(ref obj);
		RemoveInvalidEntities();
	}
}
