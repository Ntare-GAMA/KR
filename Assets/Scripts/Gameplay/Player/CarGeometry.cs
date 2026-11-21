using UnityEngine;

namespace KR.Gameplay.Player
{
    // Measurements of the player's car (Resources/Car/car_parts.json, written in the Editor by
    // KR.Editor.Batch.CarParts.Export because the car mesh is not readable in a build). Car-local space, metres.
    public static class CarGeometry
    {
        [System.Serializable]
        public sealed class Mirror
        {
            public Vector3 centre, normal, right, up;
            public float width, height;
        }

        [System.Serializable]
        public sealed class Box
        {
            public Vector3 centre, size;
        }

        [System.Serializable]
        sealed class Data
        {
            public Mirror[] mirrors;
            public Box cluster;
        }

        static Data _data;
        static bool _loaded;

        public static Mirror[] Mirrors => Load()?.mirrors ?? System.Array.Empty<Mirror>();
        // The instrument cluster behind the steering wheel; null if it was not measured.
        public static Box Cluster
        {
            get
            {
                var c = Load()?.cluster;
                return c != null && c.size.sqrMagnitude > 0f ? c : null;
            }
        }

        static Data Load()
        {
            if (_loaded)
                return _data;
            _loaded = true;
            var text = Resources.Load<TextAsset>("Car/car_parts");
            if (text != null)
                _data = JsonUtility.FromJson<Data>(text.text);
            return _data;
        }
    }
}
