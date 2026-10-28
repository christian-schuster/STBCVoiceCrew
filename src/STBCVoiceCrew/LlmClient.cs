using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using BepInEx.Logging;

namespace STBCVoiceCrew
{
    public sealed class ChatMessage
    {
        public string Role;
        public string Content;
        public ChatMessage(string role, string content) { Role = role; Content = content; }
    }

    /// <summary>
    /// Spricht die OpenAI-kompatible Schnittstelle von llama-server an (/v1/chat/completions).
    /// Anfragen laufen auf einem Hintergrund-Thread; das Ergebnis wird über den MainThread-Dispatcher
    /// zurück in den Unity-Hauptthread gereicht.
    /// </summary>
    public sealed class LlmClient
    {
        readonly ManualLogSource _log;
        readonly string _baseUrl;
        readonly int _timeoutMs;
        int _inFlight;

        public float Temperature = 0.6f;

        /// <summary>Laufende Anfragen. llama-server bearbeitet mehrere parallel (ein Slot je Offizier).</summary>
        public int InFlight => _inFlight;

        public LlmClient(ManualLogSource log, string baseUrl, int timeoutMs)
        {
            _log = log;
            _baseUrl = baseUrl.TrimEnd('/');
            _timeoutMs = timeoutMs;
            ServicePointManager.Expect100Continue = false;
        }

        /// <summary>
        /// Schickt eine Chat-Anfrage, deren Antwort per JSON-Schema erzwungen wird (llama.cpp-Grammatik).
        /// onDone(content, error, latencySec) läuft im Unity-Hauptthread.
        /// </summary>
        public void Send(List<ChatMessage> messages, object jsonSchema, int maxTokens, Action<string, string, double> onDone, float temperature = -1f)
        {
            Interlocked.Increment(ref _inFlight);
            var msgList = new List<object>();
            foreach (var m in messages)
                msgList.Add(new Dictionary<string, object> { { "role", m.Role }, { "content", m.Content } });

            var body = new Dictionary<string, object>
            {
                { "messages", msgList },
                { "temperature", temperature >= 0f ? temperature : Temperature },
                { "max_tokens", maxTokens },
                { "stream", false },
                // Denk-Modus (Qwen3 u. a.) aus: kostet sonst Sekunden, die im Gefecht fehlen
                { "chat_template_kwargs", new Dictionary<string, object> { { "enable_thinking", false } } }
            };
            if (jsonSchema != null)
            {
                body["response_format"] = new Dictionary<string, object>
                {
                    { "type", "json_schema" },
                    { "json_schema", new Dictionary<string, object> { { "name", "crew_response" }, { "schema", jsonSchema } } }
                };
            }
            string payload = Json.Serialize(body);

            var t = new Thread(() => Worker(payload, onDone)) { IsBackground = true, Name = "STBCVoiceCrew.Llm" };
            t.Start();
        }

        void Worker(string payload, Action<string, string, double> onDone)
        {
            string content = null, error = null;
            var started = DateTime.UtcNow;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(_baseUrl + "/v1/chat/completions");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = _timeoutMs;
                req.ReadWriteTimeout = _timeoutMs;
                req.Proxy = null;
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);

                string respText;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    respText = r.ReadToEnd();

                var choices = Json.Arr(Json.Get(Json.Parse(respText), "choices"));
                if (choices == null || choices.Count == 0) throw new Exception("Antwort ohne choices: " + Trunc(respText));
                content = Json.Str(Json.Get(choices[0], "message"), "content");
                if (content == null) throw new Exception("Antwort ohne message.content: " + Trunc(respText));
            }
            catch (WebException we)
            {
                error = we.Message;
                try
                {
                    if (we.Response != null)
                        using (var r = new StreamReader(we.Response.GetResponseStream(), Encoding.UTF8))
                            error += " – " + Trunc(r.ReadToEnd());
                }
                catch { }
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }

            double latency = (DateTime.UtcNow - started).TotalSeconds;
            if (error != null) _log.LogWarning("LLM-Anfrage fehlgeschlagen: " + error);
            MainThread.Post(() => onDone(content, error, latency));
        }

        static string Trunc(string s) => s == null ? "" : (s.Length > 300 ? s.Substring(0, 300) + "…" : s);
    }
}
