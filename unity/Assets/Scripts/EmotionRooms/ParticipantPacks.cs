// Finds a participant's stimuli, wherever they are.
//
// Two places, in order:
//
//   StreamingAssets/participants/<id>/   shipped inside the app
//   persistentDataPath/                  written by test-participant.sh
//
// The shipped copy is what makes the study runnable by someone who does not have this
// repo, Python, or Unity. A built app carries the rooms for the whole sample and the
// researcher picks a number; the data folder is still checked first so a machine with
// the pipeline on it can override a single participant without rebuilding.

using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EmotionRooms
{
    public static class ParticipantPacks
    {
        public static string ShippedRoot
        {
            get { return Path.Combine(Application.streamingAssetsPath, "participants"); }
        }

        /// <summary>Participant ids the app ships stimuli for.</summary>
        public static List<string> Available()
        {
            // From the loaded index rather than a directory listing: StreamingAssets
            // cannot be enumerated inside an APK.
            var ids = new List<string>(ShippedAssets.Participants);
            ids.Sort(string.CompareOrdinal);
            return ids;
        }

        /// <summary>
        /// Where to read `fileName` for this participant, or null if there is nowhere.
        ///
        /// The loose file in the data folder wins. Someone who has just regenerated one
        /// participant expects to be running that, and silently preferring the shipped
        /// copy would hand them a session they thought they had replaced.
        /// </summary>
        public static string Read(string participant, string fileName)
        {
            // The loose file in the data folder wins. Someone who has just regenerated
            // one participant expects to be running that, and silently preferring the
            // shipped copy would hand them a session they thought they had replaced.
            string loose = Path.Combine(Application.persistentDataPath, fileName);
            if (File.Exists(loose)) return File.ReadAllText(loose);

            if (string.IsNullOrEmpty(participant)) return null;
            return ShippedAssets.Get("participants/" + participant + "/" + fileName);
        }

        public static bool Has(string participant)
        {
            return Read(participant, "session.json") != null;
        }
    }
}
