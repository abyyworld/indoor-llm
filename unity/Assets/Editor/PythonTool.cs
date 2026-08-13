// Finding and running Python, once, for everything that needs it.
//
// This exists because the same bug was fixed twice and then appeared a third time.
// The panel shelled out to /bin/bash to run the pipeline; the study server shelled
// out to /bin/bash to run serve-study.py. Neither works on Windows, and the study is
// run by two people on two operating systems, so every one of those calls is a
// session that cannot start on somebody's machine.
//
// The rules that took a while to learn, kept here so they are not rediscovered:
//
//   * Windows installs the interpreter as "python" from python.org, as "py" through
//     the launcher (which is on PATH even when python is not), and as "python3" only
//     sometimes. macOS and Linux use "python3", where bare "python" is often absent.
//   * The Microsoft Store stub answers to "python" and exits without running
//     anything, so a name resolving is not proof it works.
//   * "py" needs -3 to select a Python 3.
//   * A process that starts and then fails is a real error worth surfacing, not a
//     reason to try the next candidate name.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace EmotionRooms.EditorTools
{
    public static class PythonTool
    {
        /// <summary>Interpreter names to try, most likely first for this platform.</summary>
        public static string[] Candidates
        {
            get
            {
                return Application.platform == RuntimePlatform.WindowsEditor
                    ? new[] { "python", "py", "python3" }
                    : new[] { "python3", "python" };
            }
        }

        /// <summary>The name that worked last time, tried first from then on.</summary>
        static string found;

        /// <summary>
        /// How to spell a pipeline command on THIS machine, for text a person will read.
        ///
        /// Every dialog in the project used to say "python3 -m pipeline.cli ...". On
        /// Windows python3 usually does not exist, so the one instruction offered to
        /// the researcher who runs the sessions was an instruction that fails.
        /// </summary>
        public static string Cli
        {
            get
            {
                string exe = found;
                if (string.IsNullOrEmpty(exe))
                    exe = Application.platform == RuntimePlatform.WindowsEditor
                        ? "python" : "python3";
                return exe + (exe == "py" ? " -3" : "") + " -m pipeline.cli";
            }
        }

        /// <summary>What to tell someone when no interpreter can be started.</summary>
        public static string InstallHint(IEnumerable<string> tried)
        {
            return "Could not run Python. Tried: " + string.Join(", ", new List<string>(tried).ToArray()) +
                   ".\n\nInstall Python 3 from python.org and tick \"Add python.exe to PATH\" " +
                   "during setup, then restart Unity so it inherits the new PATH. Restarting " +
                   "matters: a Unity that was already open will not see a PATH change.";
        }

        static string Prefix(string exe) { return exe == "py" ? "-3 " : ""; }

        /// <summary>
        /// Start a long-running Python process and hand it back, or null.
        ///
        /// Used for the study server, which has to keep running: the caller keeps the
        /// handle so it can be stopped and so its exit can be reported.
        /// </summary>
        public static Process Launch(string arguments, string workingDirectory,
                                     bool redirect, out string error)
        {
            var tried = new List<string>();

            foreach (var exe in Order())
            {
                try
                {
                    var info = new ProcessStartInfo(exe, Prefix(exe) + arguments)
                    {
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = false,
                        RedirectStandardOutput = redirect,
                        RedirectStandardError = redirect,
                        CreateNoWindow = true,
                    };
                    var started = Process.Start(info);
                    if (started == null) { tried.Add(exe + " (did not start)"); continue; }

                    found = exe;
                    error = null;
                    return started;
                }
                catch (Exception e)
                {
                    tried.Add(exe + " (" + e.Message.Split('\n')[0] + ")");
                }
            }

            error = InstallHint(tried);
            return null;
        }

        /// <summary>
        /// Run a Python command to completion. True when it exits zero.
        ///
        /// An interpreter that runs and returns non-zero stops the search: that is the
        /// pipeline failing, and trying the next name would bury the real message.
        /// </summary>
        public static bool Run(string arguments, string workingDirectory, out string output)
        {
            var tried = new List<string>();
            output = "";

            foreach (var exe in Order())
            {
                try
                {
                    var info = new ProcessStartInfo(exe, Prefix(exe) + arguments)
                    {
                        WorkingDirectory = workingDirectory,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    };
                    using (var process = Process.Start(info))
                    {
                        string text = process.StandardOutput.ReadToEnd() +
                                      process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        output = text.Trim();
                        found = exe;
                        return process.ExitCode == 0;
                    }
                }
                catch (Exception e)
                {
                    tried.Add(exe + " (" + e.Message.Split('\n')[0] + ")");
                }
            }

            output = InstallHint(tried);
            return false;
        }

        /// <summary>Candidates with the one that already worked moved to the front.</summary>
        static IEnumerable<string> Order()
        {
            if (!string.IsNullOrEmpty(found)) yield return found;
            foreach (var exe in Candidates)
                if (exe != found) yield return exe;
        }
    }
}
