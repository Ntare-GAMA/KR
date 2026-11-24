using KR.Core;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // Connects events between Story Mode systems at start (kept out of the systems themselves so each stays
    // independent): a photo taken goes to the story (gallery, badge, photo missions).
    public sealed class StoryWiring : MonoBehaviour
    {
        void Start()
        {
            var director = GetComponent<StoryDirector>().OrNull() ?? FindFirstObjectByType<StoryDirector>();
            var camera = GetComponent<PhotoCamera>().OrNull() ?? FindFirstObjectByType<PhotoCamera>();
            if (director != null && camera != null)
                camera.Taken += director.OnPhotoTaken;
        }
    }
}
