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

[HarmonyPatch(typeof(NDeckUpgradeSelectScreen), "OnCardClicked")]
internal static class Patch_OnCardClicked
{
	private static void Postfix(NDeckUpgradeSelectScreen __instance)
	{
		try
		{
			object grid = ReflectUtil.GetGrid(__instance);

			if (grid == null)
			{
				Log.Warn("[CampfireUpgradeBugFix2] _grid not found.");
				return;
			}

			// 0.103 原版会把这里设成 Disabled。
			// 这里恢复成 0.99 近似状态。
			ReflectUtil.SetEnumProperty(grid, "FocusBehaviorRecursive", "Inherited");

			object lastSelectedCard = ReflectUtil.GetLastSelectedCard(__instance);

			// 第一次保留：把 holder/grid 的焦点关系种回去
			if (lastSelectedCard != null)
			{
				RefocusCardHolder(grid, lastSelectedCard);
			}

			// 刷新 screen context，让它重新识别 grid 的焦点来源
			ActiveScreenContext.Instance.Update();

			// 关键改动：
			// 不再做第二次 RefocusCardHolder。
			// 改成在更新完 context 之后，主动释放当前 GUI 焦点。
			// 效果就是“选完第一张后表面上焦点消失”，
			// 但下一次方向键仍会从 grid 的上下文继续导航。
			Viewport vp = __instance.GetViewport();
			if (vp != null)
			{
				vp.GuiReleaseFocus();
			}

			Log.Warn("[CampfireUpgradeBugFix2] Seeded grid focus context, then released GUI focus.");
		}
		catch (Exception e)
		{
			Log.Error("[CampfireUpgradeBugFix2] Patch_OnCardClicked failed: " + e);
		}
	}

	private static void RefocusCardHolder(object grid, object card)
	{
		MethodInfo getCardHolder = ReflectUtil.FindMethod(grid.GetType(), "GetCardHolder", 1);

		if (getCardHolder == null)
		{
			Log.Warn("[CampfireUpgradeBugFix2] GetCardHolder not found.");
			return;
		}

		object holder = getCardHolder.Invoke(grid, new object[] { card });

		if (holder == null)
		{
			Log.Warn("[CampfireUpgradeBugFix2] card holder is null.");
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

[HarmonyPatch(typeof(NDeckUpgradeSelectScreen), "get_DefaultFocusedControl")]
internal static class Patch_DefaultFocusedControl
{
	private static void Postfix(NDeckUpgradeSelectScreen __instance, ref Control __result)
	{
		try
		{
			if (__result != null)
			{
				return;
			}

			Control gridDefault = ReflectUtil.GetGridControlProperty(__instance, "DefaultFocusedControl");

			if (gridDefault != null)
			{
				__result = gridDefault;
			}
		}
		catch (Exception e)
		{
			Log.Error("[CampfireUpgradeBugFix2] Patch_DefaultFocusedControl failed: " + e);
		}
	}
}

[HarmonyPatch(typeof(NDeckUpgradeSelectScreen), "get_FocusedControlFromTopBar")]
internal static class Patch_FocusedControlFromTopBar
{
	private static void Postfix(NDeckUpgradeSelectScreen __instance, ref Control __result)
	{
		try
		{
			if (__result != null)
			{
				return;
			}

			Control gridFocus = ReflectUtil.GetGridControlProperty(__instance, "FocusedControlFromTopBar");

			if (gridFocus != null)
			{
				__result = gridFocus;
			}
		}
		catch (Exception e)
		{
			Log.Error("[CampfireUpgradeBugFix2] Patch_FocusedControlFromTopBar failed: " + e);
		}
	}
}
