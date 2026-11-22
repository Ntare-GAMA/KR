using UnityEngine;

namespace KR.Core
{
    // Unity objects that are destroyed or missing compare equal to null with ==, but the C# ?? and ?. operators do
    // not see that (in the Editor GetComponent returns a stand-in "missing" object, not null). These helpers make
    // "get or add" and "this or that" safe.
    public static class UnityObjectExtensions
    {
        public static T GetOrAdd<T>(this GameObject go) where T : Component =>
            go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();

        // The object itself if it is alive, otherwise real null (so ?? works after it).
        public static T OrNull<T>(this T obj) where T : Object => obj ? obj : null;
    }
}
