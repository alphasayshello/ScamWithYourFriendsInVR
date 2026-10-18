using System;
using System.Collections.Generic;

namespace ScamWithYourFriendsInVR
{
    internal static class ReflectionTypes
    {
        private static readonly Dictionary<string, Type> cache = new Dictionary<string, Type>();
        internal static Type Find(string name)
        {
            if (cache.TryGetValue(name, out Type found)) return found;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = assembly.GetType(name, false);
                    if (type != null) { cache[name] = type; return type; }
                }
                catch (TypeLoadException) { }
            }
            return null;
        }
    }
}
