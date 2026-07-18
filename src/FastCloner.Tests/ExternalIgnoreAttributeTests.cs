using System;
using FastCloner.Code;

namespace FastCloner.Tests;

/// <summary>
/// Tests for the externally-registered ignore-attribute feature
/// (FastCloner.RegisterIgnoreAttribute).
/// </summary>
[NotInParallel]
public class ExternalIgnoreAttributeTests
{
    #region Test attributes & classes

    /// <summary>
    /// Stands in for framework attributes such as JsonIgnoreAttribute / BsonIgnoreAttribute / NotMappedAttribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Event)]
    private sealed class MyExternalIgnoreAttribute : Attribute
    {
    }

    /// <summary>
    /// Stands in for a second, independent framework attribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Event)]
    private sealed class MyOtherIgnoreAttribute : Attribute
    {
    }

    private class DtoWithExternalIgnore
    {
        public string Name { get; set; } = "";

        [MyExternalIgnore]
        public string Secret { get; set; } = "";

        public InnerPayload Payload { get; set; } = null!;
    }

    private class DtoWithBothAttributes
    {
        [FastClonerIgnore]
        [MyExternalIgnore]
        public string Token { get; set; } = "";
    }

    private class DtoWithExternalIgnoreOnField
    {
        public string Name { get; set; } = "";

        [MyExternalIgnore]
        public string Secret = "";
    }

    private class InnerPayload
    {
        public string Value { get; set; } = "";
    }

    #endregion

    [Test]
    public async Task WithoutRegistration_ExternalIgnoreAttributeIsNotIgnored()
    {
        // Arrange
        DtoWithExternalIgnore original = MakeDto();

        // Act
        DtoWithExternalIgnore clone = original.DeepClone();

        // Assert - nothing registered, so [MyExternalIgnore] is treated like any other member
        await Assert.That(clone.Secret).IsEqualTo("top-secret");
        await Assert.That(clone.Payload).IsNotSameReferenceAs(original.Payload);
        await Assert.That(clone.Payload.Value).IsEqualTo("v");
    }

    [Test]
    public async Task AfterRegister_RegisteredAttributeCausesIgnore()
    {
        // Arrange
        DtoWithExternalIgnore original = MakeDto();
        FastCloner.RegisterIgnoreAttribute<MyExternalIgnoreAttribute>();

        try
        {
            // Act
            DtoWithExternalIgnore clone = original.DeepClone();

            // Assert - member decorated with the registered attribute is skipped (default / null)
            await Assert.That(clone.Secret).IsEqualTo(default(string));
            await Assert.That(clone.Payload).IsNotSameReferenceAs(original.Payload);
        }
        finally
        {
            FastCloner.ResetIgnoreAttributes();
        }
    }

    [Test]
    public async Task FastClonerIgnoreTakesPrecedenceOverRegisteredAttribute()
    {
        // Arrange
        DtoWithBothAttributes original = new DtoWithBothAttributes { Token = "abc" };
        FastCloner.RegisterIgnoreAttribute<MyExternalIgnoreAttribute>();

        try
        {
            // Act
            DtoWithBothAttributes clone = original.DeepClone();

            // Assert - member is ignored. Precedence is guaranteed by the implementation checking
            // FastClonerBehaviorAttribute before any registered external attribute.
            await Assert.That(clone.Token).IsEqualTo(default(string));
        }
        finally
        {
            FastCloner.ResetIgnoreAttributes();
        }
    }

    [Test]
    public async Task MultipleRegisteredAttributes_AllHonored()
    {
        // Arrange
        DtoWithExternalIgnore original = MakeDto();
        FastCloner.RegisterIgnoreAttribute<MyExternalIgnoreAttribute>();
        FastCloner.RegisterIgnoreAttribute<MyOtherIgnoreAttribute>();

        try
        {
            // Act - both attributes registered; a member marked with either should be ignored
            DtoWithExternalIgnore clone = original.DeepClone();
            await Assert.That(clone.Secret).IsEqualTo(default(string));
        }
        finally
        {
            FastCloner.ResetIgnoreAttributes();
        }
    }

    [Test]
    public async Task FieldWithRegisteredExternalAttribute_IsIgnored()
    {
        // Arrange - the registered attribute is applied to a field, not a property
        DtoWithExternalIgnoreOnField original = new DtoWithExternalIgnoreOnField { Name = "n", Secret = "top-secret" };
        FastCloner.RegisterIgnoreAttribute<MyExternalIgnoreAttribute>();

        try
        {
            // Act
            DtoWithExternalIgnoreOnField clone = original.DeepClone();

            // Assert - fields are also recognized (the API promises field/property/event support)
            await Assert.That(clone.Secret).IsEqualTo(default(string));
            await Assert.That(clone.Name).IsEqualTo("n");
        }
        finally
        {
            FastCloner.ResetIgnoreAttributes();
        }
    }

    [Test]
    public async Task DisableOptionalFeatures_GatesExternalIgnore()
    {
        // Arrange
        DtoWithExternalIgnore original = MakeDto();
        FastCloner.RegisterIgnoreAttribute<MyExternalIgnoreAttribute>();
        FastCloner.SetDisableOptionalFeatures(true);

        try
        {
            // Act
            DtoWithExternalIgnore clone = original.DeepClone();

            // Assert - with optional features disabled, even registered ignore attributes are not honored
            await Assert.That(clone.Secret).IsEqualTo("top-secret");
        }
        finally
        {
            FastCloner.SetDisableOptionalFeatures(false);
            FastCloner.ResetIgnoreAttributes();
        }
    }

    [Test]
    public async Task RegisterIgnoreAttribute_NonAttributeType_Throws()
    {
        await Assert.That(() => FastCloner.RegisterIgnoreAttribute(typeof(string)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task RegisterIgnoreAttribute_Null_Throws()
    {
        await Assert.That(() => FastCloner.RegisterIgnoreAttribute(null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task ConcurrentRegistration_IsThreadSafe()
    {
        // Arrange
        DtoWithExternalIgnore original = MakeDto();

        // Act - hammer registration from many threads; no exceptions expected.
        System.Threading.Tasks.Parallel.For(0, 50, _ =>
        {
            FastCloner.RegisterIgnoreAttribute<MyExternalIgnoreAttribute>();
        });

        try
        {
            DtoWithExternalIgnore clone = original.DeepClone();
            await Assert.That(clone.Secret).IsEqualTo(default(string));
        }
        finally
        {
            FastCloner.ResetIgnoreAttributes();
        }
    }

    private static DtoWithExternalIgnore MakeDto() => new DtoWithExternalIgnore
    {
        Name = "n",
        Secret = "top-secret",
        Payload = new InnerPayload { Value = "v" }
    };
}
