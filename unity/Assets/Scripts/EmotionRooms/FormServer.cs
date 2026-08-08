// Serves the questionnaires as web pages on localhost, so the participant fills them in
// a browser with the headset off.
//
// Why a local server rather than the in-game overlay it replaces: a questionnaire drawn
// into the Game view has to be answered through whatever input the study is using, in
// whatever the headset is doing, and it competes with the scene for the screen. A browser
// tab is the tool everyone already knows how to use, it scrolls and it has a keyboard,
// and it separates cleanly from the VR part -- which is the point, because nothing here
// should be answered while wearing a headset.
//
// Why local rather than Google Forms: the answers land in the same folder as the rest of
// the data, under the same participant id, with no export step and nothing leaving the
// machine. The page is generated from the same questionnaires.json the instruments
// module emits, so the wording cannot drift from what is written up.
//
// Nothing here blocks a session. The researcher opens a form when it is time, the
// participant submits it, and the panel moves on. A form never answered is recorded as
// never answered.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace EmotionRooms
{
    public class FormServer : MonoBehaviour
    {
        [Tooltip("Port for the local form pages. Nothing leaves this machine.")]
        public int port = 8752;

        public QuestionnaireRunner questionnaires;

        [Tooltip("So the researcher's laptop can start the session. On a standalone " +
                 "headset build there is no editor panel and no keyboard, and the app is " +
                 "the only thing that can start itself -- but it can be told to.")]
        public StudyBootstrap bootstrap;
        public TrialRunner trialRunner;
        public OversightReview review;

        /// <summary>Address for a browser on this machine.</summary>
        public string Root { get { return "http://localhost:" + port + "/"; } }

        /// <summary>
        /// Address for a browser on another machine on the same network.
        ///
        /// Needed for the standalone headset build: the app is running on the Quest, and
        /// the questionnaires have to be filled on the researcher's laptop. A server bound
        /// to localhost would be reachable only from inside the headset, which is the one
        /// place these must not be answered.
        /// </summary>
        public string NetworkRoot
        {
            get
            {
                string ip = LocalAddress();
                return ip == null ? Root : "http://" + ip + ":" + port + "/";
            }
        }

        public bool IsRunning { get { return listener != null; } }

        TcpListener listener;
        Thread thread;
        volatile bool stopping;

        // Touched from the listener thread, drained on the main thread.
        readonly Queue<Action> mainThread = new Queue<Action>();

        void OnEnable() { StartServer(); }
        void OnDisable() { StopServer(); }

        void Update()
        {
            lock (mainThread)
            {
                while (mainThread.Count > 0) mainThread.Dequeue()();
            }
        }

        public void StartServer()
        {
            if (IsRunning) return;
            try
            {
                // A raw TcpListener rather than HttpListener. HttpListener is unreliable
                // on Android and IL2CPP, which is exactly the standalone headset build,
                // and the protocol needed here is small enough to write out: read a
                // request line, read headers, read a body, write a response.
                //
                // Bound to Any, not loopback, so the researcher's laptop can reach a
                // server running on the headset.
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();

                stopping = false;
                thread = new Thread(Serve) { IsBackground = true };
                thread.Start();

                Debug.Log("FormServer: questionnaires at " + Root +
                          (NetworkRoot != Root ? "  (from another machine: " + NetworkRoot + ")" : ""));
            }
            catch (Exception e)
            {
                Debug.LogError("FormServer: could not listen on port " + port + ". " +
                               e.Message + "\nAnother copy of the study may still be " +
                               "running. Change the port on the FormServer component if so.");
                listener = null;
            }
        }

        public void StopServer()
        {
            stopping = true;
            if (listener != null)
            {
                try { listener.Stop(); } catch (Exception) { }
                listener = null;
            }
            thread = null;
        }

        void Serve()
        {
            while (!stopping && listener != null)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch (Exception) { return; }   // Stop() closing the listener lands here.

                try { using (client) Handle(client); }
                catch (Exception e)
                {
                    // A browser that opens a connection and closes it without sending
                    // anything is normal traffic, not a fault worth a line in the log.
                    if (e.Message.IndexOf("would block", StringComparison.OrdinalIgnoreCase) < 0 &&
                        e.Message.IndexOf("closed", StringComparison.OrdinalIgnoreCase) < 0)
                        Debug.LogWarning("FormServer: " + e.Message);
                }
            }
        }

        void Handle(TcpClient client)
        {
            var stream = client.GetStream();
            client.ReceiveTimeout = 5000;

            string requestLine = ReadLine(stream);
            if (string.IsNullOrEmpty(requestLine)) return;

            var parts = requestLine.Split(' ');
            if (parts.Length < 2) return;
            string method = parts[0];
            string target = parts[1];

            int contentLength = 0;
            for (string header = ReadLine(stream); !string.IsNullOrEmpty(header);
                 header = ReadLine(stream))
            {
                if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(header.Substring(15).Trim(), out contentLength);
            }

            string body = "";
            if (contentLength > 0)
            {
                var buffer = new byte[contentLength];
                int got = 0;
                while (got < contentLength)
                {
                    int read = stream.Read(buffer, got, contentLength - got);
                    if (read <= 0) break;
                    got += read;
                }
                body = Encoding.UTF8.GetString(buffer, 0, got);
            }

            string path = target;
            string query = "";
            int mark = target.IndexOf('?');
            if (mark >= 0) { path = target.Substring(0, mark); query = target.Substring(mark + 1); }

            Write(stream, Route(method, path, query, body));
        }

        string Route(string method, string path, string query, string body)
        {
            if (method == "POST" && path == "/submit")
            {
                var fields = ParseForm(body);
                string formId, group;
                fields.TryGetValue("__form", out formId);
                fields.TryGetValue("__group", out group);

                // State changes and file writes happen on the main thread, so the panel
                // can never read a half-updated questionnaire.
                var done = new ManualResetEvent(false);
                lock (mainThread)
                {
                    mainThread.Enqueue(() =>
                    {
                        try
                        {
                            if (questionnaires == null) return;

                            if (!string.IsNullOrEmpty(group))
                            {
                                // A grouped page: input names are form::item, split back
                                // into one submission per instrument so scoring and the
                                // stored files stay per-instrument.
                                foreach (var form in questionnaires.Due(group))
                                {
                                    var sub = new Dictionary<string, string>();
                                    string prefix = form.id + "::";
                                    foreach (var pair in fields)
                                        if (pair.Key.StartsWith(prefix))
                                            sub[pair.Key.Substring(prefix.Length)] = pair.Value;
                                    questionnaires.SubmitFromWeb(form.id, sub);
                                }
                            }
                            else
                            {
                                questionnaires.SubmitFromWeb(formId, fields);
                            }
                        }
                        finally { done.Set(); }
                    });
                }
                done.WaitOne(5000);
                return ThanksPage(formId);
            }

            if (path == "/set")
            {
                // Pushed from the control panel so nobody picks a participant twice.
                // Two places to set it is two places to get it different, and a
                // mismatched id means a session whose files never join up.
                var values = ParseForm(query);
                string who, practice;
                values.TryGetValue("participant", out who);
                values.TryGetValue("practice", out practice);
                string phases;
                values.TryGetValue("phases", out phases);

                var applied = new ManualResetEvent(false);
                lock (mainThread)
                {
                    mainThread.Enqueue(() =>
                    {
                        try
                        {
                            if (bootstrap != null)
                            {
                                if (!string.IsNullOrEmpty(who))
                                {
                                    bootstrap.participantId = who;
                                    bootstrap.ApplyParticipantId();
                                }
                                if (!string.IsNullOrEmpty(practice))
                                    bootstrap.practiceOnly = practice == "1";
                                int mode;
                                if (!string.IsNullOrEmpty(phases) &&
                                    int.TryParse(phases, out mode))
                                    bootstrap.sessionMode = mode;
                            }
                        }
                        finally { applied.Set(); }
                    });
                }
                applied.WaitOne(5000);

                // State rides back on the same request, so the panel can follow the
                // session without a second endpoint: is it running, which trial, and
                // whether the review block is still going.
                bool running = trialRunner != null && trialRunner.IsRunning;
                bool reviewing = review != null && review.IsRunning;
                int done = trialRunner != null ? trialRunner.CompletedTrials : 0;
                return "{\"participant\":\"" + Escape(bootstrap != null ? bootstrap.participantId : "") +
                       "\",\"practice\":" + (bootstrap != null && bootstrap.practiceOnly ? "true" : "false") +
                       ",\"phases\":" + (bootstrap != null ? bootstrap.sessionMode : 0) +
                       ",\"running\":" + ((running || reviewing) ? "true" : "false") +
                       ",\"reviewing\":" + (reviewing ? "true" : "false") +
                       ",\"trial\":" + done.ToString(CultureInfo.InvariantCulture) + ",\"of\":8}";
            }

            // Verification instrument, not a participant control. Answers whatever is
            // currently awaiting input -- the grid or a question panel -- so a whole
            // session can be exercised from the researcher machine with nobody in the
            // headset. Localhost only, like everything this server does, and every
            // answer it lands is marked REMOTE in the event log, so a driven session is
            // unmistakable in the data.
            if (path == "/answer")
            {
                var values = ParseForm(query);
                string result = "nothing awaiting input";
                var answered = new ManualResetEvent(false);
                lock (mainThread)
                {
                    mainThread.Enqueue(() =>
                    {
                        try
                        {
                            if (bootstrap != null && bootstrap.grid != null &&
                                bootstrap.grid.IsAwaitingResponse)
                            {
                                string v, a;
                                values.TryGetValue("v", out v);
                                values.TryGetValue("a", out a);
                                int valence, arousal;
                                if (int.TryParse(v, out valence) && int.TryParse(a, out arousal) &&
                                    bootstrap.grid.CommitCell(valence, arousal))
                                    result = "grid " + valence + "," + arousal;
                                else
                                    result = "grid awaiting but v/a missing or locked";
                                return;
                            }

                            var panel = bootstrap != null ? bootstrap.CurrentPanel() : null;
                            if (panel != null)
                            {
                                string option;
                                values.TryGetValue("option", out option);
                                int index;
                                if (int.TryParse(option, out index) && panel.TrySelectOption(index))
                                    result = "panel option " + index;
                                else
                                    result = "panel awaiting but option missing or locked";
                            }
                        }
                        finally { answered.Set(); }
                    });
                }
                answered.WaitOne(5000);
                return "{\"answered\":\"" + Escape(result) + "\"}";
            }

            if (path == "/start")
            {
                var values = ParseForm(query);
                string who;
                values.TryGetValue("participant", out who);

                var done = new ManualResetEvent(false);
                lock (mainThread)
                {
                    mainThread.Enqueue(() =>
                    {
                        try
                        {
                            if (bootstrap != null)
                            {
                                if (!string.IsNullOrEmpty(who))
                                {
                                    bootstrap.participantId = who;
                                    bootstrap.ApplyParticipantId();
                                }
                                bootstrap.BeginStudy();
                            }
                        }
                        finally { done.Set(); }
                    });
                }
                done.WaitOne(5000);
                return ControlPage("Started. Put the headset on the participant.");
            }

            if (path == "/group")
            {
                var values = ParseForm(query);
                string when;
                values.TryGetValue("when", out when);
                string phase;
                values.TryGetValue("phase", out phase);
                return GroupPage(when == "after" ? "after" : "before", phase);
            }

            if (path == "/form")
            {
                var values = ParseForm(query);
                string id;
                values.TryGetValue("id", out id);
                var form = questionnaires != null ? questionnaires.Find(id) : null;
                return form == null ? NotFound(id) : FormPage(form);
            }

            return ControlPage(null);
        }

        static string ReadLine(NetworkStream stream)
        {
            var line = new StringBuilder();
            int b;
            while ((b = stream.ReadByte()) != -1)
            {
                if (b == '\n') break;
                if (b != '\r') line.Append((char)b);
            }
            return line.ToString();
        }

        static void Write(NetworkStream stream, string html)
        {
            var body = Encoding.UTF8.GetBytes(html);
            var head = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                "Content-Length: " + body.Length + "\r\n" +
                "Connection: close\r\n\r\n");
            stream.Write(head, 0, head.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        static string LocalAddress()
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (var address in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string ip = address.Address.ToString();
                        if (!ip.StartsWith("169.254")) return ip;
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        static Dictionary<string, string> ParseForm(string body)
        {
            var fields = new Dictionary<string, string>();
            foreach (var pair in body.Split('&'))
            {
                if (pair.Length == 0) continue;
                int split = pair.IndexOf('=');
                string key = split < 0 ? pair : pair.Substring(0, split);
                string value = split < 0 ? "" : pair.Substring(split + 1);
                fields[Uri.UnescapeDataString(key.Replace('+', ' '))] =
                    Uri.UnescapeDataString(value.Replace('+', ' '));
            }
            return fields;
        }

        // -------------------------------------------------------------------- pages

        const string Style = @"
<style>
  :root { color-scheme: light dark; }
  body { font: 17px/1.6 -apple-system, system-ui, sans-serif; max-width: 46rem;
         margin: 0 auto; padding: 2rem 1.25rem 6rem; }
  h1 { font-size: 1.6rem; margin: 0 0 .4rem; }
  .intro { white-space: pre-wrap; opacity: .85; margin-bottom: 2rem; }
  .q { margin: 0 0 1.9rem; padding-bottom: 1.4rem; border-bottom: 1px solid #8884; }
  .q:last-of-type { border-bottom: 0; }
  .t { font-weight: 600; margin-bottom: .15rem; }
  .help { opacity: .7; font-size: .92rem; margin-bottom: .6rem; }
  .opts { display: flex; flex-wrap: wrap; gap: .45rem; }
  label.opt { border: 1px solid #8886; border-radius: .5rem; padding: .45rem .85rem;
              cursor: pointer; user-select: none; }
  label.opt:has(input:checked) { border-color: #3b82f6; background: #3b82f622;
                                 font-weight: 600; }
  label.opt input { margin-right: .4rem; }
  .ends { display: flex; justify-content: space-between; font-size: .85rem;
          opacity: .7; margin-top: .3rem; }
  textarea, input[type=text] { width: 100%; font: inherit; padding: .6rem;
          border-radius: .5rem; border: 1px solid #8886; background: transparent;
          color: inherit; }
  textarea { min-height: 5.5rem; }
  .bar { position: fixed; left: 0; right: 0; bottom: 0; padding: .9rem 1.25rem;
         background: Canvas; border-top: 1px solid #8884; text-align: center; }
  button { font: inherit; font-weight: 600; padding: .65rem 2rem; border-radius: .5rem;
           border: 0; background: #3b82f6; color: #fff; cursor: pointer; }
  .cite { font-size: .8rem; opacity: .55; margin-top: 2rem; }
  ul { padding-left: 1.2rem; }
  a { color: #3b82f6; }
</style>";

        /// <summary>
        /// The researcher panel, served by the headset to the laptop.
        ///
        /// A standalone build has no editor window and no keyboard, and IMGUI does not
        /// render into an immersive view, so there is no interface inside the headset at
        /// all -- which is correct, because nothing should be operated from in there. The
        /// app therefore serves its own controls to whatever browser the researcher has.
        /// </summary>
        string ControlPage(string note)
        {
            var page = new StringBuilder();
            page.Append("<!doctype html><meta charset=utf-8><title>Emotion Rooms</title>");
            page.Append("<meta name=viewport content='width=device-width,initial-scale=1'>");
            // The refresh is on the CONTROL page only, never on a form. A questionnaire
            // that reloaded under someone would lose whatever they had typed, so the
            // forms are static pages that only leave when they are submitted.
            page.Append("<meta http-equiv=refresh content='10'>");
            page.Append(Style);
            page.Append("<h1>Emotion Rooms</h1>");

            if (!string.IsNullOrEmpty(note))
                page.Append("<p style='color:#4ade80;font-weight:600'>")
                    .Append(Escape(note)).Append("</p>");

            string who = bootstrap != null ? bootstrap.participantId : "";
            bool running = trialRunner != null && trialRunner.IsRunning;

            bool practiceOnly = bootstrap != null && bootstrap.practiceOnly;
            page.Append("<p>Participant <b>").Append(Escape(who)).Append("</b>")
                .Append(practiceOnly ? " &middot; <b>practice only</b>" : "")
                .Append(". Set from the control panel. ")
                .Append(running
                    ? "Running — trial " + (trialRunner != null ? trialRunner.CompletedTrials : 0) + " of 8."
                    : "Not started.")
                .Append("</p>");

            page.Append("<form method=get action=/start>");
            page.Append("<input type=hidden name=participant value='")
                .Append(Escape(who)).Append("'>");

            page.Append("<h2>1. Questionnaires — before</h2>");
            page.Append("<p><a href='/group?when=before'><b>Open them all on one page</b></a></p>");

            page.Append("<h2>2. Fit the headset, then start</h2>");
            page.Append("<div class=bar><button type=submit")
                .Append(running ? " disabled" : "")
                .Append(">").Append(running ? "Running…" : "START THE ROOMS")
                .Append("</button></div></form>");

            page.Append("<h2>3. Questionnaires — after</h2>");
            page.Append("<p><a href='/group?when=after'><b>Open them all on one page</b></a></p>");

            page.Append("<p class=cite>Served by the app on the headset; refreshes every ")
                .Append("ten seconds. Answers are written the moment a form is submitted, ")
                .Append("so nothing here can lose them.</p>");
            return page.ToString();
        }

        string IndexPage()
        {
            var page = new StringBuilder();
            page.Append("<!doctype html><meta charset=utf-8><title>Emotion Rooms</title>");
            page.Append("<meta name=viewport content='width=device-width,initial-scale=1'>");
            page.Append(Style).Append("<h1>Emotion Rooms</h1>");

            if (questionnaires == null)
            {
                page.Append("<p>No questionnaires loaded.</p>");
                return page.ToString();
            }

            page.Append("<p class=intro>Participant <b>")
                .Append(Escape(questionnaires.participantId))
                .Append("</b>. The researcher will tell you which to open.</p><ul>");

            foreach (var form in questionnaires.AllForms())
            {
                page.Append("<li><a href='/form?id=").Append(Escape(form.id)).Append("'>")
                    .Append(Escape(form.title)).Append("</a> — ")
                    .Append(questionnaires.StateOf(form.id) == FormState.Completed
                        ? "done" : "not yet")
                    .Append("</li>");
            }
            page.Append("</ul>");
            return page.ToString();
        }

        /// <summary>
        /// Every instrument due at one point in the session, on one page with one
        /// submit. Separate instruments are kept separate in storage -- scoring and
        /// citations depend on it -- but a participant answers them in one sitting.
        /// Twelve tabs was twelve chances to lose their attention.
        /// </summary>
        string GroupPage(string when, string phase)
        {
            // The runner already filters by the participant's phase; an explicit phase
            // in the URL only narrows it further, so a link cannot hand somebody a form
            // that does not apply to them.
            if (questionnaires != null && !string.IsNullOrEmpty(phase))
                questionnaires.phase = phase;

            var forms = questionnaires != null
                ? questionnaires.Due(when)
                : new List<QuestionForm>();

            var page = new StringBuilder();
            page.Append("<!doctype html><meta charset=utf-8><title>Emotion Rooms</title>");
            page.Append("<meta name=viewport content='width=device-width,initial-scale=1'>");
            page.Append(Style);
            page.Append("<h1>").Append(when == "after" ? "A few last questions" : "Before we start")
                .Append("</h1>");

            if (forms.Count == 0)
            {
                page.Append("<p class=intro>Nothing to show.</p>");
                return page.ToString();
            }

            page.Append("<form method=post action=/submit>");
            page.Append("<input type=hidden name=__group value='").Append(Escape(when)).Append("'>");

            foreach (var form in forms)
            {
                page.Append("<h2 style='margin:2.2rem 0 .2rem'>").Append(Escape(form.title)).Append("</h2>");
                if (!string.IsNullOrEmpty(form.instruction))
                    page.Append("<div class=intro>").Append(Escape(form.instruction)).Append("</div>");
                foreach (var item in form.items)
                    AppendItem(page, form.id + "::" + item.id, item);
                if (!string.IsNullOrEmpty(form.citation))
                    page.Append("<div class=cite>").Append(Escape(form.citation)).Append("</div>");
            }

            page.Append("<div class=bar><button type=submit>Submit</button></div></form>");
            return page.ToString();
        }

        string FormPage(QuestionForm form)
        {
            var page = new StringBuilder();
            page.Append("<!doctype html><meta charset=utf-8><title>")
                .Append(Escape(form.title)).Append("</title>");
            page.Append("<meta name=viewport content='width=device-width,initial-scale=1'>");
            page.Append(Style);

            page.Append("<h1>").Append(Escape(form.title)).Append("</h1>");
            if (!string.IsNullOrEmpty(form.instruction))
                page.Append("<div class=intro>").Append(Escape(form.instruction)).Append("</div>");

            page.Append("<form method=post action=/submit>");
            page.Append("<input type=hidden name=__form value='").Append(Escape(form.id)).Append("'>");

            foreach (var item in form.items)
                AppendItem(page, item.id, item);

            if (!string.IsNullOrEmpty(form.citation))
                page.Append("<div class=cite>").Append(Escape(form.citation)).Append("</div>");

            // No required attributes anywhere. Any question may be left blank and the
            // form still submits: a participant declining to answer is data, and a form
            // that will not submit until it is full produces compliance, not answers.
            page.Append("<div class=bar><button type=submit>Submit</button></div>");
            page.Append("</form>");
            return page.ToString();
        }

        /// <summary>One question, rendered under whatever input name the page needs --
        /// the bare item id on a single form, form::item on a grouped page.</summary>
        static void AppendItem(StringBuilder page, string name, QuestionItem item)
        {
            page.Append("<div class=q>");
            if (!string.IsNullOrEmpty(item.text))
                page.Append("<div class=t>").Append(Escape(item.text)).Append("</div>");
            if (!string.IsNullOrEmpty(item.help))
                page.Append("<div class=help>").Append(Escape(item.help)).Append("</div>");

            switch (item.type)
            {
                case "choice":
                    page.Append("<div class=opts>");
                    foreach (var option in item.options)
                        page.Append("<label class=opt><input type=radio name='")
                            .Append(Escape(name)).Append("' value='")
                            .Append(Escape(option)).Append("'>")
                            .Append(Escape(option)).Append("</label>");
                    page.Append("</div>");
                    break;

                case "scale":
                    page.Append("<div class=opts>");
                    for (int v = item.min; v <= item.max; v += item.Step)
                        page.Append("<label class=opt><input type=radio name='")
                            .Append(Escape(name)).Append("' value='").Append(v)
                            .Append("'>").Append(v).Append("</label>");
                    page.Append("</div><div class=ends><span>")
                        .Append(Escape(item.min_label ?? "")).Append("</span><span>")
                        .Append(Escape(item.max_label ?? "")).Append("</span></div>");
                    break;

                case "paragraph":
                    page.Append("<textarea name='").Append(Escape(name)).Append("'></textarea>");
                    break;

                default:
                    page.Append("<input type=text name='").Append(Escape(name)).Append("'>");
                    break;
            }
            page.Append("</div>");
        }

        static string ThanksPage(string formId)
        {
            return "<!doctype html><meta charset=utf-8><title>Thank you</title>" + Style +
                   "<h1>Thank you</h1><p class=intro>Your answers have been saved. " +
                   "Please let the researcher know you have finished.</p>" +
                   "<p><a href='/'>All forms</a></p>";
        }

        static string NotFound(string id)
        {
            return "<!doctype html><meta charset=utf-8>" + Style +
                   "<h1>Not found</h1><p class=intro>No form called '" + Escape(id) +
                   "'.</p><p><a href='/'>All forms</a></p>";
        }

        static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                       .Replace("\"", "&quot;").Replace("'", "&#39;");
        }
    }
}
