using System;
using System.Collections;
using System.Reflection;
using BepInEx.Logging;
using CrawlOnline.Online;
using HarmonyLib;
using UnityEngine;

namespace CrawlOnline.Menu
{
    internal sealed class NativeMainMenuIntegration : IDisposable
    {
        private const int ScanIntervalFrames = 15;
        private readonly ManualLogSource log;
        private readonly Func<bool> selected;
        private readonly Action hostSelected;
        private readonly Action joinSelected;
        private readonly Action backSelected;
        private readonly Action inviteSelected;
        private readonly Action cancelSelected;
        private readonly Action refreshSelected;
        private readonly Action<int> friendSelected;
        private readonly Type menuMainType;
        private readonly bool simulationMode;
        private int framesUntilScan;
        private object installedMenu;
        private MainMenuOnlineBridge bridge;
        private bool contractWarningLogged;
        private bool submenuOpen;

        public NativeMainMenuIntegration(ManualLogSource logSource, Func<bool> selectedCallback,
            Action hostCallback, Action joinCallback, Action backCallback, Action inviteCallback,
            Action cancelCallback, Action refreshCallback, Action<int> friendCallback,
            bool localSimulationMode)
        {
            log = logSource;
            selected = selectedCallback;
            hostSelected = hostCallback;
            joinSelected = joinCallback;
            backSelected = backCallback;
            inviteSelected = inviteCallback;
            cancelSelected = cancelCallback;
            refreshSelected = refreshCallback;
            friendSelected = friendCallback;
            simulationMode = localSimulationMode;
            menuMainType = AccessTools.TypeByName("MenuMain");
        }

        public void Tick()
        {
            framesUntilScan--;
            if (framesUntilScan > 0) return;
            framesUntilScan = ScanIntervalFrames;

            if (submenuOpen)
            {
                var component = installedMenu as Component;
                if (component != null && component.gameObject != null && component.gameObject.activeInHierarchy)
                    return;
                submenuOpen = false;
                installedMenu = null;
                if (backSelected != null) backSelected();
            }

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
            if (submenuOpen) TryRestoreOriginalMenu();
            else TryRemoveInstalledItem();
            if (bridge != null) UnityEngine.Object.Destroy(bridge);
            bridge = null;
            installedMenu = null;
            submenuOpen = false;
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
            SetSelectedItemAndObserve(menu, restoredSelection, "install-online");
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
            bridge.Initialise(OpenSubmenu, OnHostSelected, OnJoinSelected, CloseSubmenu,
                OnInviteSelected, OnCancelSelected);
            bridge.InitialiseJoin(OnRefreshSelected, OnFriendSelected);
            bridge.InitialiseFocusProbe(log);
        }

        public void ShowHostWaiting()
        {
            if (!submenuOpen || installedMenu == null) return;
            try
            {
                IList data = ReadRequiredField(installedMenu.GetType(), installedMenu, "m_itemsData") as IList;
                GameObject owner = ReadRequiredField(installedMenu.GetType(), installedMenu, "m_owner") as GameObject;
                if (data == null || data.Count < 2 || owner == null)
                    throw new InvalidOperationException("Main-menu template is unavailable");
                RemoveAllRenderedItems(installedMenu);
                InsertRenderedItem(installedMenu, 0, data[0],
                    simulationMode ? "DROP / RECONNECT" : "INVITE FRIENDS",
                    "MsgCrawlOnlineInvite", owner);
                InsertRenderedItem(installedMenu, 1, data[1], "CANCEL", "MsgCrawlOnlineCancel", owner);
                SetMenuActive(installedMenu);
                SetSelectedItemAndObserve(installedMenu, 0, "host-waiting");
                log.LogInfo("Native Online submenu entered host waiting mode.");
            }
            catch (Exception exception)
            {
                WarnContractOnce("Host waiting menu failed safely: " + DescribeException(exception) + ".");
                if (cancelSelected != null) cancelSelected();
            }
        }

        public void CloseSessionMenu()
        {
            CloseSubmenu();
        }

        public bool CanAcceptExternalJoin
        {
            get
            {
                var component = installedMenu as Component;
                if (component == null || component.gameObject == null ||
                    !component.gameObject.activeInHierarchy) return false;
                try
                {
                    object active = ReadRequiredField(installedMenu.GetType(), installedMenu, "m_active");
                    return active is bool && (bool)active;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool IsActiveMainMenu
        {
            get
            {
                var component = installedMenu as Component;
                if (component == null || component.gameObject == null ||
                    !component.gameObject.activeInHierarchy) return false;
                try
                {
                    object active = ReadRequiredField(installedMenu.GetType(), installedMenu, "m_active");
                    return active is bool && (bool)active;
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool OpenForExternalJoin()
        {
            if (!submenuOpen) OpenSubmenu();
            return submenuOpen;
        }

        public void ShowFriendSearch()
        {
            ReplaceJoinMenu(new[] { "SEARCHING...", "BACK" },
                new[] { "MsgCrawlOnlineNoop", "MsgCrawlOnlineCancel" }, 0);
        }

        public void ShowFriendLobbies(int count)
        {
            FriendLobbyMenuPlan plan = FriendLobbyMenuPlan.Create(count);
            ReplaceJoinMenu(plan.Labels, plan.Messages, plan.SelectedIndex);
        }

        public void ShowFriendSearchError()
        {
            ReplaceJoinMenu(new[] { "SEARCH FAILED", "RETRY", "BACK" },
                new[] { "MsgCrawlOnlineNoop", "MsgCrawlOnlineRefresh", "MsgCrawlOnlineCancel" }, 1);
        }

        public void ShowFriendJoining()
        {
            ReplaceJoinMenu(new[] { "JOINING...", "BACK" },
                new[] { "MsgCrawlOnlineNoop", "MsgCrawlOnlineCancel" }, 0);
        }

        public void ShowFriendConnected()
        {
            ReplaceJoinMenu(new[] { "CONNECTED", "LEAVE" },
                new[] { "MsgCrawlOnlineNoop", "MsgCrawlOnlineCancel" }, 1);
        }

        private void ReplaceJoinMenu(string[] labels, string[] messages, int selectedIndex)
        {
            if (!submenuOpen || installedMenu == null || labels == null || messages == null ||
                labels.Length != messages.Length) return;
            try
            {
                IList data = ReadRequiredField(installedMenu.GetType(), installedMenu, "m_itemsData") as IList;
                GameObject owner = ReadRequiredField(installedMenu.GetType(), installedMenu, "m_owner") as GameObject;
                if (data == null || data.Count < 2 || owner == null)
                    throw new InvalidOperationException("Main-menu template is unavailable");
                RemoveAllRenderedItems(installedMenu);
                for (int i = 0; i < labels.Length; i++)
                    InsertRenderedItem(installedMenu, i, data[Math.Min(i, 1)], labels[i], messages[i], owner);
                SetMenuActive(installedMenu);
                SetSelectedItemAndObserve(installedMenu, selectedIndex, "join-menu");
            }
            catch (Exception exception)
            {
                WarnContractOnce("Friend-lobby menu failed safely: " + DescribeException(exception) + ".");
                if (cancelSelected != null) cancelSelected();
            }
        }

        private void OpenSubmenu()
        {
            if (submenuOpen || installedMenu == null) return;
            try
            {
                ReplaceWithSubmenu(installedMenu);
                if (selected != null && !selected())
                {
                    TryRestoreMainMenuWithOnlineFocus();
                    return;
                }
                submenuOpen = true;
                log.LogInfo("Native Online submenu opened.");
            }
            catch (Exception exception)
            {
                WarnContractOnce("Native Online submenu failed safely: " + DescribeException(exception) + ".");
                TryRestoreMainMenuWithOnlineFocus();
            }
        }

        private void OnHostSelected()
        {
            if (!submenuOpen) return;
            if (hostSelected != null) hostSelected();
        }

        private void OnJoinSelected()
        {
            if (!submenuOpen) return;
            if (joinSelected != null) joinSelected();
        }

        private void OnInviteSelected()
        {
            if (!submenuOpen) return;
            if (inviteSelected != null) inviteSelected();
        }

        private void OnCancelSelected()
        {
            if (!submenuOpen) return;
            if (cancelSelected != null) cancelSelected();
        }

        private void OnRefreshSelected()
        {
            if (!submenuOpen) return;
            if (refreshSelected != null) refreshSelected();
        }

        private void OnFriendSelected(int index)
        {
            if (!submenuOpen) return;
            if (friendSelected != null) friendSelected(index);
        }

        private void CloseSubmenu()
        {
            if (!submenuOpen) return;
            TryRestoreMainMenuWithOnlineFocus();
            if (!submenuOpen && backSelected != null) backSelected();
        }

        private void ReplaceWithSubmenu(object menu)
        {
            IList data = ReadRequiredField(menu.GetType(), menu, "m_itemsData") as IList;
            GameObject owner = ReadRequiredField(menu.GetType(), menu, "m_owner") as GameObject;
            if (data == null || data.Count < 2 || owner == null)
                throw new InvalidOperationException("Main-menu template is unavailable");

            RemoveAllRenderedItems(menu);
            InsertRenderedItem(menu, 0, data[0], simulationMode ? "START SIMULATION" : "HOST GAME",
                "MsgCrawlOnlineHost", owner);
            if (!simulationMode)
                InsertRenderedItem(menu, 1, data[1], "JOIN FRIEND", "MsgCrawlOnlineJoin", owner);
            int backIndex = simulationMode ? 1 : 2;
            InsertRenderedItem(menu, backIndex, data[1], "BACK", "MsgCrawlOnlineBack", owner);
            SetMenuActive(menu);
            SetSelectedItemAndObserve(menu, 0, "open-submenu");

            IList items = ReadRequiredField(menu.GetType(), menu, "m_items") as IList;
            string[] expected = simulationMode
                ? new[] { "MsgCrawlOnlineHost", "MsgCrawlOnlineBack" }
                : new[] { "MsgCrawlOnlineHost", "MsgCrawlOnlineJoin", "MsgCrawlOnlineBack" };
            if (items == null || !MessagesEqual(ReadMessages(items), expected))
                throw new InvalidOperationException("Online submenu invariant failed");
        }

        private void RestoreMainMenu(object menu, bool includeOnline)
        {
            IList data = ReadRequiredField(menu.GetType(), menu, "m_itemsData") as IList;
            GameObject owner = ReadRequiredField(menu.GetType(), menu, "m_owner") as GameObject;
            if (data == null || data.Count == 0 || owner == null)
                throw new InvalidOperationException("Main-menu template is unavailable");

            RemoveAllRenderedItems(menu);
            for (int i = 0; i < data.Count; i++) InsertRenderedItem(menu, i, data[i], null, null, owner);
            if (includeOnline)
                InsertRenderedItem(menu, 1, data[1], "ONLINE", NativeMenuContract.OnlineMessage, owner);
            SetMenuActive(menu);
            SetSelectedItemAndObserve(menu, includeOnline ? 1 : 0,
                includeOnline ? "restore-online" : "restore-original");
        }

        private void TryRestoreMainMenuWithOnlineFocus()
        {
            try
            {
                RestoreMainMenu(installedMenu, true);
                submenuOpen = false;
                log.LogInfo("Native Online submenu closed; focus restored to ONLINE.");
            }
            catch (Exception exception)
            {
                log.LogWarning("Could not restore native main menu: " + DescribeException(exception) + ".");
            }
        }

        private void TryRestoreOriginalMenu()
        {
            try
            {
                RestoreMainMenu(installedMenu, false);
            }
            catch (Exception exception)
            {
                log.LogWarning("Could not restore original main menu during shutdown: " +
                    DescribeException(exception) + ".");
            }
        }

        private static void RemoveAllRenderedItems(object menu)
        {
            IList items = ReadRequiredField(menu.GetType(), menu, "m_items") as IList;
            if (items == null) throw new InvalidOperationException("Rendered menu items are unavailable");
            for (int index = items.Count - 1; index >= 0; index--) TryRemoveItem(menu, index);
        }

        private static void InsertRenderedItem(object menu, int index, object template, string text,
            string message, GameObject owner)
        {
            object itemData = CloneItemData(template);
            if (text != null) WriteRequiredField(itemData.GetType(), itemData, "m_text", text);
            if (message != null) WriteRequiredField(itemData.GetType(), itemData, "m_message", message);
            WriteRequiredField(itemData.GetType(), itemData, "m_enabled", true);
            WriteRequiredField(itemData.GetType(), itemData, "m_visible", true);

            MethodInfo insert = AccessTools.Method(menu.GetType(), "InsertItem",
                new[] { typeof(int), itemData.GetType() });
            if (insert == null) throw new MissingMethodException(menu.GetType().FullName, "InsertItem");
            InvokeReflected(insert, menu, new[] { (object)index, itemData }, "InsertItem");

            IList items = ReadRequiredField(menu.GetType(), menu, "m_items") as IList;
            if (items == null || index >= items.Count) throw new InvalidOperationException("Inserted item is absent");
            object inserted = items[index];
            WriteRequiredField(inserted.GetType(), inserted, "m_messageObject", owner);
        }

        private static void SetSelectedItem(object menu, int index)
        {
            MethodInfo method = menu.GetType().GetMethod("SetSelectedItem",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null);
            if (method == null) throw new MissingMethodException(menu.GetType().FullName, "SetSelectedItem(Int32)");
            InvokeReflected(method, menu, new object[] { index }, "SetSelectedItem");
        }

        private void SetSelectedItemAndObserve(object menu, int index, string transition)
        {
            if (bridge != null)
                bridge.ObserveFocus(transition, delegate { return DescribeFocus(menu, index); });
            SetSelectedItem(menu, index);
            SynchronizeVisualSelection(menu, index);
        }

        private static void SynchronizeVisualSelection(object menu, int selectedIndex)
        {
            IList items = ReadRequiredField(menu.GetType(), menu, "m_items") as IList;
            if (items == null) throw new InvalidOperationException("Rendered menu items are unavailable");
            NativeMenuSelectionPlan plan = NativeMenuSelectionPlan.Create(items.Count, selectedIndex);
            for (int index = 0; index < plan.Count; index++)
            {
                object item = items[index];
                MethodInfo method = item == null ? null : AccessTools.Method(item.GetType(),
                    plan.IsSelected(index) ? "Select" : "Deselect", Type.EmptyTypes);
                if (method == null)
                    throw new MissingMethodException(item == null ? "MenuTextMenuItem" :
                        item.GetType().FullName, plan.IsSelected(index) ? "Select()" : "Deselect()");
                InvokeReflected(method, item, null, plan.IsSelected(index) ? "Select" : "Deselect");
            }
        }

        private static string DescribeFocus(object menu, int requestedIndex)
        {
            Type type = menu.GetType();
            IList items = ReadRequiredField(type, menu, "m_items") as IList;
            object selected = ReadRequiredField(type, menu, "m_selectedItem");
            object active = ReadRequiredField(type, menu, "m_active");
            object initialised = ReadRequiredField(type, menu, "m_initialised");
            object scroll = ReadRequiredField(type, menu, "m_scrollOffset");
            string message = "<none>";
            string itemState = "<none>";
            if (items != null && selected is int && (int)selected >= 0 && (int)selected < items.Count)
            {
                object item = items[(int)selected];
                message = ReadRequiredField(item.GetType(), item, "m_message") as string ?? "<null>";
                itemState = DescribeItemState(item);
            }
            return "requested=" + requestedIndex + " selected=" + selected + " active=" + active +
                " initialised=" + initialised + " scroll=" + scroll + " count=" +
                (items == null ? 0 : items.Count) + " message=" + message + " item=" + itemState;
        }

        private static string DescribeItemState(object item)
        {
            Type type = item.GetType();
            return "actioning=" + ReadOptionalField(type, item, "m_actioning") +
                ",actionWait=" + ReadOptionalField(type, item, "m_actionWaitTime") +
                ",controller=" + ReadOptionalField(type, item, "m_actionedControllerId");
        }

        private static object ReadOptionalField(Type type, object instance, string name)
        {
            FieldInfo field = AccessTools.Field(type, name);
            return field == null ? "<absent>" : field.GetValue(instance);
        }

        private static void SetMenuActive(object menu)
        {
            WriteRequiredField(menu.GetType(), menu, "m_active", true);
        }

        private static bool MessagesEqual(string[] actual, string[] expected)
        {
            if (actual == null || expected == null || actual.Length != expected.Length) return false;
            for (int i = 0; i < actual.Length; i++)
                if (!string.Equals(actual[i], expected[i], StringComparison.Ordinal)) return false;
            return true;
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
