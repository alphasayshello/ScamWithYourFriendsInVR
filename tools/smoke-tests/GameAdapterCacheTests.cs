using System;
using System.Reflection;

namespace ScamWithYourFriendsInVR.SmokeTests
{
    public sealed partial class PointerSmokeTests
    {
        private class CacheProbe
        {
            public int Field = 4;
            public int Property { get; set; } = 5;
            public int Add(int value, int extra = 3) => value + extra;
        }
        private sealed class DerivedCacheProbe : CacheProbe { }
        private void TestGameAdapterCache()
        {
            Type adapter = typeof(VRManager).Assembly.GetType("ScamWithYourFriendsInVR.Patches.GameAdapter");
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var read = adapter.GetMethod("Read", flags);
            var write = adapter.GetMethod("Write", flags);
            var call = adapter.GetMethod("Call", flags);
            var probe = new CacheProbe(); var derived = new DerivedCacheProbe();
            Check((int)read.Invoke(null, new object[] { probe, "Field" }) == 4, "Cached field read changed its value");
            write.Invoke(null, new object[] { probe, "Field", 9 });
            write.Invoke(null, new object[] { derived, "Property", 8 });
            Check(probe.Field == 9 && derived.Property == 8 && (int)read.Invoke(null, new object[] { derived, "Field" }) == 4,
                "Member cache mixed owner types, properties or inherited fields");
            Check(read.Invoke(null, new object[] { probe, "Add" }) == null, "Reading a method as data returned a value");
            Check((int)call.Invoke(null, new object[] { probe, "Add", new object[] { 4 } }) == 7,
                "Data lookup poisoned the method cache, or optional arguments changed");
            Check((int)call.Invoke(null, new object[] { derived, "Add", new object[] { 4, 5 } }) == 9,
                "Inherited method argument dispatch changed");
            Check(read.Invoke(null, new object[] { probe, "Missing" }) == null, "Missing member handling changed");
        }
    }
}
