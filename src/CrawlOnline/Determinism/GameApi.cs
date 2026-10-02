using System;
using System.Reflection;
using HarmonyLib;

namespace CrawlOnline.Determinism
{
    internal static class GameApi
    {
        public static Type Type(string name)
        {
            Type type = AccessTools.TypeByName(name);
            if (type == null)
            {
                throw new TypeLoadException("Crawl type not found: " + name);
            }
            return type;
        }

        public static object[] GetPlayers()
        {
            object value = InvokeStatic("SystemPlayers", "GetPlayers");
            Array array = value as Array;
            if (array == null)
            {
                return new object[0];
            }

            object[] players = new object[array.Length];
            for (int i = 0; i < array.Length; i++)
            {
                players[i] = array.GetValue(i);
            }
            return players;
        }

        public static object Invoke(object instance, string method)
        {
            return AccessTools.Method(instance.GetType(), method).Invoke(instance, null);
        }

        public static T Invoke<T>(object instance, string method)
        {
            return (T)Invoke(instance, method);
        }

        public static object InvokeWithArgument(object instance, string method, object argument)
        {
            return AccessTools.Method(instance.GetType(), method).Invoke(instance, new[] { argument });
        }

        public static object InvokeStatic(string type, string method)
        {
            return AccessTools.Method(Type(type), method).Invoke(null, null);
        }

        public static T InvokeStatic<T>(string type, string method)
        {
            return (T)InvokeStatic(type, method);
        }

        public static object InvokeStaticWithArgument(string type, string method, object argument)
        {
            return AccessTools.Method(Type(type), method).Invoke(null, new[] { argument });
        }

        public static T Property<T>(object instance, string property)
        {
            PropertyInfo info = AccessTools.Property(instance.GetType(), property);
            return (T)info.GetValue(instance, null);
        }

        public static object StaticProperty(string type, string property)
        {
            return AccessTools.Property(Type(type), property).GetValue(null, null);
        }

        public static T Field<T>(object instance, string field)
        {
            return (T)AccessTools.Field(instance.GetType(), field).GetValue(instance);
        }
    }
}
