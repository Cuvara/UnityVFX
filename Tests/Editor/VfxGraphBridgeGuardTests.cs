using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Cuvara.VFX.Editor.Bridge;
using NUnit.Framework;

namespace Cuvara.VFX.Editor.Tests
{
    /// <summary>
    /// The internals guard: the bridge compiles against the VFX Graph internals under the one assembly
    /// name the package lets in, holds nothing but <see cref="VfxGraphBridge"/>, exposes no internal type,
    /// and its version check matches the installed package.
    /// </summary>
    public sealed class VfxGraphBridgeGuardTests
    {
        const string BridgeAssemblyName = "Unity.Testing.VisualEffectGraph.Editor";
        const string VfxEditorAssemblyName = "Unity.VisualEffectGraph.Editor";

        // By name, so this test assembly does not reference Unity.VisualEffectGraph.Editor itself.
        static Assembly VfxEditorAssembly =>
            Type.GetType("UnityEditor.VFX.VFXGraph, " + VfxEditorAssemblyName, throwOnError: true).Assembly;

        [Test]
        public void BridgeAssembly_HasTheNameTheVfxPackageLetsIn()
        {
            Assert.That(typeof(VfxGraphBridge).Assembly.GetName().Name, Is.EqualTo(BridgeAssemblyName));
            var friends = VfxEditorAssembly.GetCustomAttributes<InternalsVisibleToAttribute>().Select(a => a.AssemblyName).ToList();
            Assert.That(friends, Does.Contain(BridgeAssemblyName));
        }

        [Test]
        public void BridgeAssembly_ReferencesTheVfxEditorAssembly()
        {
            var references = typeof(VfxGraphBridge).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
            Assert.That(references, Does.Contain(VfxEditorAssemblyName));
        }

        [Test]
        public void BridgeAssembly_ContainsOnlyVfxGraphBridge()
        {
            var topLevel = typeof(VfxGraphBridge).Assembly.GetTypes()
                .Where(t => !t.IsNested && !t.Name.StartsWith("<", StringComparison.Ordinal))
                .Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() == null)
                .Select(t => t.FullName)
                .ToList();
            Assert.That(topLevel, Is.EquivalentTo(new[] { typeof(VfxGraphBridge).FullName }));
        }

        [Test]
        public void BridgePublicApi_ExposesNoVfxEditorType()
        {
            var vfxEditor = VfxEditorAssembly;
            var offenders = new List<string>();
            foreach (var type in PublicTypes(typeof(VfxGraphBridge)))
            {
                foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    foreach (var used in TypesOf(member))
                        if (Expand(used).Any(t => t.Assembly == vfxEditor))
                            offenders.Add($"{type.Name}.{member.Name}: {used}");
                }
                if (type.BaseType != null && Expand(type.BaseType).Any(t => t.Assembly == vfxEditor))
                    offenders.Add($"{type.Name} derives from {type.BaseType}");
            }
            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void NoOtherPackageAssembly_ReferencesTheVfxEditorAssembly()
        {
            foreach (var assembly in new[] { typeof(VfxCli).Assembly, typeof(Cuvara.VFX.VfxLibrary).Assembly })
            {
                var references = assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
                Assert.That(references, Does.Not.Contain(VfxEditorAssemblyName), assembly.GetName().Name);
            }
        }

        [Test]
        public void VersionGuard_MatchesTheInstalledPackage()
        {
            var installed = UnityEditor.PackageManager.PackageInfo.FindForAssembly(VfxEditorAssembly)?.version;
            Assert.That(installed, Is.Not.Null.And.Not.Empty);
            Assert.That(VfxGraphBridge.InstalledVersion, Is.EqualTo(installed));
            Assert.That(VfxGraphBridge.MajorMinor(installed), Is.EqualTo(VfxGraphBridge.WrittenForMajorMinor));
            Assert.That(VfxGraphBridge.IsSupportedVersion(out var message), Is.True, message);
            Assert.DoesNotThrow(VfxGraphBridge.RequireSupportedVersion);
        }

        [Test]
        public void MajorMinor_IgnoresPatchAndPreRelease()
        {
            Assert.That(VfxGraphBridge.MajorMinor("17.6.0"), Is.EqualTo("17.6"));
            Assert.That(VfxGraphBridge.MajorMinor("17.6.12-pre.1"), Is.EqualTo("17.6"));
            Assert.That(VfxGraphBridge.MajorMinor("17.7.0"), Is.Not.EqualTo(VfxGraphBridge.WrittenForMajorMinor));
            Assert.That(VfxGraphBridge.MajorMinor(null), Is.Empty);
        }

        static IEnumerable<Type> PublicTypes(Type root)
        {
            yield return root;
            foreach (var nested in root.GetNestedTypes(BindingFlags.Public))
                foreach (var inner in PublicTypes(nested))
                    yield return inner;
        }

        static IEnumerable<Type> TypesOf(MemberInfo member)
        {
            switch (member)
            {
                case FieldInfo field:
                    yield return field.FieldType;
                    break;
                case PropertyInfo property:
                    yield return property.PropertyType;
                    foreach (var p in property.GetIndexParameters())
                        yield return p.ParameterType;
                    break;
                case MethodInfo method:
                    yield return method.ReturnType;
                    foreach (var p in method.GetParameters())
                        yield return p.ParameterType;
                    break;
                case ConstructorInfo constructor:
                    foreach (var p in constructor.GetParameters())
                        yield return p.ParameterType;
                    break;
                case EventInfo e:
                    yield return e.EventHandlerType;
                    break;
            }
        }

        static IEnumerable<Type> Expand(Type type)
        {
            if (type == null)
                yield break;
            if (type.HasElementType)
            {
                foreach (var inner in Expand(type.GetElementType()))
                    yield return inner;
                yield break;
            }
            yield return type;
            if (type.IsGenericType)
                foreach (var argument in type.GetGenericArguments())
                    foreach (var inner in Expand(argument))
                        yield return inner;
        }
    }
}
