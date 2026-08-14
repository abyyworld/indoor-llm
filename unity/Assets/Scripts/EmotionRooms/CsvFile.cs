// Opening a results file without silently misaligning it.
//
// Both response writers created their header only when the file did not exist, and
// appended from then on. That is correct until the schema changes, which it has: the
// review file went from 22 columns to 25 to 26, and the trial file just gained one. A
// headset carrying a file from an older build keeps the old header and starts taking
// rows with more fields in them. Nothing errors. The file opens in any tool, every
// column is shifted, and the numbers underneath the wrong names are plausible.
//
// That is the worst kind of data loss because it does not look like loss. So the header
// is checked rather than assumed, and a file whose shape no longer matches is moved
// aside instead of being appended to.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace EmotionRooms
{
    public static class CsvFile
    {
        /// <summary>
        /// Make sure `path` exists and its first line is `header`.
        ///
        /// A file with a different header is renamed with a timestamp and a new one
        /// started. Nothing is deleted: the old rows stay readable next to the new ones,
        /// under the header they were actually written with.
        /// </summary>
        public static void EnsureHeader(string path, string header)
        {
            try
            {
                if (!File.Exists(path))
                {
                    File.WriteAllText(path, header + "\n", Encoding.UTF8);
                    return;
                }

                string first = null;
                using (var reader = new StreamReader(path, Encoding.UTF8))
                    first = reader.ReadLine();

                if (first == header) return;

                // Empty file from an interrupted create. Just write the header.
                if (string.IsNullOrEmpty(first))
                {
                    File.WriteAllText(path, header + "\n", Encoding.UTF8);
                    return;
                }

                string moved = path + "." +
                               DateTime.UtcNow.ToString("yyyyMMdd_HHmmss",
                                                        CultureInfo.InvariantCulture) +
                               ".old.csv";
                File.Move(path, moved);
                File.WriteAllText(path, header + "\n", Encoding.UTF8);

                Debug.LogWarning(
                    "CsvFile: " + Path.GetFileName(path) + " was written by an older " +
                    "build and its columns no longer match. The old file is kept as " +
                    Path.GetFileName(moved) + " and a new one started. Nothing was lost, " +
                    "but the two files have different columns and must be read separately.");
            }
            catch (Exception error)
            {
                Debug.LogError("CsvFile: could not prepare " + path + ": " + error.Message);
            }
        }
    }
}
