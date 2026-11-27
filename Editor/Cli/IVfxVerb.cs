namespace Cuvara.VFX.Editor
{
    /// <summary>
    /// One verb of the agent interface (<see cref="VfxCli.Run"/>).
    /// </summary>
    /// <remarks>
    /// Add a verb with one file under <c>Editor/Verbs/</c>: a non-abstract class with a public
    /// parameterless constructor that implements this interface. <see cref="VfxVerbRegistry"/> finds it
    /// through the <c>TypeCache</c>; no list has to be edited. A verb reports failures with
    /// <see cref="VfxResult.Error"/>; an exception it throws also becomes an error of the result.
    /// </remarks>
    public interface IVfxVerb
    {
        /// <summary>The verb name agents call, lower case with hyphens (<c>catalog</c>, <c>import-texture</c>).</summary>
        string Name { get; }

        /// <summary>One line for the list of verbs.</summary>
        string Summary { get; }

        /// <summary>Runs the verb: reads <paramref name="args"/> and writes errors, warnings and payload into <paramref name="result"/>.</summary>
        void Execute(VfxArgs args, VfxResult result);
    }
}
