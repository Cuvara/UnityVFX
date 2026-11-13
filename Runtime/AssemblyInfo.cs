using System.Runtime.CompilerServices;

// The PlayMode tests reach the internal seams (ChainLightningBinder.Apply with a fake property sink,
// VfxPlayer pool counters) without making them part of the public API.
[assembly: InternalsVisibleTo("Cuvara.VFX.Tests")]
