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

        public string Root { get { return "http://localhost:" + port + "/"; } }
        public bool IsRunning { get { return listener != null && listener.IsListening; } }

        HttpListener listener;
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
                listener = new HttpListener();
                listener.Prefixes.Add(Root);
                listener.Start();

                stopping = false;
                thread = new Thread(Serve) { IsBackground = true };
                thread.Start();

                Debug.Log("FormServer: questionnaires available at " + Root);
            }
            catch (Exception e)
            {
                Debug.LogError("FormServer: could not start on " + Root + ". " + e.Message +
                               "\nAnother copy of the study may still be running. Change " +
                               "the port on the FormServer component if so.");
                listener = null;
            }
        }

        public void StopServer()
        {
            stopping = true;
            if (listener != null)
            {
                try { listener.Stop(); listener.Close(); } catch (Exception) { }
                listener = null;
            }
            thread = null;
        }

        void Serve()
        {
            while (!stopping && listener != null && listener.IsListening)
            {
                HttpListenerContext context;
                try { context = listener.GetContext(); }
                catch (Exception) { return; }   // Stop() closing the listener lands here.

                try { Handle(context); }
                catch (Exception e)
                {
                    Debug.LogError("FormServer: " + e.Message);
                    try { context.Response.Abort(); } catch (Exception) { }
                }
            }
        }

        void Handle(HttpListenerContext context)
        {
            string path = context.Request.Url.AbsolutePath;

            if (context.Request.HttpMethod == "POST" && path == "/submit")
            {
                string body;
                using (var reader = new StreamReader(context.Request.InputStream,
                                                     context.Request.ContentEncoding))
                    body = reader.ReadToEnd();

                var fields = ParseForm(body);
                string formId;
                fields.TryGetValue("__form", out formId);

                // File writing and state changes happen on the main thread, so the
                // questionnaire state cannot be read half-updated by the panel.
                var done = new ManualResetEvent(false);
                lock (mainThread)
                {
                    mainThread.Enqueue(() =>
                    {
                        try { if (questionnaires != null) questionnaires.SubmitFromWeb(formId, fields); }
                        finally { done.Set(); }
                    });
                }
                done.WaitOne(5000);

                Respond(context, ThanksPage(formId), "text/html");
                return;
            }

            if (path == "/form")
            {
                string id = context.Request.QueryString["id"];
                var form = questionnaires != null ? questionnaires.Find(id) : null;
                if (form == null) { Respond(context, NotFound(id), "text/html", 404); return; }
                Respond(context, FormPage(form), "text/html");
                return;
            }

            Respond(context, IndexPage(), "text/html");
        }

        static void Respond(HttpListenerContext context, string html, string type, int code = 200)
        {
            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.StatusCode = code;
            context.Response.ContentType = type + "; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
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
                                .Append(Escape(item.id)).Append("' value='")
                                .Append(Escape(option)).Append("'>")
                                .Append(Escape(option)).Append("</label>");
                        page.Append("</div>");
                        break;

                    case "scale":
                        page.Append("<div class=opts>");
                        for (int v = item.min; v <= item.max; v += item.Step)
                            page.Append("<label class=opt><input type=radio name='")
                                .Append(Escape(item.id)).Append("' value='").Append(v)
                                .Append("'>").Append(v).Append("</label>");
                        page.Append("</div><div class=ends><span>")
                            .Append(Escape(item.min_label ?? "")).Append("</span><span>")
                            .Append(Escape(item.max_label ?? "")).Append("</span></div>");
                        break;

                    case "paragraph":
                        page.Append("<textarea name='").Append(Escape(item.id)).Append("'></textarea>");
                        break;

                    default:
                        page.Append("<input type=text name='").Append(Escape(item.id)).Append("'>");
                        break;
                }
                page.Append("</div>");
            }

            if (!string.IsNullOrEmpty(form.citation))
                page.Append("<div class=cite>").Append(Escape(form.citation)).Append("</div>");

            // No required attributes anywhere. Any question may be left blank and the
            // form still submits: a participant declining to answer is data, and a form
            // that will not submit until it is full produces compliance, not answers.
            page.Append("<div class=bar><button type=submit>Submit</button></div>");
            page.Append("</form>");
            return page.ToString();
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
