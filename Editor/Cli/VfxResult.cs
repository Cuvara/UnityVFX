using System;
using System.Collections.Generic;

namespace Cuvara.VFX.Editor
{
    /// <summary>
    /// The result of one verb: <c>{"ok", "verb", "errors", "warnings", ...payload}</c>.
    /// </summary>
    /// <remarks>
    /// <c>ok</c> is true exactly when no error was added. The payload keeps the order in which a verb
    /// sets its keys, so the written JSON is stable.
    /// </remarks>
    public sealed class VfxResult
    {
        static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.Ordinal) { "ok", "verb", "errors", "warnings" };

        readonly JsonObject m_Payload = new JsonObject();

        /// <summary>Creates an empty result for a verb.</summary>
        public VfxResult(string verb)
        {
            Verb = verb ?? string.Empty;
        }

        /// <summary>The verb name as requested.</summary>
        public string Verb { get; }

        /// <summary>Errors; any error makes <see cref="Ok"/> false.</summary>
        public List<string> Errors { get; } = new List<string>();

        /// <summary>Warnings; they do not change <see cref="Ok"/>.</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>True when no error was added.</summary>
        public bool Ok => Errors.Count == 0;

        /// <summary>The payload keys and values, in the order they were set.</summary>
        public IReadOnlyList<KeyValuePair<string, object>> Payload => m_Payload;

        /// <summary>Adds an error (the result becomes <c>"ok": false</c>).</summary>
        public void Error(string message) => Errors.Add(message ?? "error");

        /// <summary>Adds a warning.</summary>
        public void Warning(string message) => Warnings.Add(message ?? "warning");

        /// <summary>Sets a payload key. The envelope keys ok, verb, errors and warnings cannot be set.</summary>
        public void Set(string key, object value)
        {
            if (string.IsNullOrEmpty(key) || Reserved.Contains(key))
                throw new ArgumentException($"'{key}' is not a payload key");
            m_Payload.Set(key, value);
        }

        /// <summary>The value of a payload key, or null.</summary>
        public object Get(string key) => m_Payload.Get(key);

        /// <summary>The whole result as an ordered JSON object.</summary>
        public JsonObject ToJsonObject()
        {
            var json = new JsonObject
            {
                { "ok", Ok },
                { "verb", Verb },
                { "errors", new List<string>(Errors) },
                { "warnings", new List<string>(Warnings) },
            };
            foreach (var pair in m_Payload)
                json.Add(pair.Key, pair.Value);
            return json;
        }

        /// <summary>The whole result as JSON text.</summary>
        public string ToJson(bool indent = false) => Json.Write(ToJsonObject(), indent);
    }
}
