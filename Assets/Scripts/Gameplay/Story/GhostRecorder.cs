using System;
using System.Collections.Generic;
using System.IO;
using KR.Core;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // A recorded run: the player's position every interval seconds.
    [Serializable]
    public sealed class GhostTrack
    {
        public string milestone;
        public float seconds;
        public float interval = 0.25f;
        public Vector3[] points;

        public Vector3 At(float t)
        {
            if (points == null || points.Length == 0)
                return Vector3.zero;
            float f = t / interval;
            int i = Mathf.Clamp(Mathf.FloorToInt(f), 0, points.Length - 1);
            int j = Mathf.Min(i + 1, points.Length - 1);
            return Vector3.Lerp(points[i], points[j], f - i);
        }
    }

    // Records the current milestone run and replays your best one as a glowing ghost beside you (later: other
    // players' runs from the leaderboard). One JSON file per milestone in persistentDataPath/ghosts.
    public sealed class GhostRecorder
    {
        const float Interval = 0.25f;

        readonly List<Vector3> _track = new List<Vector3>();
        readonly string _folder;
        float _timer;
        Transform _marker;

        public GhostTrack Best { get; private set; }

        public GhostRecorder(string folder) => _folder = folder;

        string PathOf(string id) => Path.Combine(_folder, id + ".json");

        // A new run of milestone id: clear the recording and load the best run to race against.
        public void Begin(string id)
        {
            _track.Clear();
            _timer = 0f;
            Best = Load(id);
        }

        public void Tick(float dt, Vector3 player, float elapsed, bool showGhost)
        {
            _timer -= dt;
            if (_timer > 0f)
                return;
            _timer = Interval;
            _track.Add(player);
            if (_marker != null && Best != null && showGhost)
                _marker.position = Best.At(elapsed) + Vector3.up;
        }

        // This run was the best: keep it.
        public void SaveBest(string id, float seconds)
        {
            try
            {
                Directory.CreateDirectory(_folder);
                var g = new GhostTrack { milestone = id, seconds = seconds, interval = Interval, points = _track.ToArray() };
                AtomicFile.Write(PathOf(id), JsonUtility.ToJson(g));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Story] Ghost not saved: {e.Message}");
            }
        }

        GhostTrack Load(string id)
        {
            try
            {
                if (File.Exists(PathOf(id)))
                {
                    var g = JsonUtility.FromJson<GhostTrack>(File.ReadAllText(PathOf(id)));
                    EnsureMarker();
                    return g;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Story] Ghost not loaded: {e.Message}");
            }
            return null;
        }

        void EnsureMarker()
        {
            if (_marker != null)
                return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Ghost (your best run)";
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", new Color(0.3f, 0.9f, 1f));
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _marker = go.transform;
        }
    }
}
