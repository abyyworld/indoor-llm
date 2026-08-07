// Lets the control panel talk to serve-study.py.
//
// So every step of a session is a button in one window: the panel reads the headset's
// state and sends the start command, instead of the researcher keeping a browser tab
// open alongside and remembering which of the two is authoritative.
//
// Requests are fired and forgotten with a callback, pumped off EditorApplication.update.
// UnityWebRequest cannot be awaited in an editor window and a blocking call would freeze
// the editor for the length of the timeout every time the panel repainted.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace EmotionRooms.EditorTools
{
    [Serializable]
    public class ServerState
    {
        public string command;
        public string participant;
        public string headset;      // away | ready | in_vr | running | finished
        public int trial;
        public int of;
        public bool connected;
    }

    [Serializable]
    public class HeadsetState
    {
        public string participant;
        public bool practice;
        public bool running;
        public bool reviewing;
        public int trial;
        public int of;
    }

    public static class StudyServerLink
    {
        /// <summary>
        /// Accepts the study server's own certificate.
        ///
        /// It is self-signed on purpose -- WebXR needs HTTPS and there is no certificate
        /// authority for a laptop on a lab network. This only ever talks to localhost, so
        /// there is no man in the middle to be protected from; refusing it would just mean
        /// the panel could not see its own server.
        /// </summary>
        class LocalCertificate : CertificateHandler
        {
            protected override bool ValidateCertificate(byte[] certificate) { return true; }
        }

        const string Base = "https://localhost:8443";

        static readonly List<Pending> inFlight = new List<Pending>();

        class Pending
        {
            public UnityWebRequest request;
            public Action<string> onDone;
        }

        static StudyServerLink()
        {
            EditorApplication.update += Pump;
        }

        static void Pump()
        {
            for (int i = inFlight.Count - 1; i >= 0; i--)
            {
                var pending = inFlight[i];
                if (!pending.request.isDone) continue;

                inFlight.RemoveAt(i);
                string body = pending.request.result == UnityWebRequest.Result.Success
                    ? pending.request.downloadHandler.text
                    : null;
                pending.request.Dispose();

                if (pending.onDone != null) pending.onDone(body);
            }
        }

        static void Send(UnityWebRequest request, Action<string> onDone)
        {
            request.certificateHandler = new LocalCertificate();
            request.disposeCertificateHandlerOnDispose = true;
            request.timeout = 4;
            request.SendWebRequest();
            inFlight.Add(new Pending { request = request, onDone = onDone });
        }

        /// <summary>Current headset and session state, or null if the server is down.</summary>
        public static void FetchState(Action<ServerState> onDone)
        {
            Send(UnityWebRequest.Get(Base + "/state"), body =>
            {
                if (body == null) { onDone(null); return; }
                try { onDone(JsonUtility.FromJson<ServerState>(body)); }
                catch (Exception) { onDone(null); }
            });
        }

        public static void SetParticipant(string participant)
        {
            Post("{\"participant\":\"" + Escape(participant) + "\"}", null);
        }

        public static void StartRooms(string participant, Action<bool> onDone)
        {
            Post("{\"command\":\"run\",\"participant\":\"" + Escape(participant) + "\"}",
                 body => { if (onDone != null) onDone(body != null); });
        }

        static void Post(string json, Action<string> onDone)
        {
            var request = new UnityWebRequest(Base + "/command", "POST")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            request.SetRequestHeader("Content-Type", "application/json");
            Send(request, onDone);
        }

        public static void Bundle(string participant, Action<string> onDone)
        {
            Send(UnityWebRequest.Get(Base + "/bundle?participant=" +
                                     UnityWebRequest.EscapeURL(participant)), onDone);
        }

        /// <summary>
        /// Push the participant and the mode to the app running on the headset.
        ///
        /// The panel is where these are set, so they are sent rather than asked for
        /// again on the other side. Two places to set a participant id is two places to
        /// get it different, and a mismatched id is a session whose files never join up.
        /// </summary>
        public static void PushToHeadset(string headsetIp, string participant,
                                         bool practiceOnly, int sessionMode,
                                         Action<bool> onDone)
        {
            if (string.IsNullOrEmpty(headsetIp))
            {
                if (onDone != null) onDone(false);
                return;
            }

            string url = "http://" + headsetIp + ":8752/set?participant=" +
                         UnityWebRequest.EscapeURL(participant ?? "") +
                         "&practice=" + (practiceOnly ? "1" : "0") +
                         "&phases=" + sessionMode;
            Send(UnityWebRequest.Get(url), body =>
            {
                if (onDone != null) onDone(body != null);
            });
        }

        public static void StartOnHeadset(string headsetIp, string participant,
                                          Action<bool> onDone)
        {
            if (string.IsNullOrEmpty(headsetIp))
            {
                if (onDone != null) onDone(false);
                return;
            }
            string url = "http://" + headsetIp + ":8752/start?participant=" +
                         UnityWebRequest.EscapeURL(participant ?? "");
            Send(UnityWebRequest.Get(url), body =>
            {
                if (onDone != null) onDone(body != null);
            });
        }

        /// <summary>What the app on the headset is doing right now, or null.</summary>
        public static void QueryHeadset(string headsetIp, Action<HeadsetState> onDone)
        {
            if (string.IsNullOrEmpty(headsetIp)) { onDone(null); return; }

            // /set with no parameters changes nothing and reports everything.
            Send(UnityWebRequest.Get("http://" + headsetIp + ":8752/set"), body =>
            {
                if (body == null) { onDone(null); return; }
                try { onDone(JsonUtility.FromJson<HeadsetState>(body)); }
                catch (Exception) { onDone(null); }
            });
        }

        public static string HeadsetPage(string headsetIp, string path)
        {
            return "http://" + headsetIp + ":8752/" + path;
        }

        public static string FormUrl(string group, string participant, int sessionMode)
        {
            string phase = sessionMode == 1 ? "A" : sessionMode == 2 ? "B" : "";
            return Base + "/form.html?group=" + group +
                   "&participant=" + UnityWebRequest.EscapeURL(participant) +
                   (phase.Length > 0 ? "&phase=" + phase : "");
        }

        static string Escape(string value)
        {
            return (value ?? "").Replace("\\", "").Replace("\"", "");
        }
    }
}
