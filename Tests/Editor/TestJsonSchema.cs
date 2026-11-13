using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Cuvara.VFX.Editor.Tests
{
    /// <summary>
    /// A small JSON Schema (2020-12) validator for the keywords the package schemas use: <c>$ref</c> to
    /// <c>#/$defs/...</c>, <c>type</c>, <c>const</c>, <c>enum</c>, <c>required</c>, <c>properties</c>,
    /// <c>additionalProperties</c>, <c>items</c>, <c>minItems</c>, <c>maxItems</c>, <c>pattern</c>,
    /// <c>minLength</c>, <c>minimum</c>, <c>exclusiveMinimum</c>, <c>maximum</c>, <c>allOf</c>, <c>anyOf</c>,
    /// <c>oneOf</c>, <c>if</c>/<c>then</c>/<c>else</c>. An unknown keyword fails the validation, so a
    /// schema change cannot pass the tests by being ignored. CI validates with the Python
    /// <c>jsonschema</c> package (<c>.github/scripts/check_recipes.py</c>).
    /// </summary>
    static class TestJsonSchema
    {
        static readonly HashSet<string> Annotations = new HashSet<string>(StringComparer.Ordinal)
        {
            "$schema", "$id", "$defs", "title", "description", "default", "examples",
        };

        public static List<string> Validate(JsonObject schema, object document)
        {
            var errors = new List<string>();
            Check(schema, schema, document, "(root)", errors);
            return errors;
        }

        static void Check(JsonObject root, object schemaValue, object value, string path, List<string> errors)
        {
            if (schemaValue is bool allowed)
            {
                if (!allowed)
                    errors.Add($"{path}: not allowed");
                return;
            }
            var schema = (JsonObject)schemaValue;
            foreach (var pair in schema)
            {
                switch (pair.Key)
                {
                    case "$ref":
                        Check(root, Resolve(root, (string)pair.Value), value, path, errors);
                        break;
                    case "type":
                    {
                        var types = pair.Value is string single ? new List<string> { single } : ((List<object>)pair.Value).Cast<string>().ToList();
                        if (!types.Any(t => IsType(value, t)))
                            errors.Add($"{path}: expected {string.Join(" or ", types)}");
                        break;
                    }
                    case "const":
                        if (!Equal(pair.Value, value))
                            errors.Add($"{path}: expected {Json.Write(pair.Value)}");
                        break;
                    case "enum":
                        if (!((List<object>)pair.Value).Any(v => Equal(v, value)))
                            errors.Add($"{path}: not one of {Json.Write(pair.Value)}");
                        break;
                    case "required":
                        if (value is JsonObject required)
                            foreach (var key in ((List<object>)pair.Value).Cast<string>())
                                if (!required.Has(key))
                                    errors.Add($"{path}: missing '{key}'");
                        break;
                    case "properties":
                        if (value is JsonObject obj)
                            foreach (var property in (JsonObject)pair.Value)
                                if (obj.Has(property.Key))
                                    Check(root, property.Value, obj.Get(property.Key), path + "/" + property.Key, errors);
                        break;
                    case "additionalProperties":
                        if (value is JsonObject extra)
                        {
                            var known = schema.Get("properties") as JsonObject ?? new JsonObject();
                            foreach (var property in extra)
                            {
                                if (known.Has(property.Key))
                                    continue;
                                Check(root, pair.Value, property.Value, path + "/" + property.Key, errors);
                            }
                        }
                        break;
                    case "items":
                        if (value is List<object> list)
                            for (int i = 0; i < list.Count; i++)
                                Check(root, pair.Value, list[i], path + "/" + i, errors);
                        break;
                    case "minItems":
                        if (value is List<object> few && few.Count < Convert.ToInt64(pair.Value))
                            errors.Add($"{path}: fewer than {pair.Value} items");
                        break;
                    case "maxItems":
                        if (value is List<object> many && many.Count > Convert.ToInt64(pair.Value))
                            errors.Add($"{path}: more than {pair.Value} items");
                        break;
                    case "pattern":
                        if (value is string text && !Regex.IsMatch(text, (string)pair.Value))
                            errors.Add($"{path}: '{text}' does not match {pair.Value}");
                        break;
                    case "minLength":
                        if (value is string shortText && shortText.Length < Convert.ToInt64(pair.Value))
                            errors.Add($"{path}: shorter than {pair.Value}");
                        break;
                    case "minimum":
                        if (IsNumber(value) && Convert.ToDouble(value) < Convert.ToDouble(pair.Value))
                            errors.Add($"{path}: below {pair.Value}");
                        break;
                    case "exclusiveMinimum":
                        if (IsNumber(value) && Convert.ToDouble(value) <= Convert.ToDouble(pair.Value))
                            errors.Add($"{path}: not above {pair.Value}");
                        break;
                    case "maximum":
                        if (IsNumber(value) && Convert.ToDouble(value) > Convert.ToDouble(pair.Value))
                            errors.Add($"{path}: above {pair.Value}");
                        break;
                    case "allOf":
                        foreach (var part in (List<object>)pair.Value)
                            Check(root, part, value, path, errors);
                        break;
                    case "anyOf":
                        if (!((List<object>)pair.Value).Any(part => Passes(root, part, value, path)))
                            errors.Add($"{path}: matches none of anyOf");
                        break;
                    case "oneOf":
                        if (((List<object>)pair.Value).Count(part => Passes(root, part, value, path)) != 1)
                            errors.Add($"{path}: does not match exactly one of oneOf");
                        break;
                    case "if":
                        if (Passes(root, pair.Value, value, path))
                        {
                            if (schema.Has("then"))
                                Check(root, schema.Get("then"), value, path, errors);
                        }
                        else if (schema.Has("else"))
                        {
                            Check(root, schema.Get("else"), value, path, errors);
                        }
                        break;
                    case "then":
                    case "else":
                        break;
                    default:
                        if (!Annotations.Contains(pair.Key))
                            errors.Add($"{path}: the test validator does not know the keyword '{pair.Key}'");
                        break;
                }
            }
        }

        static bool Passes(JsonObject root, object schema, object value, string path)
        {
            var errors = new List<string>();
            Check(root, schema, value, path, errors);
            return errors.Count == 0;
        }

        static object Resolve(JsonObject root, string reference)
        {
            const string prefix = "#/$defs/";
            if (!reference.StartsWith(prefix, StringComparison.Ordinal))
                throw new NotSupportedException("only #/$defs/ references are supported: " + reference);
            var defs = (JsonObject)root.Get("$defs");
            var target = defs.Get(reference.Substring(prefix.Length));
            return target ?? throw new KeyNotFoundException(reference);
        }

        static bool IsNumber(object value) => value is long || value is double;

        static bool IsType(object value, string type)
        {
            switch (type)
            {
                case "object": return value is JsonObject;
                case "array": return value is List<object>;
                case "string": return value is string;
                case "boolean": return value is bool;
                case "null": return value == null;
                case "number": return IsNumber(value);
                case "integer": return value is long || value is double d && Math.Abs(d - Math.Round(d)) < 1e-12;
                default: throw new NotSupportedException("type " + type);
            }
        }

        static bool Equal(object a, object b)
        {
            if (IsNumber(a) && IsNumber(b))
                return Convert.ToDouble(a) == Convert.ToDouble(b);
            return Json.Write(a) == Json.Write(b);
        }
    }
}
