using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Cuvara.VFX.Editor.Tests
{
    /// <summary>Helpers to read a verb result in a test.</summary>
    static class TestJson
    {
        public static JsonObject Result(string verb, string args)
        {
            var text = VfxCli.Run(verb, args);
            Assert.That(text, Is.Not.Null.And.Not.Empty);
            var parsed = Json.Parse(text) as JsonObject;
            Assert.That(parsed, Is.Not.Null, "the result is not a JSON object: " + text);
            return parsed;
        }

        public static JsonObject Ok(string verb, string args)
        {
            var result = Result(verb, args);
            Assert.That(result.Get("ok"), Is.EqualTo(true), "errors: " + string.Join(" | ", Strings(result, "errors")));
            return result;
        }

        public static List<string> Strings(JsonObject obj, string key) =>
            ((List<object>)obj.Get(key) ?? new List<object>()).Select(o => (string)o).ToList();

        public static List<JsonObject> Objects(JsonObject obj, string key) =>
            ((List<object>)obj.Get(key) ?? new List<object>()).Cast<JsonObject>().ToList();
    }
}
