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
// Only what is needed, when it is needed. Startup loads the questionnaires and the
// participant index -- two files -- and a participant's three room files are fetched when
// that participant is chosen.
//
// The first version loaded all ninety-one at startup. Under a megabyte in total, so it
// looked harmless, but each one is a separate request into the APK archive and on a Quest
// that is slow enough that the app sat on "Loading participants" long enough to look
// hung. Ninety of those files belong to participants who will never be run in this
// session.

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
            Instance = this;
            if (Ready) return;
            StartCoroutine(LoadAll());
        }

        IEnumerator LoadAll()
        {
            yield return Load("questionnaires.json");
            yield return Load("participants/index.json");

            string index = Get("participants/index.json");
            if (index != null) participants.AddRange(ParseIds(index));

            Ready = true;
            Debug.Log("ShippedAssets: index lists " + participants.Count +
                      " participants; " +
                      (Get("questionnaires.json") != null ? "questionnaires loaded"
                                                          : "NO questionnaires") +
                      ". Room files load per participant.");
        }

        /// <summary>True once this participant's rooms are in memory.</summary>
        public static bool HasParticipant(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            return cache.ContainsKey(
                "participants/" + ParticipantPacks.PackFor(id) + "/session.json");
        }

        /// <summary>
        /// Fetch one participant's three room files. Safe to call repeatedly: anything
        /// already cached is skipped, so this costs nothing after the first time.
        /// </summary>
        public IEnumerator LoadParticipant(string id)
        {
            if (string.IsNullOrEmpty(id)) yield break;

            string pack = ParticipantPacks.PackFor(id);
            yield return Load("participants/" + pack + "/session.json");
            yield return Load("participants/" + pack + "/oversight.json");
            yield return Load("participants/" + pack + "/rationale.json");
            yield return Load("participants/" + pack + "/practice.json");

            Debug.Log("ShippedAssets: " + id + " runs order " + pack +
                      (HasParticipant(id) ? "." : " -- NOT FOUND."));
        }

        /// <summary>The instance in the scene, for callers that need a coroutine host.</summary>
        public static ShippedAssets Instance { get; private set; }

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
