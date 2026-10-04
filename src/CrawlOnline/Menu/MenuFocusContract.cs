using System;
using System.Collections.Generic;
using System.Reflection;

namespace CrawlOnline.Menu
{
    internal static class MenuFocusContract
    {
        private const int MaximumMembers = 48;

        public static string Describe(Type type)
        {
            var members = new List<string>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            FieldInfo[] fields = type.GetFields(flags);
            for (int index = 0; index < fields.Length; index++)
                if (IsRelevant(fields[index].Name))
                    members.Add("field=" + fields[index].Name + ":" + TypeName(fields[index].FieldType));

            PropertyInfo[] properties = type.GetProperties(flags);
            for (int index = 0; index < properties.Length; index++)
                if (IsRelevant(properties[index].Name))
                    members.Add("property=" + properties[index].Name + ":" + TypeName(properties[index].PropertyType));

            MethodInfo[] methods = type.GetMethods(flags);
            for (int index = 0; index < methods.Length; index++)
                if (!methods[index].IsSpecialName && IsRelevant(methods[index].Name))
                    members.Add(DescribeMethod(methods[index]));

            members.Sort(StringComparer.Ordinal);
            if (members.Count > MaximumMembers) members.RemoveRange(MaximumMembers, members.Count - MaximumMembers);
            return "type=" + TypeName(type) + " members=" + string.Join("|", members.ToArray());
        }

        private static string DescribeMethod(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            var signature = "method=" + method.Name + "(";
            for (int index = 0; index < parameters.Length; index++)
            {
                if (index > 0) signature += ",";
                signature += TypeName(parameters[index].ParameterType);
            }
            return signature + "):" + TypeName(method.ReturnType);
        }

        private static bool IsRelevant(string name)
        {
            string value = name.ToLowerInvariant();
            return value.Contains("select") || value.Contains("colour") || value.Contains("color") ||
                value.Contains("highlight") || value.Contains("animation") || value.Contains("state") ||
                value.Contains("controller") || value.Contains("update") || value.Contains("action");
        }

        private static string TypeName(Type type)
        {
            return type == null ? "<none>" : (type.FullName ?? type.Name);
        }
    }
}
