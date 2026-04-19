/*
SPDX-License-Identifier: GPL-3.0-or-later
Copyright (c) 2025 RussDev7, unknowghost0
This file is part of https://github.com/RussDev7/CMZDedicatedServers - see LICENSE for details.
*/

using System.Reflection;
using System.Linq;
using System;

namespace CMZDedicatedSteamServer.Common
{
    internal static class ReflectEx
    {
        public static Type GetRequiredType(Assembly asm, string fullName)
        {
            Type t = asm?.GetType(fullName, throwOnError: false, ignoreCase: false);
            return t ?? throw new InvalidOperationException("Required type not found: " + fullName);
        }

        public static PropertyInfo GetRequiredProperty(Type type, string name)
        {
            PropertyInfo p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            return p ?? throw new InvalidOperationException($"Required property '{name}' not found on {type.FullName}.");
        }

        public static FieldInfo GetRequiredField(Type type, string name)
        {
            FieldInfo f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            return f ?? throw new InvalidOperationException($"Required field '{name}' not found on {type.FullName}.");
        }

        public static MethodInfo GetRequiredMethod(Type type, string name, int parameterCount = -1)
        {
            MethodInfo[] methods = [.. type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Where(m => m.Name == name)];

            MethodInfo match = parameterCount >= 0
                ? methods.FirstOrDefault(m => m.GetParameters().Length == parameterCount)
                : methods.FirstOrDefault();

            return match ?? throw new InvalidOperationException($"Required method '{name}' not found on {type.FullName}.");
        }

        public static MethodInfo GetRequiredMethod(Type type, string name, params Type[] parameterTypes)
        {
            MethodInfo[] methods = [.. type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Where(m => m.Name == name)];

            foreach (MethodInfo method in methods)
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != parameterTypes.Length)
                    continue;

                bool matched = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type expected = parameters[i].ParameterType;
                    Type actual = parameterTypes[i];
                    if (expected == actual)
                        continue;

                    if (actual != null && expected.IsAssignableFrom(actual))
                        continue;

                    matched = false;
                    break;
                }

                if (matched)
                    return method;
            }

            throw new InvalidOperationException($"Required method '{name}' with the expected signature was not found on {type.FullName}.");
        }

        public static object GetRequiredMemberValue(object instance, string memberName)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            Type type = instance.GetType();
            PropertyInfo p = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (p != null)
                return p.GetValue(instance, null);

            FieldInfo f = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            return f != null
                ? f.GetValue(instance)
                : throw new InvalidOperationException($"Required member '{memberName}' not found on {type.FullName}.");
        }

        public static void SetRequiredMemberValue(object instance, string memberName, object value)
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            Type type = instance.GetType();
            PropertyInfo p = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (p != null)
            {
                p.SetValue(instance, value, null);
                return;
            }

            FieldInfo f = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (f != null)
            {
                f.SetValue(instance, value);
                return;
            }

            throw new InvalidOperationException($"Required member '{memberName}' not found on {type.FullName}.");
        }
    }
}
