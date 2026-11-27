using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Cuvara.VFX.Editor
{
    /// <summary>
    /// The JSON arguments of one verb call, with typed accessors that record which keys were read.
    /// </summary>
    /// <remarks>
    /// A key that no accessor read is reported by <see cref="VfxCli.Run"/> as a warning, so a misspelt
    /// argument does not pass silently. Accessors throw <see cref="VfxArgumentException"/> on a missing
    /// required key or a wrong type; <see cref="VfxCli.Run"/> turns that into an error of the result.
    /// </remarks>
    public sealed class VfxArgs
    {
        readonly JsonObject m_Values;
        readonly HashSet<string> m_Read = new HashSet<string>(StringComparer.Ordinal);

        VfxArgs(JsonObject values)
        {
            m_Values = values ?? new JsonObject();
        }

        /// <summary>Parses the argument text: null, empty or whitespace mean no arguments; otherwise a JSON object.</summary>
        public static VfxArgs Parse(string jsonArgs)
        {
            if (string.IsNullOrWhiteSpace(jsonArgs))
                return new VfxArgs(new JsonObject());
            object parsed;
            try
            {
                parsed = Json.Parse(jsonArgs);
            }
            catch (FormatException exception)
            {
                throw new VfxArgumentException("arguments are not valid JSON: " + exception.Message);
            }
            if (parsed is JsonObject obj)
                return new VfxArgs(obj);
            if (parsed == null)
                return new VfxArgs(new JsonObject());
            throw new VfxArgumentException("arguments must be a JSON object");
        }

        /// <summary>The raw values.</summary>
        public JsonObject Values => m_Values;

        /// <summary>Keys present in the arguments that no accessor read, in order.</summary>
        public IEnumerable<string> UnreadKeys => m_Values.Select(p => p.Key).Where(k => !m_Read.Contains(k));

        /// <summary>True when the key is present (null counts as present).</summary>
        public bool Has(string key)
        {
            m_Read.Add(key);
            return m_Values.Has(key);
        }

        /// <summary>A string argument; null when absent and not required.</summary>
        public string GetString(string key, bool required = false)
        {
            m_Read.Add(key);
            var value = m_Values.Get(key);
            if (value == null)
            {
                if (required)
                    throw new VfxArgumentException($"missing required argument '{key}' (string)");
                return null;
            }
            if (value is string s)
                return s;
            throw new VfxArgumentException($"argument '{key}' must be a string");
        }

        /// <summary>A boolean argument, or <paramref name="fallback"/> when absent.</summary>
        public bool GetBool(string key, bool fallback)
        {
            m_Read.Add(key);
            var value = m_Values.Get(key);
            if (value == null)
                return fallback;
            if (value is bool b)
                return b;
            throw new VfxArgumentException($"argument '{key}' must be true or false");
        }

        /// <summary>An integer argument, or <paramref name="fallback"/> when absent.</summary>
        public long GetLong(string key, long fallback)
        {
            m_Read.Add(key);
            var value = m_Values.Get(key);
            switch (value)
            {
                case null: return fallback;
                case long l: return l;
                case double d when Math.Abs(d - Math.Round(d)) < 1e-9 && d >= long.MinValue && d <= long.MaxValue: return (long)d;
                default: throw new VfxArgumentException($"argument '{key}' must be an integer");
            }
        }

        /// <summary>A number argument, or <paramref name="fallback"/> when absent.</summary>
        public double GetDouble(string key, double fallback)
        {
            m_Read.Add(key);
            var value = m_Values.Get(key);
            switch (value)
            {
                case null: return fallback;
                case long l: return l;
                case double d: return d;
                default: throw new VfxArgumentException($"argument '{key}' must be a number");
            }
        }

        /// <summary>The raw value of a key (marks it read).</summary>
        public object GetRaw(string key)
        {
            m_Read.Add(key);
            return m_Values.Get(key);
        }

        /// <summary>For messages: the keys present, comma separated.</summary>
        public override string ToString() => string.Join(", ", m_Values.Select(p => p.Key.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>A bad argument of a verb call; becomes an error of the result.</summary>
    public sealed class VfxArgumentException : Exception
    {
        /// <summary>Creates the exception.</summary>
        public VfxArgumentException(string message) : base(message)
        {
        }
    }
}
