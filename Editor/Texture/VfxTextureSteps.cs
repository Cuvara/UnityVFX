using System;
using System.Collections.Generic;
using System.Linq;

namespace Cuvara.VFX.Editor
{
    /// <summary>
    /// The step names a <c>transform</c> of <c>import-texture</c> may use, with their parameters, as
    /// printed by <c>catalog</c> and checked by <see cref="VfxTextureImport"/>.
    /// </summary>
    /// <remarks>
    /// The list follows <c>Documentation~/DESIGN.md</c>, section "Texture import". The reference with
    /// examples is in <c>Documentation~/AUTHORING.md</c>, section "Texture import"; a change here changes it.
    /// </remarks>
    public static class VfxTextureSteps
    {
        /// <summary>One step: its name and its required and optional parameters (besides <c>step</c>).</summary>
        public sealed class Spec
        {
            internal Spec(string name, string[] required, string[] optional, string summary)
            {
                Name = name;
                Required = required;
                Optional = optional;
                Summary = summary;
            }

            /// <summary>The step name.</summary>
            public string Name { get; }

            /// <summary>Parameters the step must have.</summary>
            public IReadOnlyList<string> Required { get; }

            /// <summary>Parameters the step may have.</summary>
            public IReadOnlyList<string> Optional { get; }

            /// <summary>One line on what the step does.</summary>
            public string Summary { get; }

            /// <summary>Every parameter the step accepts, <c>step</c> included.</summary>
            public IEnumerable<string> AllFields => new[] { "step" }.Concat(Required).Concat(Optional);
        }

        /// <summary>Values of the <c>color</c> parameter of <c>luminance_to_alpha</c>.</summary>
        public static readonly IReadOnlyList<string> ColorModes = new[] { "keep", "white", "unpremultiply" };

        /// <summary>Channels a <c>pack_channels</c> source can read.</summary>
        public static readonly IReadOnlyList<string> Channels = new[] { "r", "g", "b", "a", "luminance" };

        /// <summary>Fields of a <c>pack_channels</c> source object.</summary>
        public static readonly IReadOnlyList<string> SourceFields = new[] { "file", "channel" };

        /// <summary>Values of the <c>u</c>, <c>v</c> and <c>mode</c> parameters of <c>wrap</c>.</summary>
        public static readonly IReadOnlyList<string> WrapModes = new[] { "repeat", "clamp" };

        /// <summary>The largest texture size, also the default <c>max_size</c> of <c>resize_pot</c>.</summary>
        public const int MaxSize = 16384;

        /// <summary>The steps, in the order a transform usually uses them.</summary>
        public static readonly IReadOnlyList<Spec> Specs = new[]
        {
            new Spec("luminance_to_alpha", Array.Empty<string>(), new[] { "color" },
                "alpha = Rec. 709 luminance of RGB (integer weights 2126, 7152, 722 per 10000, rounded); color: keep (default), white, unpremultiply"),
            new Spec("premultiply", Array.Empty<string>(), Array.Empty<string>(),
                "RGB = RGB * A / 255, rounded to nearest"),
            new Spec("pack_channels", Array.Empty<string>(), new[] { "r", "g", "b", "a" },
                "each named channel from a constant in [0, 1] or {file (optional, default: the image before this step), channel: r, g, b, a or luminance}; unnamed channels are kept"),
            new Spec("resize_pot", Array.Empty<string>(), new[] { "max_size" },
                "each axis to its nearest power of two, then both halved until neither exceeds max_size (a power of two, default 16384); area average in integer arithmetic"),
            new Spec("flipbook", new[] { "columns", "rows" }, Array.Empty<string>(),
                "records the frame grid; the output width must divide by columns and the height by rows"),
            new Spec("wrap", Array.Empty<string>(), new[] { "u", "v", "mode" },
                "importer wrap mode per axis: repeat or clamp (mode sets both; an axis not named stays clamp)"),
            new Spec("linear", Array.Empty<string>(), Array.Empty<string>(),
                "imports the texture as linear data (sRGB off): masks, noise, flow maps"),
            new Spec("srgb", Array.Empty<string>(), Array.Empty<string>(),
                "imports the texture as sRGB colour (the default)"),
        };

        /// <summary>Step names, in the order a transform usually uses them.</summary>
        public static readonly IReadOnlyList<string> Names = Specs.Select(s => s.Name).ToList();

        /// <summary>The spec of a step, or null.</summary>
        public static Spec Find(string name) => Specs.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
    }
}
