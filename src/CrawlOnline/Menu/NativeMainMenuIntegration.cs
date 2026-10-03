using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CrawlOnline.Menu
{
    internal sealed class NativeMainMenuIntegration : IDisposable
    {
        private const int ScanIntervalFrames = 15;
        private readonly ManualLogSource log;
        private readonly Action selected;
        private readonly Type menuMainType;
        private int framesUntilScan;
        private object installedMenu;
        private MainMenuOnlineBridge bridge;
        private bool contractWarningLogged;

        public NativeMainMenuIntegration(ManualLogSource logSource, Action selectedCallback)
        {
            log = logSource;
            selected = selectedCallback;
            menuMainType = AccessTools.TypeByName("MenuMain");
        }

        public void Tick()
        {
            framesUntilScan--;
            if (framesUntilScan > 0) return;
            framesUntilScan = ScanIntervalFrames;

            if (menuMainType == null)
            {
                WarnContractOnce("MenuMain type is unavailable; native ONLINE entry disabled.");
                return;
            }

            try
            {
                TryInstall();
            }
            catch (Exception exception)
            {
                WarnContractOnce("Native ONLINE entry failed safely: " + DescribeException(exception) + ".");
            }
        }

        public void Dispose()
        {
            TryRemoveInstalledItem();
            if (bridge != null) UnityEngine.Object.Destroy(bridge);
            bridge = null;
            installedMenu = null;
        }

        private void TryInstall()
        {
            object menuMain = FindActiveMenuMain();
            if (menuMain == null) return;

            object menu = ReadRequiredField(menuMain.GetType(), menuMain, "m_menuMain");
            if (menu == null) return;
            IList data = ReadRequiredField(menu.GetType(), menu, "m_itemsData") as IList;
            if (data == null || data.Count == 0) return;
            IList items = ReadRequiredField(menu.GetType(), menu, "m_items") as IList;
            if (items == null || items.Count == 0) return;
            object activeValue = ReadRequiredField(menu.GetType(), menu, "m_active");
            object selectedValue = ReadRequiredField(menu.GetType(), menu, "m_selectedItem");
            if (!(activeValue is bool) || !(bool)activeValue || !(selectedValue is int) ||
                (int)selectedValue < 0) return;
            int selectedIndex = (int)selectedValue;

            string[] messages = ReadMessages(items);
            NativeMenuPlan plan = NativeMenuContract.Evaluate(messages);
            if (plan.Kind == NativeMenuPlanKind.Unsupported)
            {
                WarnContractOnce("Main-menu anchors are incompatible; original menu left untouched.");
                return;
            }

            GameObject owner = ReadRequiredField(menu.GetType(), menu, "m_owner") as GameObject;
            if (owner == null)
            {
                WarnContractOnce("Main-menu owner is unavailable; original menu left untouched.");
                return;
            }
            EnsureBridge(owner);

            if (plan.Kind == NativeMenuPlanKind.AlreadyInstalled)
            {
                installedMenu = menu;
                return;
            }

            if (items.Count != data.Count || plan.Index >= data.Count)
            {
                WarnContractOnce("Main-menu template and rendered items disagree; original menu left untouched.");
                return;
            }

            object itemData = CloneItemData(data[plan.Index]);
            WriteRequiredField(itemData.GetType(), itemData, "m_text", "ONLINE");
            WriteRequiredField(itemData.GetType(), itemData, "m_message", NativeMenuContract.OnlineMessage);
            WriteRequiredField(itemData.GetType(), itemData, "m_enabled", true);
            WriteRequiredField(itemData.GetType(), itemData, "m_visible", true);

            MethodInfo insert = AccessTools.Method(menu.GetType(), "InsertItem",
                new[] { typeof(int), itemData.GetType() });
            if (insert == null) throw new MissingMethodException(menu.GetType().FullName, "InsertItem");
            MethodInfo setSelected = menu.GetType().GetMethod("SetSelectedItem",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null);
            if (setSelected == null)
                throw new MissingMethodException(menu.GetType().FullName, "SetSelectedItem(Int32)");

            int originalCount = items.Count;
            InvokeReflected(insert, menu, new[] { (object)plan.Index, itemData }, "InsertItem");
            int restoredSelection = selectedIndex >= plan.Index ? selectedIndex + 1 : selectedIndex;
            InvokeReflected(setSelected, menu, new object[] { restoredSelection }, "SetSelectedItem");
            IList installedItems = ReadRequiredField(menu.GetType(), menu, "m_items") as IList;
            if (installedItems == null)
                throw new InvalidOperationException("Rendered menu items disappeared after insertion");
            object installedItem = installedItems[plan.Index];
            WriteRequiredField(installedItem.GetType(), installedItem, "m_messageObject", owner);
            string[] installedMessages = ReadMessages(installedItems);
            NativeMenuPlan installedPlan = NativeMenuContract.Evaluate(installedMessages);
            if (installedItems.Count != originalCount + 1 ||
                installedPlan.Kind != NativeMenuPlanKind.AlreadyInstalled)
            {
                TryRemoveItem(menu, plan.Index);
                throw new InvalidOperationException("Menu insertion invariant failed (count " +
                    originalCount + " -> " + installedItems.Count + ", plan " + installedPlan.Kind + ")");
            }

            installedMenu = menu;
            contractWarningLogged = false;
            log.LogInfo("Native ONLINE menu entry installed between START GAME and THE VAULT.");
        }

        private object FindActiveMenuMain()
        {
            UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(menuMainType);
            for (int i = 0; i < objects.Length; i++)
            {
                var component = objects[i] as Component;
                if (component != null && component.gameObject != null && component.gameObject.activeInHierarchy)
                    return objects[i];
            }
            return null;
        }

        private void EnsureBridge(GameObject owner)
        {
            MainMenuOnlineBridge existing = owner.GetComponent<MainMenuOnlineBridge>();
            bridge = existing == null ? owner.AddComponent<MainMenuOnlineBridge>() : existing;
            bridge.Initialise(selected);
        }

        private static object CloneItemData(object source)
        {
            MethodInfo clone = typeof(object).GetMethod("MemberwiseClone",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (clone == null || source == null) throw new MissingMethodException("MemberwiseClone");
            return InvokeReflected(clone, source, null, "MemberwiseClone");
        }

        private static string[] ReadMessages(IList data)
        {
            var messages = new string[data.Count];
            for (int i = 0; i < data.Count; i++)
            {
                object item = data[i];
                messages[i] = ReadRequiredField(item.GetType(), item, "m_message") as string;
            }
            return messages;
        }

        private void TryRemoveInstalledItem()
        {
            if (installedMenu == null) return;
            try
            {
                IList items = ReadRequiredField(installedMenu.GetType(), installedMenu, "m_items") as IList;
                if (items == null) return;
                NativeMenuPlan plan = NativeMenuContract.Evaluate(ReadMessages(items));
                if (plan.Kind == NativeMenuPlanKind.AlreadyInstalled) TryRemoveItem(installedMenu, plan.Index);
            }
            catch (Exception exception)
            {
                log.LogWarning("Could not remove native ONLINE entry during shutdown: " +
                    exception.GetType().Name + ".");
            }
        }

        private static void TryRemoveItem(object menu, int index)
        {
            MethodInfo remove = AccessTools.Method(menu.GetType(), "RemoveItem", new[] { typeof(int) });
            if (remove != null) InvokeReflected(remove, menu, new object[] { index }, "RemoveItem");
        }

        private static object ReadRequiredField(Type type, object instance, string name)
        {
            FieldInfo field = AccessTools.Field(type, name);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            return field.GetValue(instance);
        }

        private static void WriteRequiredField(Type type, object instance, string name, object value)
        {
            FieldInfo field = AccessTools.Field(type, name);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            field.SetValue(instance, value);
        }

        private static object InvokeReflected(MethodInfo method, object instance, object[] arguments,
            string operation)
        {
            try
            {
                return method.Invoke(instance, arguments);
            }
            catch (TargetInvocationException exception)
            {
                throw new InvalidOperationException(operation + " invocation failed",
                    exception.InnerException ?? exception);
            }
        }

        private void WarnContractOnce(string message)
        {
            if (contractWarningLogged) return;
            contractWarningLogged = true;
            log.LogWarning(message);
        }

        private static string DescribeException(Exception exception)
        {
            string description = exception.GetType().Name;
            if (!string.IsNullOrEmpty(exception.Message)) description += ": " + exception.Message;
            Exception current = exception.InnerException;
            int depth = 0;
            while (current != null && depth < 3)
            {
                description += " -> " + current.GetType().Name;
                if (!string.IsNullOrEmpty(current.Message)) description += ": " + current.Message;
                current = current.InnerException;
                depth++;
            }
            return description;
        }
    }
}
