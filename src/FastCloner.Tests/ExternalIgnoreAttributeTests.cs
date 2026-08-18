using FastCloner.Code;
using FastCloner.SourceGenerator.Shared;

// Assembly-level registration recognized by BOTH cloners:
// - reflection resolves it from the declaring assembly's attributes,
// - the source generator reads the same attribute at compile time.
[assembly: FastClonerExternalIgnore(typeof(FastCloner.Tests.ExternalIgnoreTestAttribute), typeof(FastCloner.Tests.ExternalIgnoreBaseAttribute))]

namespace FastCloner.Tests;

/// <summary>
/// Stands in for framework attributes such as JsonIgnoreAttribute / BsonIgnoreAttribute / NotMappedAttribute.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Event)]
public sealed class ExternalIgnoreTestAttribute : Attribute
{
}

/// <summary>
/// Base attribute used to verify that registrations match derived attribute types.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Event)]
public class ExternalIgnoreBaseAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Event)]
public sealed class ExternalIgnoreDerivedAttribute : ExternalIgnoreBaseAttribute
{
}

/// <summary>
/// Not registered anywhere - carrying this attribute must not change cloning.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class UnrelatedMarkerAttribute : Attribute
{
}

/// <summary>
/// Tests for external ignore attribute registration via
/// <c>[assembly: FastClonerExternalIgnore(...)]</c>.
/// </summary>
public class ExternalIgnoreAttributeTests
{
    #region Test models

    public class ExternalIgnoreInner
    {
        public string Value { get; set; } = "";
    }

    public class ReflectionDto
    {
        public string Name { get; set; } = "";

        [ExternalIgnoreTest]
        public string Secret { get; set; } = "";

        [ExternalIgnoreTest]
        public string SecretField = "";

        [UnrelatedMarker]
        public string Unmarked { get; set; } = "";

        [FastClonerBehavior(CloneBehavior.Clone)]
        [ExternalIgnoreTest]
        public string ExplicitlyKept { get; set; } = "";

        [ExternalIgnoreDerived]
        public string DerivedMarked { get; set; } = "";

        public ExternalIgnoreInner? Inner { get; set; }
    }

    [FastClonerClonable]
    public class SourceGenDto
    {
        public string Name { get; set; } = "";

        [ExternalIgnoreTest]
        public string Secret { get; set; } = "";

        [ExternalIgnoreTest]
        public string SecretField = "";

        [UnrelatedMarker]
        public string Unmarked { get; set; } = "";

        [FastClonerBehavior(CloneBehavior.Clone)]
        [ExternalIgnoreTest]
        public string ExplicitlyKept { get; set; } = "";

        [ExternalIgnoreDerived]
        public string DerivedMarked { get; set; } = "";

        public ExternalIgnoreInner? Inner { get; set; }
    }

    #endregion

    private static ReflectionDto MakeReflectionDto() => new()
    {
        Name = "n",
        Secret = "top-secret",
        SecretField = "field-secret",
        Unmarked = "u",
        ExplicitlyKept = "kept",
        DerivedMarked = "derived",
        Inner = new ExternalIgnoreInner { Value = "v" }
    };

    private static SourceGenDto MakeSourceGenDto() => new()
    {
        Name = "n",
        Secret = "top-secret",
        SecretField = "field-secret",
        Unmarked = "u",
        ExplicitlyKept = "kept",
        DerivedMarked = "derived",
        Inner = new ExternalIgnoreInner { Value = "v" }
    };

    [Test]
    public async Task Reflection_RegisteredAttributeCausesIgnore()
    {
        ReflectionDto original = MakeReflectionDto();

        ReflectionDto clone = original.DeepClone();

        // Members carrying a registered attribute are set to default
        await Assert.That(clone.Secret).IsNull();
        await Assert.That(clone.SecretField).IsNull();
        await Assert.That(clone.DerivedMarked).IsNull();

        // Everything else is cloned normally
        await Assert.That(clone.Name).IsEqualTo("n");
        await Assert.That(clone.Unmarked).IsEqualTo("u");
        await Assert.That(clone.Inner).IsNotNull();
        await Assert.That(clone.Inner!.Value).IsEqualTo("v");
        await Assert.That(clone.Inner).IsNotSameReferenceAs(original.Inner);
    }

    [Test]
    public async Task Reflection_ExplicitFastClonerBehaviorWinsOverRegisteredAttribute()
    {
        ReflectionDto original = MakeReflectionDto();

        ReflectionDto clone = original.DeepClone();

        await Assert.That(clone.ExplicitlyKept).IsEqualTo("kept");
    }

    [Test]
    [NotInParallel]
    public async Task Reflection_DisableOptionalFeatures_DisablesRegisteredIgnore()
    {
        try
        {
            FastCloner.SetDisableOptionalFeatures(true);

            ReflectionDto original = MakeReflectionDto();
            ReflectionDto clone = original.DeepClone();

            await Assert.That(clone.Secret).IsEqualTo("top-secret");
        }
        finally
        {
            FastCloner.SetDisableOptionalFeatures(false);
        }
    }

    [Test]
    public async Task SourceGenerator_RegisteredAttributeCausesIgnore()
    {
        SourceGenDto original = MakeSourceGenDto();

        SourceGenDto clone = original.FastDeepClone();

        // Ignored members are never assigned by the generated cloner. Unlike the
        // reflection cloner (which explicitly resets to default), they keep whatever
        // the member initializer set - identical to how [FastClonerIgnore] behaves
        // under source generation.
        await Assert.That(clone.Secret).IsEqualTo("");
        await Assert.That(clone.SecretField).IsEqualTo("");
        await Assert.That(clone.DerivedMarked).IsEqualTo("");

        await Assert.That(clone.Name).IsEqualTo("n");
        await Assert.That(clone.Unmarked).IsEqualTo("u");
        await Assert.That(clone.ExplicitlyKept).IsEqualTo("kept");
        await Assert.That(clone.Inner).IsNotNull();
        await Assert.That(clone.Inner!.Value).IsEqualTo("v");
        await Assert.That(clone.Inner).IsNotSameReferenceAs(original.Inner);
    }
}
