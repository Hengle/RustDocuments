public static class BaseEntityEx
{
	public static bool IsValid(this BaseEntity ent)
	{
		if (ent == null)
		{
			return false;
		}
		if (ent.net == null)
		{
			return false;
		}
		return true;
	}

	public static bool IsRealNull(this BaseEntity ent)
	{
		return (object)ent == null;
	}

	public static bool IsValidEntityReference<T>(this T obj) where T : class
	{
		return obj as BaseEntity != null;
	}

	public static bool HasEntityInParents(this BaseEntity ent, BaseEntity toFind)
	{
		if (ent == null || toFind == null)
		{
			return false;
		}
		if (ent == toFind || ent.EqualNetID(toFind))
		{
			return true;
		}
		BaseEntity parentEntity = ent.GetParentEntity();
		while (parentEntity != null)
		{
			if (parentEntity == toFind || parentEntity.EqualNetID(toFind))
			{
				return true;
			}
			parentEntity = parentEntity.GetParentEntity();
		}
		return false;
	}
}