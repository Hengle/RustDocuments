using Characters.Projectiles;
using Characters.Utils;
using UnityEngine;

namespace Characters.Operations.Customs.AquaSkull
{
	public class FireHighTideProjectile : CharacterOperation
	{
		public enum DirectionType
		{
			RotationOfFirePosition,
			OwnerDirection,
			Constant
		}

		[SerializeField]
		private Projectile _projectile;

		[Header("Special Setting")]
		[SerializeField]
		private Projectile[] _projectilesToCount;

		[SerializeField]
		[Tooltip("발사 순서 * _fireInterval 만큼 대기한 후 발사됨")]
		private float _fireInterval;

		[SerializeField]
		private float[] _countsByCount;

		[Space]
		[SerializeField]
		private CustomFloat _speedMultiplier = new CustomFloat(1f);

		[SerializeField]
		private CustomFloat _damageMultiplier = new CustomFloat(1f);

		[SerializeField]
		private CustomFloat _scale = new CustomFloat(1f);

		[Space]
		[SerializeField]
		private Transform _fireTransform;

		[SerializeField]
		private bool _group;

		[SerializeField]
		private bool _flipXByOwnerDirection;

		[SerializeField]
		private bool _flipYByOwnerDirection;

		[Space]
		[SerializeField]
		private DirectionType _directionType;

		[SerializeField]
		private CustomAngle _direction;

		private IAttackDamage _attackDamage;

		public CustomFloat scale => _scale;

		public override void Initialize()
		{
			_attackDamage = GetComponentInParent<IAttackDamage>();
			if (_fireTransform == null)
			{
				_fireTransform = base.transform;
			}
		}

		private float GetPorjectileCount()
		{
			int num = 0;
			Projectile[] projectilesToCount = _projectilesToCount;
			foreach (Projectile projectile in projectilesToCount)
			{
				num += projectile.reusable.spawnedCount;
			}
			int num2 = Mathf.Clamp(num, 0, _countsByCount.Length - 1);
			return _countsByCount[num2];
		}

		public override void Run(Character owner)
		{
			float porjectileCount = GetPorjectileCount();
			float attackDamage = _attackDamage.amount * _damageMultiplier.value;
			bool flipX = false;
			bool flipY = false;
			HitHistoryManager hitHistoryManager = (_group ? new HitHistoryManager(15) : null);
			for (int i = 0; (float)i < porjectileCount; i++)
			{
				float num;
				switch (_directionType)
				{
				case DirectionType.RotationOfFirePosition:
					num = _fireTransform.rotation.eulerAngles.z + _direction.value;
					if (_fireTransform.lossyScale.x < 0f)
					{
						num = (180f - num) % 360f;
					}
					break;
				case DirectionType.OwnerDirection:
				{
					num = _direction.value;
					bool flag = owner.lookingDirection == Character.LookingDirection.Left || _fireTransform.lossyScale.x < 0f;
					flipX = _flipXByOwnerDirection && flag;
					flipY = _flipYByOwnerDirection && flag;
					num = (flag ? ((180f - num) % 360f) : num);
					break;
				}
				default:
					num = _direction.value;
					break;
				}
				Projectile component = _projectile.reusable.Spawn(_fireTransform.position).GetComponent<Projectile>();
				component.transform.localScale = Vector3.one * _scale.value;
				component.Fire(owner, attackDamage, num, flipX, flipY, _speedMultiplier.value, _group ? hitHistoryManager : null, _fireInterval * (float)i);
			}
		}
	}
}