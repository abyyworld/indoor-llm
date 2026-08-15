// Writes one combined CSV per participant, inside the app, at the end of the session.
//
// The study writes six files at four different grains. That split is right at write time
// -- one writer would couple the affect grid to the telemetry clock -- but the person
// running the study should never have to know it, and "remember to run the bundler
// afterwards" is a step that gets forgotten on the day it matters.
//
// So it runs itself when the session ends or a participant withdraws. The Python
// bundle-participant command still exists and produces the same shape for re-running
// over old data; this is the copy that always happens.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace EmotionRooms
{
    public static class SessionBundle
    {
        /// <summary>
        /// Join every file belonging to `participant` into one long-format CSV.
        /// Returns the path written, or null if there was nothing to write.
        /// </summary>
        public static string Write(string participant)
        {
            string dir = StudyPaths.Data;
            string outDir = Path.Combine(dir, "bundles");
            Directory.CreateDirectory(outDir);

            var rows = new List<Dictionary<string, string>>();
            var columns = new List<string> { "source", "source_file" };

            Collect(rows, columns, Path.Combine(dir, "responses.csv"), "trial", participant);
            Collect(rows, columns, Path.Combine(dir, "oversight_responses.csv"), "review", participant);
            Collect(rows, columns, Path.Combine(dir, "questionnaire_responses.csv"), "questionnaire", participant);
            Collect(rows, columns, Path.Combine(dir, "rationale_responses.csv"), "rationale", participant);
            Collect(rows, columns, Path.Combine(dir, "consent_log.csv"), "consent", participant);

            // Event logs come in; telemetry stays out. The 20 Hz stream turned one
            // participant's bundle into 108 MB of padded columns while its own file
            // was 2.5 MB -- the wide format multiplies continuous streams. Telemetry
            // and the raw event files sit next to the bundle for anyone who wants
            // them; the bundle is the analysis-grade join, not an archive format.
            string logs = Path.Combine(dir, "logs");
            if (Directory.Exists(logs))
            {
                foreach (var path in Directory.GetFiles(logs, "*.csv"))
                {
                    string name = Path.GetFileName(path);
                    if (!name.Contains(participant)) continue;
                    if (name.Contains("telemetry")) continue;
                    Collect(rows, columns, path, "event", null);
                }
            }

            if (rows.Count == 0) return null;

            var text = new StringBuilder();
            text.Append(string.Join(",", columns.ToArray())).Append('\n');
            foreach (var row in rows)
            {
                for (int i = 0; i < columns.Count; i++)
                {
                    if (i > 0) text.Append(',');
                    string value;
                    if (!row.TryGetValue(columns[i], out value)) value = "";
                    text.Append(Escape(value));
                }
                text.Append('\n');
            }

            string outPath = Path.Combine(outDir, participant + "_all.csv");
            File.WriteAllText(outPath, text.ToString(), Encoding.UTF8);
            return outPath;
        }

        /// <param name="participant">Filter on the participant column, or null to take
        /// every row because the filename already identifies the participant.</param>
        static void Collect(List<Dictionary<string, string>> rows, List<string> columns,
                            string path, string source, string participant)
        {
            if (!File.Exists(path)) return;

            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (IOException e)
            {
                Debug.LogWarning("SessionBundle: could not read " + path + ": " + e.Message);
                return;
            }
            if (lines.Length < 2) return;

            var header = Split(lines[0]);
            foreach (var name in header)
                if (!columns.Contains(name)) columns.Add(name);

            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                var cells = Split(lines[i]);

                var row = new Dictionary<string, string>
                {
                    { "source", source },
                    { "source_file", Path.GetFileName(path) },
                };
                for (int c = 0; c < header.Length && c < cells.Length; c++)
                    row[header[c]] = cells[c];

                if (participant != null)
                {
                    string who;
                    if (!row.TryGetValue("participant", out who) || who != participant) continue;
                }
                rows.Add(row);
            }
        }

        /// <summary>Split one CSV line, honouring quoted cells that contain commas.</summary>
        static string[] Split(string line)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                        else quoted = false;
                    }
                    else cell.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { cells.Add(cell.ToString()); cell.Length = 0; }
                else cell.Append(c);
            }
            cells.Add(cell.ToString());
            return cells.ToArray();
        }

        static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0 && value.IndexOf('\n') < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"").Replace("\n", " ") + "\"";
        }
    }
}
