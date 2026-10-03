using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrawlOnline.Diagnostics
{
    // Opt-in runtime observation only. This records type/member shape and visible
    // menu metadata, never assets, method bodies, or decompiled source. Output
    // belongs in disposable logs.
    internal sealed class MenuContractProbe
    {
        private const string EnvironmentVariable = "CRAWL_ONLINE_MENU_PROBE";
        private readonly ManualLogSource log;
        private string observedScene = string.Empty;
        private int framesInScene;
        private bool disabled;

        private MenuContractProbe(ManualLogSource logSource)
        {
            log = logSource;
            log.LogWarning("Menu contract probe enabled; press F4 to capture active menu component contracts.");
        }

        public static MenuContractProbe TryCreate(ManualLogSource logSource)
        {
            return string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "1",
                StringComparison.Ordinal) ? new MenuContractProbe(logSource) : null;
        }

        public void Tick()
        {
            if (disabled) return;
            string scene = SceneManager.GetActiveScene().name ?? string.Empty;
            if (!string.Equals(scene, observedScene, StringComparison.Ordinal))
            {
                observedScene = scene;
                framesInScene = 0;
            }

            framesInScene++;
            if (framesInScene != 180 && !Input.GetKeyDown(KeyCode.F4)) return;
            try
            {
                Capture();
            }
            catch (Exception exception)
            {
                disabled = true;
                log.LogWarning("Menu contract probe disabled after " + exception.GetType().Name + ".");
            }
        }

        private void Capture()
        {
            UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(typeof(MonoBehaviour));
            var contracts = new SortedDictionary<string, Type>(StringComparer.Ordinal);
            for (int i = 0; i < objects.Length; i++)
            {
                var behaviour = objects[i] as MonoBehaviour;
                if (behaviour == null || behaviour.gameObject == null || !behaviour.gameObject.activeInHierarchy)
                    continue;

                Type type = behaviour.GetType();
                if (IsMenuCandidate(type)) AddContract(type, contracts, 0);
            }

            AddNamedContract("MenuTextMenuItem", contracts);
            AddNamedContract("MenuTextMenuItemData", contracts);
            AddNamedContract("MenuState", contracts);

            log.LogInfo("MENU_PROBE scene=" + observedScene + " candidates=" + contracts.Count);
            foreach (KeyValuePair<string, Type> contract in contracts)
            {
                Type type = contract.Value;
                log.LogInfo("MENU_PROBE type=" + type.FullName + " base=" + SafeTypeName(type.BaseType));
                LogFields(type);
                LogMethods(type);
            }
            LogMenuInstances(objects);
            log.LogInfo("MENU_PROBE end");
        }

        private void LogMenuInstances(UnityEngine.Object[] objects)
        {
            int instanceIndex = 0;
            var seen = new HashSet<int>();
            for (int i = 0; i < objects.Length; i++)
            {
                var behaviour = objects[i] as MonoBehaviour;
                if (behaviour == null || behaviour.gameObject == null || !behaviour.gameObject.activeInHierarchy)
                    continue;

                object menu = null;
                Type behaviourType = behaviour.GetType();
                if (behaviourType.FullName == "MenuTextMenu") menu = behaviour;
                if (behaviourType.FullName == "MenuMain")
                    menu = ReadField(behaviourType, behaviour, "m_menuMain");
                if (menu == null) continue;
                var menuObject = menu as UnityEngine.Object;
                if (menuObject != null && !seen.Add(menuObject.GetInstanceID())) continue;

                Type type = menu.GetType();
                IList data = ReadField(type, menu, "m_itemsData") as IList;
                IList items = ReadField(type, menu, "m_items") as IList;
                log.LogInfo("MENU_PROBE instance=" + instanceIndex +
                    " active=" + ReadField(type, menu, "m_active") +
                    " selected=" + ReadField(type, menu, "m_selectedItem") +
                    " spacing=" + ReadField(type, menu, "m_itemSpacing") +
                    " count=" + (data == null ? 0 : data.Count));
                if (data != null)
                {
                    for (int itemIndex = 0; itemIndex < data.Count; itemIndex++)
                    {
                        object itemData = data[itemIndex];
                        Type dataType = itemData.GetType();
                        string position = "<none>";
                        if (items != null && itemIndex < items.Count)
                        {
                            var component = items[itemIndex] as Component;
                            if (component != null) position = component.transform.localPosition.ToString();
                        }
                        log.LogInfo("MENU_PROBE item=" + itemIndex +
                            " text=" + SafeValue(ReadField(dataType, itemData, "m_text")) +
                            " message=" + SafeValue(ReadField(dataType, itemData, "m_message")) +
                            " enabled=" + ReadField(dataType, itemData, "m_enabled") +
                            " visible=" + ReadField(dataType, itemData, "m_visible") +
                            " position=" + position);
                    }
                }
                instanceIndex++;
            }
        }

        private static object ReadField(Type type, object instance, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(instance);
        }

        private static string SafeValue(object value)
        {
            string text = value == null ? "<null>" : value.ToString();
            text = text.Replace('\r', ' ').Replace('\n', ' ');
            return text.Length <= 80 ? text : text.Substring(0, 80);
        }

        private static void AddNamedContract(string name, SortedDictionary<string, Type> contracts)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(name, false);
                if (type == null) continue;
                AddContract(type, contracts, 0);
                return;
            }
        }

        private static void AddContract(Type type, SortedDictionary<string, Type> contracts, int depth)
        {
            if (type == null) return;
            string key = type.FullName ?? type.Name;
            if (contracts.ContainsKey(key)) return;
            contracts.Add(key, type);
            if (depth >= 2) return;

            if (IsGameMenuType(type.BaseType)) AddContract(type.BaseType, contracts, depth + 1);
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int i = 0; i < fields.Length; i++)
            {
                Type fieldType = fields[i].FieldType;
                if (IsGameMenuType(fieldType)) AddContract(fieldType, contracts, depth + 1);
                if (!fieldType.IsGenericType) continue;
                Type[] arguments = fieldType.GetGenericArguments();
                for (int argument = 0; argument < arguments.Length; argument++)
                {
                    if (IsGameMenuType(arguments[argument]))
                        AddContract(arguments[argument], contracts, depth + 1);
                }
            }
        }

        private void LogFields(Type type)
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Array.Sort(fields, delegate(FieldInfo left, FieldInfo right)
            {
                return string.CompareOrdinal(left.Name, right.Name);
            });
            for (int i = 0; i < fields.Length; i++)
            {
                log.LogInfo("MENU_PROBE field=" + fields[i].Name + ":" + SafeTypeName(fields[i].FieldType));
            }
        }

        private void LogMethods(Type type)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Array.Sort(methods, delegate(MethodInfo left, MethodInfo right)
            {
                return string.CompareOrdinal(left.Name, right.Name);
            });
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].IsSpecialName) continue;
                ParameterInfo[] parameters = methods[i].GetParameters();
                string signature = methods[i].Name + "(";
                for (int parameter = 0; parameter < parameters.Length; parameter++)
                {
                    if (parameter > 0) signature += ",";
                    signature += SafeTypeName(parameters[parameter].ParameterType);
                }
                signature += "):" + SafeTypeName(methods[i].ReturnType);
                log.LogInfo("MENU_PROBE method=" + signature);
            }
        }

        private static bool IsMenuCandidate(Type type)
        {
            string name = (type.FullName ?? type.Name).ToLowerInvariant();
            return name.Contains("menu") || name.Contains("button") || name.Contains("select") ||
                name.Contains("title") || name.Contains("option");
        }

        private static bool IsGameMenuType(Type type)
        {
            return type != null && type.Assembly.GetName().Name == "Assembly-CSharp" && IsMenuCandidate(type);
        }

        private static string SafeTypeName(Type type)
        {
            return type == null ? "<none>" : (type.FullName ?? type.Name);
        }
    }
}
