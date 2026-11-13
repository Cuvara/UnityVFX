using System.Runtime.CompilerServices;

// The EditMode tests reach the verb registry and the JSON helpers without widening the public API.
[assembly: InternalsVisibleTo("Cuvara.VFX.Editor.Tests")]
