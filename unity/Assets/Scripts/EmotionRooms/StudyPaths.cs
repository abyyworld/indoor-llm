// Where the study writes its data.
//
// One place, because eleven files were each calling Application.persistentDataPath and
// that answer is wrong in the editor. On a headset it is the app's private folder and
// there is no alternative: an Android app cannot write to a folder on the researcher's
// laptop, so that data has to be pulled across, and the panel does that automatically
// when a session ends.
//
// In the editor there is no such barrier. persistentDataPath there is
// ~/Library/Application Support/DefaultCompany/unity, which is nowhere near the project,
// is easy to forget about, and is the reason a mouse-driven pilot looked like it had
// saved nothing. Editor runs write into runs/local-data inside the repo instead, next to
// everything else the study produces.
//
// runs/ is gitignored, so participant data still never reaches GitHub.

using System.IO;
using UnityEngine;

namespace EmotionRooms
{
    public static class StudyPaths
    {
        /// <summary>The folder every log, response file and bundle is written under.</summary>
        public static string Data
        {
            get
            {
                if (!Application.isEditor) return Application.persistentDataPath;

                // <repo>/unity/Assets -> <repo>/unity -> <repo>
                var project = Directory.GetParent(Application.dataPath);
                var repo = project != null ? project.Parent : null;
                if (repo == null) return Application.persistentDataPath;

                string local = Path.Combine(repo.FullName, "runs", "local-data");
                try
                {
                    Directory.CreateDirectory(local);
                    return local;
                }
                catch (System.Exception)
                {
                    // A read-only checkout is not worth failing a session over.
                    return Application.persistentDataPath;
                }
            }
        }
    }
}
