using System;
using System.Collections;
using System.Collections.Generic;
using KR.Gameplay.Player;
using KR.Gameplay.World;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace KR.Gameplay.Story
{
    // The ambulance's two paramedics and their wheeled stretcher. The paramedics are copies of people from the city
    // (anyone not in uniform and not a story character), body and animation only, hidden until needed. PickUp is the
    // whole scene at the person on the ground: out of the back of the ambulance, lift them on, cover them, back in.
    public sealed class AmbulanceCrew
    {
        readonly Transform _parent;
        readonly Transform[] _medics;
        GameObject _stretcher, _blanket;

        public AmbulanceCrew(Transform parent, int count)
        {
            _parent = parent;
            _medics = Copy(parent, count);
        }

        static Transform[] Copy(Transform parent, int count)
        {
            var pool = new List<PersonAnimator>();
            foreach (var p in Object.FindObjectsByType<PersonAnimator>(FindObjectsSortMode.None))
            {
                string n = p.name;
                if (n.Contains("olice") || n.Contains("fficer") || p.GetComponent<Npc>() != null ||
                    p.GetComponentInParent<RoadNetworkTraffic>() != null || p.GetComponentInParent<Rigidbody>() != null)
                    continue;
                pool.Add(p);
            }
            var list = new List<Transform>();
            for (int i = 0; i < count && pool.Count > 0; i++)
            {
                var template = pool[Random.Range(0, pool.Count)];
                pool.Remove(template);
                var go = Object.Instantiate(template.gameObject, parent);
                go.name = $"Paramedic {i + 1}";
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                    if (!(mb is PersonAnimator))
                        Object.Destroy(mb);
                go.SetActive(false);
                RoadNetworkTraffic.AddObstacle(go.transform, 0.4f);
                list.Add(go.transform);
            }
            if (list.Count == 0)
                Debug.LogWarning("[Emergency] No people in the city to copy as paramedics; the stretcher moves on its own.");
            return list.ToArray();
        }

        // The stretcher comes out of the back of the ambulance, goes to the person on the ground (or `fallback` if
        // they are gone), they are lifted onto it under a blanket and it goes back in; then `done` runs.
        public IEnumerator PickUp(Transform ambulance, Transform victim, Vector3 fallback, PlayerRig rig, Action done)
        {
            BuildStretcher();
            Vector3 rear = GroundAt(ambulance.position - ambulance.forward * 3.2f);
            Vector3 beside = victim != null ? GroundAt(victim.position + (rear - victim.position).normalized * 0.9f) : fallback;
            var face = Quaternion.LookRotation(Flat(beside - rear) + ambulance.forward * 1e-3f);
            _blanket.SetActive(false);
            _stretcher.SetActive(true);
            ShowMedics(true);
            yield return Roll(rear, beside, face, 3f);
            // The crew either side of the person, looking down at them.
            for (int k = 0; k < _medics.Length; k++)
                if (_medics[k] != null)
                {
                    var side = face * Vector3.right * (k == 0 ? -0.9f : 0.9f);
                    var at = GroundAt(beside + side);
                    _medics[k].SetPositionAndRotation(at, Quaternion.LookRotation(Flat(beside - at) + face * Vector3.forward * 1e-3f));
                }
            yield return new WaitForSeconds(3f);
            if (victim != null)
                CarImpacts.Collect(victim);
            _blanket.SetActive(true);
            if (rig != null && (rig.Player.transform.position - fallback).sqrMagnitude < 80f * 80f)
                rig.Toast("The paramedics lift them onto the stretcher and cover them.", 5f);
            yield return new WaitForSeconds(1.5f);
            yield return Roll(beside, rear, face, 3f);
            _stretcher.SetActive(false);
            ShowMedics(false);
            yield return new WaitForSeconds(2f);
            done?.Invoke();
        }

        void ShowMedics(bool on)
        {
            foreach (var m in _medics)
                if (m != null)
                    m.gameObject.SetActive(on);
        }

        IEnumerator Roll(Vector3 from, Vector3 to, Quaternion facing, float seconds)
        {
            var t = _stretcher.transform;
            var dir = Flat(to - from);
            var walk = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : facing;
            for (float k = 0f; k < 1f; k += Time.deltaTime / seconds)
            {
                var p = GroundAt(Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, k)));
                t.SetPositionAndRotation(p, facing);
                // One paramedic at each end of the stretcher, walking with it.
                for (int i = 0; i < _medics.Length; i++)
                    if (_medics[i] != null)
                        _medics[i].SetPositionAndRotation(GroundAt(p + facing * Vector3.forward * (i == 0 ? 1.25f : -1.25f)), walk);
                yield return null;
            }
            t.SetPositionAndRotation(to, facing);
        }

        // A wheeled stretcher: a padded top on a frame and legs, and a blanket shown once someone is on it.
        void BuildStretcher()
        {
            if (_stretcher != null)
                return;
            var root = new GameObject("Ambulance stretcher");
            root.transform.SetParent(_parent, false);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            Material M(Color c)
            {
                var m = new Material(shader);
                m.SetColor("_BaseColor", c);
                return m;
            }
            var metal = M(new Color(0.75f, 0.77f, 0.8f));
            var pad = M(new Color(0.15f, 0.2f, 0.35f));
            var cloth = M(new Color(0.85f, 0.35f, 0.1f));
            void Part(Transform parent, Material m, Vector3 at, Vector3 size, string name = "Part")
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(parent, false);
                go.transform.localPosition = at;
                go.transform.localScale = size;
                go.GetComponent<Renderer>().sharedMaterial = m;
            }
            Part(root.transform, pad, new Vector3(0f, 0.78f, 0f), new Vector3(0.62f, 0.08f, 1.95f));
            Part(root.transform, metal, new Vector3(0f, 0.7f, 0f), new Vector3(0.58f, 0.05f, 1.9f));
            foreach (float x in new[] { -0.26f, 0.26f })
                foreach (float z in new[] { -0.8f, 0.8f })
                {
                    Part(root.transform, metal, new Vector3(x, 0.35f, z), new Vector3(0.04f, 0.7f, 0.04f));
                    Part(root.transform, metal, new Vector3(x, 0.05f, z), new Vector3(0.05f, 0.1f, 0.1f), "Wheel");
                }
            var cover = new GameObject("Blanket");
            cover.transform.SetParent(root.transform, false);
            Part(cover.transform, cloth, new Vector3(0f, 0.92f, -0.1f), new Vector3(0.52f, 0.2f, 1.45f));
            Part(cover.transform, pad, new Vector3(0f, 0.9f, 0.78f), new Vector3(0.4f, 0.14f, 0.3f), "Pillow");
            root.SetActive(false);
            _stretcher = root;
            _blanket = cover;
        }

        static Vector3 GroundAt(Vector3 p) => Floor.At(p, 1.5f, 5f);
        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
