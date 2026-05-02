using System;
using System.Collections;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace CampfireUpgradeBugFix2;

[ModInitializer(nameof(Initialize))]
public static class MainFile
{
	public static void Initialize()
	{
		var harmony = new Harmony("com.yourname.sts2.campfire_upgrade_bug_fix2");
		harmony.PatchAll(typeof(MainFile).Assembly);

		Log.Warn("[CampfireUpgradeBugFix2] Loaded.");
	}
}

internal static class ReflectUtil
{
	public static object GetFieldValue(object instance, string fieldName)
	{
		for (Type t = instance.GetType(); t != null; t = t.BaseType)
		{
			FieldInfo field = t.GetField(
				fieldName,
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
			);

			if (field != null)
			{
				return field.GetValue(instance);
			}
		}

		return null;
	}

	public static object GetPropertyValue(object instance, string propertyName)
	{
		if (instance == null)
		{
			return null;
		}

		PropertyInfo prop = instance.GetType().GetProperty(
			propertyName,
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
		);

		if (prop == null)
		{
			return null;
		}

		return prop.GetValue(instance);
	}

	public static void SetEnumProperty(object instance, string propertyName, string enumName)
	{
		if (instance == null)
		{
			return;
		}

		PropertyInfo prop = instance.GetType().GetProperty(
			propertyName,
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
		);

		if (prop == null || !prop.PropertyType.IsEnum)
		{
			return;
		}

		object value = Enum.Parse(prop.PropertyType, enumName);
		prop.SetValue(instance, value);
	}

	public static MethodInfo FindMethod(Type type, string methodName, int parameterCount)
	{
		for (Type t = type; t != null; t = t.BaseType)
		{
			MethodInfo[] methods = t.GetMethods(
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
			);

			foreach (MethodInfo method in methods)
			{
				if (method.Name == methodName && method.GetParameters().Length == parameterCount)
				{
					return method;
				}
			}
		}

		return null;
	}

	public static object GetLastSelectedCard(object screen)
	{
		object selectedCardsObj = GetFieldValue(screen, "_selectedCards");

		if (selectedCardsObj is not IEnumerable selectedCards)
		{
			return null;
		}

		object last = null;

		foreach (object card in selectedCards)
		{
			last = card;
		}

		return last;
	}

	public static object GetGrid(object screen)
	{
		return GetFieldValue(screen, "_grid");
	}

	public static Control GetGridControlProperty(object screen, string propertyName)
	{
		object grid = GetGrid(screen);

		if (grid == null)
		{
			return null;
		}

		object value = GetPropertyValue(grid, propertyName);

		if (value is Control control)
		{
			return control;
		}

		return null;
	}
}

internal static class SharedPatchLogic
{
	internal static void OnCardClickedPostfix(Node instance, string label)
	{
		try
		{
			object grid = ReflectUtil.GetGrid(instance);

			if (grid == null)
			{
				Log.Warn($"[CampfireUpgradeBugFix2] {label}: _grid not found.");
				return;
			}

			ReflectUtil.SetEnumProperty(grid, "FocusBehaviorRecursive", "Inherited");

			object lastSelectedCard = ReflectUtil.GetLastSelectedCard(instance);

			if (lastSelectedCard != null)
			{
				RefocusCardHolder(grid, lastSelectedCard, label);
			}

			ActiveScreenContext.Instance.Update();

			Viewport vp = instance.GetViewport();
			if (vp != null)
			{
				vp.GuiReleaseFocus();
			}

			Log.Warn($"[CampfireUpgradeBugFix2] {label}: Seeded grid focus context, then released GUI focus.");
		}
		catch (Exception e)
		{
			Log.Error($"[CampfireUpgradeBugFix2] {label} OnCardClicked failed: " + e);
		}
	}

	internal static void DefaultFocusedControlPostfix(Node instance, ref Control __result, string label)
	{
		try
		{
			if (__result != null)
			{
				return;
			}

			Control gridDefault = ReflectUtil.GetGridControlProperty(instance, "DefaultFocusedControl");

			if (gridDefault != null)
			{
				__result = gridDefault;
			}
		}
		catch (Exception e)
		{
			Log.Error($"[CampfireUpgradeBugFix2] {label} DefaultFocusedControl failed: " + e);
		}
	}

	internal static void FocusedControlFromTopBarPostfix(Node instance, ref Control __result, string label)
	{
		try
		{
			if (__result != null)
			{
				return;
			}

			Control gridFocus = ReflectUtil.GetGridControlProperty(instance, "FocusedControlFromTopBar");

			if (gridFocus != null)
			{
				__result = gridFocus;
			}
		}
		catch (Exception e)
		{
			Log.Error($"[CampfireUpgradeBugFix2] {label} FocusedControlFromTopBar failed: " + e);
		}
	}

	private static void RefocusCardHolder(object grid, object card, string label)
	{
		MethodInfo getCardHolder = ReflectUtil.FindMethod(grid.GetType(), "GetCardHolder", 1);

		if (getCardHolder == null)
		{
			Log.Warn($"[CampfireUpgradeBugFix2] {label}: GetCardHolder not found.");
			return;
		}

		object holder = getCardHolder.Invoke(grid, new object[] { card });

		if (holder == null)
		{
			Log.Warn($"[CampfireUpgradeBugFix2] {label}: card holder is null.");
			return;
		}

		MethodInfo tryGrabFocus = ReflectUtil.FindMethod(holder.GetType(), "TryGrabFocus", 0);

		if (tryGrabFocus != null)
		{
			tryGrabFocus.Invoke(holder, null);
		}

		if (holder is Control control)
		{
			control.GrabFocus();
			control.CallDeferred(Control.MethodName.GrabFocus);
		}
	}
}

[HarmonyPatch(typeof(NDeckUpgradeSelectScreen), "OnCardClicked")]
internal static class Patch_Upgrade_OnCardClicked
{
	private static void Postfix(NDeckUpgradeSelectScreen __instance) =>
		SharedPatchLogic.OnCardClickedPostfix(__instance, "Upgrade");
}

[HarmonyPatch(typeof(NDeckUpgradeSelectScreen), "get_DefaultFocusedControl")]
internal static class Patch_Upgrade_DefaultFocusedControl
{
	private static void Postfix(NDeckUpgradeSelectScreen __instance, ref Control __result) =>
		SharedPatchLogic.DefaultFocusedControlPostfix(__instance, ref __result, "Upgrade");
}

[HarmonyPatch(typeof(NDeckUpgradeSelectScreen), "get_FocusedControlFromTopBar")]
internal static class Patch_Upgrade_FocusedControlFromTopBar
{
	private static void Postfix(NDeckUpgradeSelectScreen __instance, ref Control __result) =>
		SharedPatchLogic.FocusedControlFromTopBarPostfix(__instance, ref __result, "Upgrade");
}

[HarmonyPatch(typeof(NDeckTransformSelectScreen), "OnCardClicked")]
internal static class Patch_Transform_OnCardClicked
{
	private static void Postfix(NDeckTransformSelectScreen __instance) =>
		SharedPatchLogic.OnCardClickedPostfix(__instance, "Transform");
}

[HarmonyPatch(typeof(NDeckTransformSelectScreen), "get_DefaultFocusedControl")]
internal static class Patch_Transform_DefaultFocusedControl
{
	private static void Postfix(NDeckTransformSelectScreen __instance, ref Control __result) =>
		SharedPatchLogic.DefaultFocusedControlPostfix(__instance, ref __result, "Transform");
}

[HarmonyPatch(typeof(NDeckTransformSelectScreen), "get_FocusedControlFromTopBar")]
internal static class Patch_Transform_FocusedControlFromTopBar
{
	private static void Postfix(NDeckTransformSelectScreen __instance, ref Control __result) =>
		SharedPatchLogic.FocusedControlFromTopBarPostfix(__instance, ref __result, "Transform");
}

[HarmonyPatch(typeof(NDeckCardSelectScreen), "OnCardClicked")]
internal static class Patch_CardSelect_OnCardClicked
{
	private static void Postfix(NDeckCardSelectScreen __instance) =>
		SharedPatchLogic.OnCardClickedPostfix(__instance, "CardSelect");
}

[HarmonyPatch(typeof(NDeckCardSelectScreen), "get_DefaultFocusedControl")]
internal static class Patch_CardSelect_DefaultFocusedControl
{
	private static void Postfix(NDeckCardSelectScreen __instance, ref Control __result) =>
		SharedPatchLogic.DefaultFocusedControlPostfix(__instance, ref __result, "CardSelect");
}

[HarmonyPatch(typeof(NDeckCardSelectScreen), "get_FocusedControlFromTopBar")]
internal static class Patch_CardSelect_FocusedControlFromTopBar
{
	private static void Postfix(NDeckCardSelectScreen __instance, ref Control __result) =>
		SharedPatchLogic.FocusedControlFromTopBarPostfix(__instance, ref __result, "CardSelect");
}

[HarmonyPatch(typeof(NDeckEnchantSelectScreen), "OnCardClicked")]
internal static class Patch_Enchant_OnCardClicked
{
	private static void Postfix(NDeckEnchantSelectScreen __instance) =>
		SharedPatchLogic.OnCardClickedPostfix(__instance, "Enchant");
}

[HarmonyPatch(typeof(NDeckEnchantSelectScreen), "get_DefaultFocusedControl")]
internal static class Patch_Enchant_DefaultFocusedControl
{
	private static void Postfix(NDeckEnchantSelectScreen __instance, ref Control __result) =>
		SharedPatchLogic.DefaultFocusedControlPostfix(__instance, ref __result, "Enchant");
}

[HarmonyPatch(typeof(NDeckEnchantSelectScreen), "get_FocusedControlFromTopBar")]
internal static class Patch_Enchant_FocusedControlFromTopBar
{
	private static void Postfix(NDeckEnchantSelectScreen __instance, ref Control __result) =>
		SharedPatchLogic.FocusedControlFromTopBarPostfix(__instance, ref __result, "Enchant");
}