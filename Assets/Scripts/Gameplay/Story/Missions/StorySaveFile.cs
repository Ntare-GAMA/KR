using System.IO;
using UnityEngine;

namespace KR.Gameplay.Story
{
    // The Story Mode save read from outside the Story scene (the main menu): progress for "Continue", and the
    // comfort settings, written back the same way StoryDirector writes (temp file, then replace; old copy kept).
    public static class StorySaveFile
    {
        public static string Path => System.IO.Path.Combine(Application.persistentDataPath, "story_save.json");

        public static bool Exists => File.Exists(Path);

        public static StorySave Load()
        {
            try
            {
                if (File.Exists(Path))
                    return JsonUtility.FromJson<StorySave>(File.ReadAllText(Path)) ?? new StorySave();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Menu] Save unreadable: {e.Message}");
            }
            return new StorySave();
        }

        public static void Write(StorySave save)
        {
            try
            {
                string tmp = Path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(save, true));
                if (File.Exists(Path))
                    File.Replace(tmp, Path, Path + ".bak");
                else
                    File.Move(tmp, Path);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Menu] Save failed: {e.Message}");
            }
        }
    }
}
