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
        /// <summary>
        /// Which shipped pack a typed id should use.
        ///
        /// The id is the researcher's to choose -- "09", "p09", "pilot2", a name -- and it
        /// is what every output file is named after. The packs, meanwhile, are thirty
        /// pre-built counterbalancing orders. Tying the two together meant an id outside
        /// p01..p30 had no rooms and the session did nothing, which is a restriction the
        /// study never needed.
        ///
        /// So they are resolved rather than required to match:
        ///   an exact pack name wins            p07  -> p07
        ///   otherwise any digits in the id     09   -> p09,  pilot2 -> p02
        ///   otherwise a stable hash of it      bob  -> one of the thirty
        ///
        /// Counterbalancing survives because thirty distinct orders remain in play, and
        /// the resolved pack is written to the event log so the analysis knows which
        /// order a participant actually saw.
        /// </summary>
        public static string PackFor(string participant)
        {
            var available = ShippedAssets.Participants;
            if (available == null || available.Count == 0) return participant;
            if (string.IsNullOrEmpty(participant)) return available[0];

            for (int i = 0; i < available.Count; i++)
                if (available[i] == participant) return participant;

            var digits = new System.Text.StringBuilder();
            foreach (char c in participant) if (char.IsDigit(c)) digits.Append(c);

            int index;
            if (digits.Length > 0 && int.TryParse(digits.ToString(), out index))
            {
                // 1-based, because a researcher typing "09" means the ninth, not the tenth.
                index = (index - 1) % available.Count;
                if (index < 0) index += available.Count;
            }
            else
            {
                int hash = 17;
                foreach (char c in participant) hash = hash * 31 + c;
                index = (hash & 0x7fffffff) % available.Count;
            }
            return available[index];
        }

        public static string Read(string participant, string fileName)
        {
            // The loose file in the data folder wins. Someone who has just regenerated
            // one participant expects to be running that, and silently preferring the
            // shipped copy would hand them a session they thought they had replaced.
            string loose = Path.Combine(Application.persistentDataPath, fileName);
            if (File.Exists(loose)) return File.ReadAllText(loose);

            if (string.IsNullOrEmpty(participant)) return null;
            return ShippedAssets.Get("participants/" + PackFor(participant) + "/" + fileName);
        }

        public static bool Has(string participant)
        {
            return Read(participant, "session.json") != null;
        }
    }
}
