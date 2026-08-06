// Reads the files that ship inside the app, on every platform.
//
// On desktop, StreamingAssets is a folder and File.ReadAllText works. On Android it is a
// compressed entry inside the APK: File.Exists returns false and File.ReadAllText throws,
// so a standalone Quest build would load no rooms and no questionnaires at all, and would
// look like an app that simply does nothing. UnityWebRequest reads both.
//
// It also cannot be enumerated inside an APK, which is why build-participants writes an
// index.json listing the participants -- a directory listing is a desktop luxury.
//
// Everything is pulled into memory once at startup. It is under a megabyte for the whole
// sample, and a synchronous read is worth avoiding at the moment a session begins.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace EmotionRooms
{
    public class ShippedAssets : MonoBehaviour
    {
        public static bool Ready { get; private set; }
        public static string Error { get; private set; }

        static readonly Dictionary<string, string> cache = new Dictionary<string, string>();
        static readonly List<string> participants = new List<string>();

        /// <summary>Participant ids the app ships stimuli for.</summary>
        public static IList<string> Participants { get { return participants; } }

        /// <summary>Contents of a shipped file, or null. Path is relative to StreamingAssets.</summary>
        public static string Get(string relativePath)
        {
            string text;
            return cache.TryGetValue(relativePath, out text) ? text : null;
        }

        void Awake()
        {
            if (Ready) return;
            StartCoroutine(LoadAll());
        }

        IEnumerator LoadAll()
        {
            yield return Load("questionnaires.json");

            yield return Load("participants/index.json");
            string index = Get("participants/index.json");

            if (index != null)
            {
                var listed = ParseIds(index);
                foreach (var id in listed)
                {
                    yield return Load("participants/" + id + "/session.json");
                    yield return Load("participants/" + id + "/oversight.json");
                    yield return Load("participants/" + id + "/practice.json");

                    if (Get("participants/" + id + "/session.json") != null)
                        participants.Add(id);
                }
            }

            Ready = true;
            Debug.Log("ShippedAssets: " + participants.Count + " participants and " +
                      (Get("questionnaires.json") != null ? "the questionnaires" : "NO questionnaires") +
                      " loaded from inside the app.");
        }

        IEnumerator Load(string relativePath)
        {
            if (cache.ContainsKey(relativePath)) yield break;

            string full = Path.Combine(Application.streamingAssetsPath, relativePath);

            // A plain path on desktop; a URL everywhere. UnityWebRequest handles both, and
            // handles the APK case that File cannot.
            if (!full.Contains("://")) full = "file://" + full;

            using (var request = UnityWebRequest.Get(full))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                    cache[relativePath] = request.downloadHandler.text;
                else if (relativePath == "questionnaires.json" ||
                         relativePath == "participants/index.json")
                    Error = relativePath + ": " + request.error;
            }
        }

        /// <summary>Pull the ids out of index.json without a JSON library.
        ///
        /// JsonUtility cannot deserialise a bare string array at the top level, and the
        /// file is ours and one line deep, so scanning for quoted values is enough and
        /// avoids a dependency for one field.</summary>
        static List<string> ParseIds(string json)
        {
            var ids = new List<string>();
            int at = json.IndexOf("participants");
            if (at < 0) return ids;

            bool inString = false;
            var current = new System.Text.StringBuilder();
            for (int i = json.IndexOf('[', at) + 1; i > 0 && i < json.Length; i++)
            {
                char c = json[i];
                if (c == ']') break;
                if (c == '"')
                {
                    if (inString && current.Length > 0) ids.Add(current.ToString());
                    current.Length = 0;
                    inString = !inString;
                }
                else if (inString) current.Append(c);
            }
            return ids;
        }
    }
}
