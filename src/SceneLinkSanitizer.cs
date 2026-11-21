// RECONSTRUIDO por descompilacao (ilspycmd 11.1) do CreatureSpawner.dll instalado (build de 07/11/2026 17:36): o .cs
// original sumiu do scratchpad antigo (limpeza de pasta temporaria). Comentarios originais perdidos; codigo = o compilado.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HutongGames.PlayMaker;
using UnityEngine;

namespace CreatureSpawner;

internal static class SceneLinkSanitizer
{
	private sealed class RefEq : IEqualityComparer<UnityEngine.Object>
	{
		public bool Equals(UnityEngine.Object a, UnityEngine.Object b)
		{
			return (object)a == b;
		}

		public int GetHashCode(UnityEngine.Object o)
		{
			return RuntimeHelpers.GetHashCode(o);
		}
	}

	private sealed class Ctx
	{
		public HashSet<UnityEngine.Object> Own = new HashSet<UnityEngine.Object>(new RefEq());

		public List<string> Cleared = new List<string>();

		public bool IsBad(UnityEngine.Object o, Type declared)
		{
			if ((object)o == null)
			{
				return false;
			}
			if (declared != null && !declared.IsInstanceOfType(o))
			{
				return true;
			}
			if (Own.Contains(o))
			{
				return false;
			}
			if (BundleObjects.Contains(o))
			{
				return true;
			}
			if (o is AssetBundle || o is RenderSettings)
			{
				return true;
			}
			return false;
		}
	}

	private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private static readonly HashSet<UnityEngine.Object> BundleObjects = new HashSet<UnityEngine.Object>(new RefEq());

	private static readonly FieldInfo FSendKilledToName = typeof(HealthManager).GetField("sendKilledToName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly FieldInfo FFsmObjectValue = typeof(FsmObject).GetField("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static readonly Dictionary<string, FieldInfo> ActionDataFields = new string[7] { "unityObjectParams", "fsmGameObjectParams", "fsmOwnerDefaultParams", "fsmEventTargetParams", "fsmObjectParams", "fsmVarParams", "fsmArrayParams" }.ToDictionary((string n) => n, (string n) => typeof(ActionData).GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

	public static bool BundleFound { get; private set; }

	public static string BuildBundleIndex(IEnumerable<string> expectedAssetNames)
	{
		BundleObjects.Clear();
		BundleFound = false;
		List<string> source = expectedAssetNames.Select((string n) => n.ToLowerInvariant()).ToList();
		AssetBundle assetBundle = null;
		foreach (AssetBundle allLoadedAssetBundle in AssetBundle.GetAllLoadedAssetBundles())
		{
			if (allLoadedAssetBundle != null && source.Any(allLoadedAssetBundle.Contains))
			{
				assetBundle = allLoadedAssetBundle;
				break;
			}
		}
		if (assetBundle == null)
		{
			return "bundle de preload nao encontrado (modo de preload != RepackAssets?) - so a checagem de tipo sera usada";
		}
		int num = 0;
		string[] allAssetNames = assetBundle.GetAllAssetNames();
		foreach (string name in allAssetNames)
		{
			GameObject gameObject = assetBundle.LoadAsset<GameObject>(name);
			if ((object)gameObject == null || gameObject == null)
			{
				continue;
			}
			num++;
			Component[] componentsInChildren = gameObject.GetComponentsInChildren<Component>(includeInactive: true);
			foreach (Component component in componentsInChildren)
			{
				if ((object)component != null)
				{
					BundleObjects.Add(component);
					BundleObjects.Add(component.gameObject);
				}
			}
			Transform parent = gameObject.transform.parent;
			for (int num4 = 0; num4 < 64; num4++)
			{
				if ((object)parent == null)
				{
					break;
				}
				if (!(parent != null))
				{
					break;
				}
				BundleObjects.Add(parent);
				BundleObjects.Add(parent.gameObject);
				parent = parent.parent;
			}
		}
		BundleFound = true;
		return $"bundle '{assetBundle.name}': {num} prefabs, {BundleObjects.Count} objetos indexados";
	}

	public static List<string> Sanitize(GameObject root)
	{
		Ctx ctx = new Ctx();
		Component[] componentsInChildren = root.GetComponentsInChildren<Component>(includeInactive: true);
		foreach (Component component in componentsInChildren)
		{
			if ((object)component != null)
			{
				ctx.Own.Add(component);
				ctx.Own.Add(component.gameObject);
			}
		}
		MonoBehaviour[] componentsInChildren2 = root.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
		foreach (MonoBehaviour monoBehaviour in componentsInChildren2)
		{
			if ((object)monoBehaviour != null)
			{
				if (monoBehaviour is PlayMakerFSM pm)
				{
					SanitizeFsm(pm, ctx);
				}
				else
				{
					SanitizeFields(monoBehaviour, ctx);
				}
				if (monoBehaviour is HealthManager healthManager && FSendKilledToName != null && FSendKilledToName.GetValue(healthManager) is string { Length: >0 } text)
				{
					FSendKilledToName.SetValue(healthManager, null);
					ctx.Cleared.Add(healthManager.name + ".sendKilledToName='" + text + "'");
				}
			}
		}
		return ctx.Cleared;
	}

	private static void SanitizeFields(MonoBehaviour mb, Ctx ctx)
	{
		Type type = mb.GetType();
		while (type != null && type != typeof(MonoBehaviour))
		{
			FieldInfo[] fields = type.GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (FieldInfo fieldInfo in fields)
			{
				if (fieldInfo.IsInitOnly || fieldInfo.IsLiteral || ((!fieldInfo.IsPublic || fieldInfo.IsNotSerialized) && !fieldInfo.IsDefined(typeof(SerializeField), inherit: false)))
				{
					continue;
				}
				Type fieldType = fieldInfo.FieldType;
				string text = mb.name + ":" + type.Name + "." + fieldInfo.Name;
				if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
				{
					if (ctx.IsBad(fieldInfo.GetValue(mb) as UnityEngine.Object, fieldType))
					{
						fieldInfo.SetValue(mb, null);
						ctx.Cleared.Add(text);
					}
				}
				else if (fieldType.IsArray && typeof(UnityEngine.Object).IsAssignableFrom(fieldType.GetElementType()))
				{
					if (fieldInfo.GetValue(mb) is Array list)
					{
						ClearInList(list, fieldType.GetElementType(), text, ctx);
					}
				}
				else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>) && typeof(UnityEngine.Object).IsAssignableFrom(fieldType.GetGenericArguments()[0]) && fieldInfo.GetValue(mb) is IList list2)
				{
					ClearInList(list2, fieldType.GetGenericArguments()[0], text, ctx);
				}
			}
			type = type.BaseType;
		}
	}

	private static void ClearInList(IList list, Type elemType, string label, Ctx ctx)
	{
		for (int i = 0; i < list.Count; i++)
		{
			if (ctx.IsBad(list[i] as UnityEngine.Object, elemType))
			{
				list[i] = null;
				ctx.Cleared.Add($"{label}[{i}]");
			}
		}
	}

	private static void SanitizeFsm(PlayMakerFSM pm, Ctx ctx)
	{
		string text = pm.name + "/" + pm.FsmName;
		FsmVariables fsmVariables = pm.FsmVariables;
		if (fsmVariables != null)
		{
			FsmGameObject[] gameObjectVariables = fsmVariables.GameObjectVariables;
			foreach (FsmGameObject fsmGameObject in gameObjectVariables)
			{
				CheckFsmGameObject(fsmGameObject, text + ".var '" + fsmGameObject?.Name + "'", ctx);
			}
			FsmObject[] objectVariables = fsmVariables.ObjectVariables;
			foreach (FsmObject fsmObject in objectVariables)
			{
				CheckFsmObject(fsmObject, text + ".var '" + fsmObject?.Name + "'", ctx);
			}
			FsmArray[] arrayVariables = fsmVariables.ArrayVariables;
			foreach (FsmArray fsmArray in arrayVariables)
			{
				CheckFsmArray(fsmArray, text + ".var '" + fsmArray?.Name + "'", ctx);
			}
		}
		if (pm.UsesTemplate)
		{
			return;
		}
		FsmState[] array = pm.Fsm?.States;
		if (array == null)
		{
			return;
		}
		FsmState[] array2 = array;
		foreach (FsmState fsmState in array2)
		{
			ActionData actionData = fsmState?.ActionData;
			if (actionData == null)
			{
				continue;
			}
			string text2 = text + "['" + fsmState.Name + "']";
			if (ActionDataFields["unityObjectParams"]?.GetValue(actionData) is List<UnityEngine.Object> list)
			{
				ClearInList(list, null, text2 + ".unityObject", ctx);
			}
			if (ActionDataFields["fsmGameObjectParams"]?.GetValue(actionData) is List<FsmGameObject> list2)
			{
				foreach (FsmGameObject item in list2)
				{
					CheckFsmGameObject(item, text2 + ".gameObject", ctx);
				}
			}
			if (ActionDataFields["fsmOwnerDefaultParams"]?.GetValue(actionData) is List<FsmOwnerDefault> list3)
			{
				foreach (FsmOwnerDefault item2 in list3)
				{
					CheckFsmGameObject(item2?.GameObject, text2 + ".ownerDefault", ctx);
				}
			}
			if (ActionDataFields["fsmEventTargetParams"]?.GetValue(actionData) is List<FsmEventTarget> list4)
			{
				foreach (FsmEventTarget item3 in list4)
				{
					if (item3 != null)
					{
						CheckFsmGameObject(item3.gameObject?.GameObject, text2 + ".eventTarget", ctx);
						if (ctx.IsBad(item3.fsmComponent, typeof(PlayMakerFSM)))
						{
							item3.fsmComponent = null;
							ctx.Cleared.Add(text2 + ".eventTarget.fsmComponent");
						}
					}
				}
			}
			if (ActionDataFields["fsmObjectParams"]?.GetValue(actionData) is List<FsmObject> list5)
			{
				foreach (FsmObject item4 in list5)
				{
					CheckFsmObject(item4, text2 + ".object", ctx);
				}
			}
			if (ActionDataFields["fsmArrayParams"]?.GetValue(actionData) is List<FsmArray> list6)
			{
				foreach (FsmArray item5 in list6)
				{
					CheckFsmArray(item5, text2 + ".array", ctx);
				}
			}
			if (!(ActionDataFields["fsmVarParams"]?.GetValue(actionData) is List<FsmVar> list7))
			{
				continue;
			}
			foreach (FsmVar item6 in list7)
			{
				if (item6 != null)
				{
					if (ctx.IsBad(item6.objectReference, null))
					{
						item6.objectReference = null;
						ctx.Cleared.Add(text2 + ".var.objectReference");
					}
					CheckFsmArray(item6.arrayValue, text2 + ".var.array", ctx);
				}
			}
		}
	}

	private static void CheckFsmGameObject(FsmGameObject v, string label, Ctx ctx)
	{
		if (v != null && ctx.IsBad(v.RawValue as UnityEngine.Object, typeof(GameObject)))
		{
			v.RawValue = null;
			v.Value = null;
			ctx.Cleared.Add(label);
		}
	}

	private static void CheckFsmObject(FsmObject v, string label, Ctx ctx)
	{
		if (v != null && !(FFsmObjectValue == null) && ctx.IsBad(FFsmObjectValue.GetValue(v) as UnityEngine.Object, null))
		{
			FFsmObjectValue.SetValue(v, null);
			ctx.Cleared.Add(label);
		}
	}

	private static void CheckFsmArray(FsmArray a, string label, Ctx ctx)
	{
		if (a?.objectReferences != null)
		{
			ClearInList(a.objectReferences, null, label, ctx);
		}
	}
}
