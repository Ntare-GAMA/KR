using System.Collections;
using KR.Gameplay.Player;
using KR.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KR.Gameplay.Story
{
    // Where the story begins, from the main-menu tile you chose (owner 2026-11-23: "if i choose to play at the school
    // through alu campus, i should arrive there ... that's where i should start from").
    //   ALU Campus: on foot by the ALU car park (the campus gate), your car parked beside you.
    //   Kibagabaga: on the street outside Maman Fils's house, car beside you.
    //   Kimironko, Story Mode, Your Car: at home beside the car, as before.
    // The chosen place also becomes the session's start spot (the first milestone and an arrest bring you back there).
    public sealed class StoryStart : MonoBehaviour
    {
        // Set by the main menu before the city loads; read once.
        public static string Area { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (string.IsNullOrEmpty(Area) || Area == "story" || Area == "kimironko" || Area == "car")
                return;
            if (Object.FindFirstObjectByType<PlayerRig>() == null || Object.FindFirstObjectByType<StoryStart>() != null)
                return;
            new GameObject("Story start").AddComponent<StoryStart>();
        }

        IEnumerator Start()
        {
            string area = Area;
            Area = null;
            // After the rig has put you beside the car at home (its Start), and the city has settled.
            yield return null;
            yield return null;
            var rig = FindFirstObjectByType<PlayerRig>();
            var director = FindFirstObjectByType<StoryDirector>();
            if (rig == null)
                yield break;
            Vector3? target = null;
            string name = null;
            if (area == "alu" && director != null && director.Week != null)
                foreach (var m in director.Week.milestones)
                    if (m != null && m.placeName != null && m.placeName.Contains("ALU"))
                    {
                        target = m.position;
                        name = "the ALU campus";
                        break;
                    }
            if (area == "kibagabaga")
            {
                var disc = FindFirstObjectByType<Discoveries>();
                if (disc != null && disc.Catalog != null)
                    foreach (var p in disc.Catalog.places)
                        if (p != null && p.id == "maman_fils")
                        {
                            target = p.position;
                            name = "Kibagabaga";
                            break;
                        }
            }
            if (!target.HasValue)
            {
                Debug.LogWarning($"[Start] No starting place found for '{area}'; starting at home.");
                yield break;
            }
            // On the nearest street, so you are outside (not in a building) with room for the car.
            var spot = target.Value;
            var nav = FindFirstObjectByType<Navigator>();
            int node = nav != null && nav.Graph != null && nav.Graph.Count > 0 ? nav.Graph.Nearest(spot) : -1;
            if (node >= 0)
                spot = nav.Graph.Position(node);
            var look = target.Value - spot;
            look.y = 0f;
            float yaw = look.sqrMagnitude > 1f ? Quaternion.LookRotation(look).eulerAngles.y : 0f;
            spot = Floor.At(spot, 3f, 6f);
            VrRig.Fade(0.5f);
            rig.SetStart(spot, yaw);
            rig.TakeAway(spot, yaw);   // on foot there, with the car parked beside you
            if (rig.Car != null && (rig.Car.transform.position - spot).sqrMagnitude > 30f * 30f)
                rig.ParkCarNear(spot, yaw);
            rig.Toast($"You start at {name}. Your car is parked beside you.", 5f);
            Debug.Log($"[Start] Session starts at {name} {spot:F0}.");
        }
    }
}
