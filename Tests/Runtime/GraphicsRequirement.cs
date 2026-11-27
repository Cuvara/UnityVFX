using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cuvara.VFX.Tests
{
    /// <summary>
    /// The guard of the tests that simulate an effect on the GPU or read a render target back
    /// (Documentation~/CI.md, GPU tests). VFX Graph simulates particles with compute shaders, so such a
    /// test can only run on a real GPU. Every such test carries <c>[Category(GraphicsRequirement.Category)]</c>
    /// and calls <see cref="IgnoreUnlessGpuSimulationRuns"/> as its first statement, before it enters
    /// play mode or sets up a scene: where VFX Graph cannot simulate, the test is reported as Ignored
    /// with the reason, never as a pass, and never reaches <c>VFXManager.Update</c>.
    /// </summary>
    /// <remarks>
    /// <para>A test cannot run here when:</para>
    /// <list type="bullet">
    /// <item>the environment variable <c>CUVARA_VFX_SKIP_GPU_TESTS</c> is <c>1</c> or <c>true</c>
    /// (any case): forces the skip, for a headless or CI run that wants it whatever the device;</item>
    /// <item>the editor or player has no graphics device (<see cref="SystemInfo.graphicsDeviceType"/>
    /// is <see cref="GraphicsDeviceType.Null"/>, for example under <c>-nographics</c>);</item>
    /// <item>the graphics device reports no compute shader support;</item>
    /// <item>the graphics device is a software rasteriser (Mesa llvmpipe, softpipe or lavapipe, Google
    /// SwiftShader, the Microsoft Basic Render Driver), matched case-insensitively in
    /// <see cref="SystemInfo.graphicsDeviceName"/> and <see cref="SystemInfo.graphicsDeviceVendor"/>.
    /// llvmpipe reports compute shader support, but VFX Graph crashes the editor natively in
    /// <c>VFX::DispatchCompute</c> on it (a GitHub-hosted Ubuntu runner has no GPU and runs Mesa
    /// llvmpipe).</item>
    /// </list>
    /// </remarks>
    public static class GraphicsRequirement
    {
        /// <summary>The NUnit category of every test that needs VFX Graph GPU simulation or a capture.</summary>
        public const string Category = "GPU";

        /// <summary>The environment variable that forces the skip when it is <c>1</c> or <c>true</c>.</summary>
        public const string SkipVariable = "CUVARA_VFX_SKIP_GPU_TESTS";

        static readonly string[] SoftwareRasterisers =
        {
            "llvmpipe",
            "softpipe",
            "lavapipe",
            "SwiftShader",
            "Microsoft Basic Render",
        };

        /// <summary>The reason a GPU test cannot run here, or null when it can.</summary>
        public static string MissingReason()
        {
            var device = SystemInfo.graphicsDeviceType;
            var name = SystemInfo.graphicsDeviceName;
            var vendor = SystemInfo.graphicsDeviceVendor;
            var describe = $"graphics device {device} '{name}' (vendor '{vendor}')";

            var skip = Environment.GetEnvironmentVariable(SkipVariable);
            if (skip != null && (skip.Trim() == "1" || string.Equals(skip.Trim(), "true", StringComparison.OrdinalIgnoreCase)))
                return $"skipped on request: the environment variable {SkipVariable} is '{skip}' ({describe}); " +
                       $"the {Category} tests run only where VFX Graph can simulate on a GPU";
            if (device == GraphicsDeviceType.Null)
                return $"needs a GPU: the {describe} is no graphics device (SystemInfo.graphicsDeviceType is Null, for example under -nographics), " +
                       "so VFX Graph cannot simulate the effect nor render a frame";
            if (!SystemInfo.supportsComputeShaders)
                return $"needs compute shaders: the {describe} does not support them, " +
                       "and VFX Graph simulates particles with compute shaders";
            var rasteriser = SoftwareRasteriser(name, vendor);
            if (rasteriser != null)
                return $"needs a GPU: the {describe} is the software rasteriser {rasteriser}, " +
                       "on which VFX Graph compute simulation crashes or yields no particles (for example a GitHub-hosted runner without a GPU)";
            return null;
        }

        /// <summary>Ends the calling test as ignored, with the reason, when it cannot run here.</summary>
        public static void IgnoreUnlessGpuSimulationRuns()
        {
            var reason = MissingReason();
            if (reason != null)
                Assert.Ignore(reason);
        }

        /// <summary>The software rasteriser named by the device name or vendor, or null.</summary>
        static string SoftwareRasteriser(string name, string vendor)
        {
            foreach (var rasteriser in SoftwareRasterisers)
            {
                if ((name != null && name.IndexOf(rasteriser, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (vendor != null && vendor.IndexOf(rasteriser, StringComparison.OrdinalIgnoreCase) >= 0))
                    return rasteriser;
            }
            return null;
        }
    }
}
