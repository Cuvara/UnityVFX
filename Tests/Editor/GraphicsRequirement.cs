using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cuvara.VFX.Editor.Tests
{
    /// <summary>
    /// The guard of the tests that simulate an effect on the GPU or read a render target back
    /// (Documentation~/CI.md, GPU tests). VFX Graph simulates particles with compute shaders; without a
    /// graphics device (<c>-nographics</c>, a hosted runner with no GPU) or without compute shader
    /// support a capture cannot run, so such a test is reported as ignored with the reason, never as a
    /// pass.
    /// </summary>
    static class GraphicsRequirement
    {
        /// <summary>The reason a GPU test cannot run here, or null when it can.</summary>
        public static string MissingReason()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return "needs a GPU: the editor has no graphics device (SystemInfo.graphicsDeviceType is Null, for example under -nographics), " +
                       "so VFX Graph cannot simulate the effect nor render a frame";
            if (!SystemInfo.supportsComputeShaders)
                return $"needs compute shaders: the graphics device {SystemInfo.graphicsDeviceType} ({SystemInfo.graphicsDeviceName}) " +
                       "does not support them, and VFX Graph simulates particles with compute shaders";
            return null;
        }

        /// <summary>Ends the calling test as ignored, with the reason, when it cannot run here.</summary>
        public static void IgnoreUnlessGpuSimulationRuns()
        {
            var reason = MissingReason();
            if (reason != null)
                Assert.Ignore(reason);
        }
    }
}
