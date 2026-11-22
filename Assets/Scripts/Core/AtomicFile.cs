using System.IO;

namespace KR.Core
{
    // Writes a file so a crash or power loss mid-write never leaves it half written: the text goes to path.tmp,
    // which then replaces path, and the previous good copy is kept as path.bak.
    public static class AtomicFile
    {
        public static void Write(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path))
                File.Replace(tmp, path, path + ".bak");
            else
                File.Move(tmp, path);
        }
    }
}
