using System.Collections.Generic;
using System.Threading.Tasks;
using FastCloner.SourceGenerator.Shared;

namespace FastCloner.Tests;

// External derived type (simulating a subtype from another assembly): registered via [FastClonerInclude].
public class ExternalSmartwatch : PolymorphicCloneTests.IncludedGadget
{
    public string? FirmwareVersion { get; set; }
}

[SourceGeneratorCompatible]
public class PolymorphicCloneTests
{
    #region Test Classes

    [FastClonerClonable]
    [FastClonerPolymorphic]
    public class Device
    {
        public string? Name { get; set; }
        public int SerialNumber { get; set; }
    }

    public class Phone : Device
    {
        public string? OperatingSystem { get; set; }
    }

    public class Smartwatch : Device
    {
        public bool HasHeartRateSensor { get; set; }
    }

    // Multi-level hierarchy: SatellitePhone must never be sliced into a Phone clone.
    public class SatellitePhone : Phone
    {
        public double FrequencyMhz { get; set; }
    }

    // Default behavior without [FastClonerPolymorphic] must stay unchanged.
    [FastClonerClonable]
    public class PlainDevice
    {
        public string? Name { get; set; }
    }

    public class PlainPhone : PlainDevice
    {
        public string? OperatingSystem { get; set; }
    }

    // Explicit registration only: auto-discovery disabled, external subtype included.
    [FastClonerClonable]
    [FastClonerPolymorphic]
    [FastClonerDisableAutoDiscovery]
    [FastClonerInclude(typeof(ExternalSmartwatch))]
    public class IncludedGadget
    {
        public string? Name { get; set; }
    }

    // Not registered anywhere (auto-discovery is disabled on its base): unknown subtype at runtime.
    public class UndiscoveredGadget : IncludedGadget
    {
        public string? SecretFeature { get; set; }
    }

    // Subtype with its own [FastClonerClonable]: dispatch must go through its generated extension.
    [FastClonerClonable]
    [FastClonerPolymorphic]
    public class MediaHub
    {
        public string? Name { get; set; }
    }

    [FastClonerClonable]
    public class Tv : MediaHub
    {
        public int DiagonalInches { get; set; }
    }

    // Dispatch must flow through member cloning of another clonable type.
    [FastClonerClonable]
    public class DeviceHolder
    {
        public Device? Favorite { get; set; }
        public List<Device>? All { get; set; }
    }

    #endregion

    #region Test Classes - Generic roots

    [FastClonerClonable]
    [FastClonerPolymorphic]
    [FastClonerInclude(typeof(TypedIntRepo))]
    public class Repo<T>
    {
        public string? Name { get; set; }
        public T? Item { get; set; }
    }

    public class StringRepo : Repo<string>
    {
        public int Extra { get; set; }
    }

    public class IntRepo : Repo<int>
    {
        public bool Doubled { get; set; }
    }

    public class CachedStringRepo : StringRepo
    {
        public int CacheSize { get; set; }
    }

    [FastClonerClonable]
    public class GuidRepo : Repo<Guid>
    {
        public DateTime Created { get; set; }
    }

    // Generic subtype: auto-discovery skips its open declaration; the closed construction
    // used below is registered via [FastClonerInclude(typeof(TypedIntRepo))] on the root.
    public class TypedIntRepo : Repo<List<int>>
    {
        public bool IsReadOnly { get; set; }
    }

    // Generic subtype with NO [FastClonerInclude]: its closed constructions are discovered
    // automatically from usages, exactly like generic clonable type arguments are.
    public class AutoTypedRepo<U> : Repo<List<U>>
    {
        public bool AutoFlag { get; set; }
    }

    [FastClonerClonable]
    public abstract class Registry<T>
    {
        public string? Name { get; set; }
        public T? Value { get; set; }
    }

    public class StringRegistry : Registry<string>
    {
        public DateTime RegisteredAt { get; set; }
    }

    #endregion

    #region Tests - Runtime type preservation

    [Test]
    [SourceGeneratorCompatible]
    public async Task PolymorphicRoot_Should_Preserve_RuntimeType_Of_Subtype()
    {
        // Arrange
        Phone phone = new Phone
        {
            Name = "MyPhone",
            SerialNumber = 42,
            OperatingSystem = "Android"
        };

        // Act - clone through the base type reference
        Device device = phone;
        Device? clone = device.FastDeepClone();

        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<Phone>();
        await Assert.That(clone).IsNotSameReferenceAs(phone);

        Phone clonedPhone = (Phone)clone!;
        await Assert.That(clonedPhone.Name).IsEqualTo("MyPhone");
        await Assert.That(clonedPhone.SerialNumber).IsEqualTo(42);
        await Assert.That(clonedPhone.OperatingSystem).IsEqualTo("Android");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task PolymorphicRoot_ExactRootInstance_Should_Clone_Normally()
    {
        // Arrange
        Device device = new Device
        {
            Name = "BaseDevice",
            SerialNumber = 7
        };

        // Act
        Device? clone = device.FastDeepClone();

        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<Device>();
        await Assert.That(clone).IsNotSameReferenceAs(device);
        await Assert.That(clone!.Name).IsEqualTo("BaseDevice");
        await Assert.That(clone.SerialNumber).IsEqualTo(7);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task PolymorphicRoot_MultiLevelHierarchy_Should_Not_Slice()
    {
        // Arrange - SatellitePhone derives from Phone which derives from Device
        SatellitePhone satellitePhone = new SatellitePhone
        {
            Name = "Iridium",
            SerialNumber = 99,
            OperatingSystem = "Proprietary",
            FrequencyMhz = 1616.5
        };

        // Act
        Device device = satellitePhone;
        Device? clone = device.FastDeepClone();

        // Assert - the clone must be a SatellitePhone with every level's members intact
        await Assert.That(clone).IsTypeOf<SatellitePhone>();

        SatellitePhone cloned = (SatellitePhone)clone!;
        await Assert.That(cloned.Name).IsEqualTo("Iridium");
        await Assert.That(cloned.SerialNumber).IsEqualTo(99);
        await Assert.That(cloned.OperatingSystem).IsEqualTo("Proprietary");
        await Assert.That(cloned.FrequencyMhz).IsEqualTo(1616.5);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task PolymorphicRoot_MultipleSubtypes_Should_Dispatch_Each()
    {
        // Arrange
        Device[] devices =
        [
            new Phone { Name = "P", OperatingSystem = "iOS" },
            new Smartwatch { Name = "W", HasHeartRateSensor = true },
            new Device { Name = "D" }
        ];

        // Act + Assert
        Device? phoneClone = ((Device)devices[0]).FastDeepClone();
        await Assert.That(phoneClone).IsTypeOf<Phone>();
        await Assert.That(((Phone)phoneClone!).OperatingSystem).IsEqualTo("iOS");

        Device? watchClone = ((Device)devices[1]).FastDeepClone();
        await Assert.That(watchClone).IsTypeOf<Smartwatch>();
        await Assert.That(((Smartwatch)watchClone!).HasHeartRateSensor).IsTrue();

        Device? rootClone = ((Device)devices[2]).FastDeepClone();
        await Assert.That(rootClone).IsTypeOf<Device>();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task PolymorphicRoot_NullSource_Should_Return_Null()
    {
        // Arrange
        Device? device = null;

        // Act
        Device? clone = device.FastDeepClone();

        // Assert
        await Assert.That(clone).IsNull();
    }

    #endregion

    #region Tests - Default behavior unchanged

    [Test]
    [SourceGeneratorCompatible]
    public async Task WithoutPolymorphicAttribute_DefaultBehavior_Should_Stay_Unchanged()
    {
        // Arrange - PlainDevice has no [FastClonerPolymorphic]: cloning through the base
        // reference produces the base type (pre-existing, documented behavior).
        PlainPhone phone = new PlainPhone
        {
            Name = "LegacyPhone",
            OperatingSystem = "Symbian"
        };

        // Act
        PlainDevice device = phone;
        PlainDevice? clone = device.FastDeepClone();

        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<PlainDevice>();
        await Assert.That(clone is PlainPhone).IsFalse();
        await Assert.That(clone!.Name).IsEqualTo("LegacyPhone");
    }

    #endregion

    #region Tests - Generic roots

    [Test]
    [SourceGeneratorCompatible]
    public async Task GenericRoot_Should_Dispatch_NonGenericSubtype()
    {
        // Arrange
        Repo<string> repo = new StringRepo { Name = "R", Item = "payload", Extra = 7 };

        // Act
        Repo<string>? clone = repo.FastDeepClone();

        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<StringRepo>();
        await Assert.That(clone).IsNotSameReferenceAs(repo);

        StringRepo stringRepo = (StringRepo)clone!;
        await Assert.That(stringRepo.Name).IsEqualTo("R");
        await Assert.That(stringRepo.Item).IsEqualTo("payload");
        await Assert.That(stringRepo.Extra).IsEqualTo(7);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task GenericRoot_ExactRootInstance_Should_Clone_Normally()
    {
        // Arrange
        Repo<string> repo = new Repo<string> { Name = "Plain", Item = "x" };

        // Act
        Repo<string>? clone = repo.FastDeepClone();

        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone!.GetType()).IsEqualTo(typeof(Repo<string>));
        await Assert.That(clone.Name).IsEqualTo("Plain");
        await Assert.That(clone.Item).IsEqualTo("x");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task GenericRoot_ValueTypeArgument_Should_Dispatch()
    {
        // Arrange
        Repo<int> repo = new IntRepo { Name = "I", Item = 5, Doubled = true };

        // Act
        Repo<int>? clone = repo.FastDeepClone();

        // Assert
        await Assert.That(clone).IsTypeOf<IntRepo>();
        IntRepo intRepo = (IntRepo)clone!;
        await Assert.That(intRepo.Item).IsEqualTo(5);
        await Assert.That(intRepo.Doubled).IsTrue();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task GenericRoot_MultiLevelHierarchy_Should_Not_Slice()
    {
        // Arrange
        Repo<string> repo = new CachedStringRepo { Name = "C", Item = "c", Extra = 1, CacheSize = 9 };

        // Act
        Repo<string>? clone = repo.FastDeepClone();

        // Assert
        await Assert.That(clone).IsTypeOf<CachedStringRepo>();
        CachedStringRepo cached = (CachedStringRepo)clone!;
        await Assert.That(cached.CacheSize).IsEqualTo(9);
        await Assert.That(cached.Extra).IsEqualTo(1);
        await Assert.That(cached.Item).IsEqualTo("c");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task GenericRoot_SubtypeWithOwnClonable_Should_Dispatch()
    {
        // Arrange
        GuidRepo guidRepoSource = new GuidRepo { Name = "G", Item = Guid.NewGuid(), Created = DateTime.UtcNow };
        Repo<Guid> repo = guidRepoSource;

        // Act
        Repo<Guid>? clone = repo.FastDeepClone();

        // Assert
        await Assert.That(clone).IsTypeOf<GuidRepo>();
        GuidRepo guidRepo = (GuidRepo)clone!;
        await Assert.That(guidRepo.Item).IsEqualTo(guidRepoSource.Item);
        await Assert.That(guidRepo.Created).IsEqualTo(guidRepoSource.Created);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task GenericRoot_IncludedClosedGenericSubtype_Should_Dispatch_And_DeepCloneMembers()
    {
        // Arrange - TypedIntRepo is a generic subtype; its closed construction is
        // registered via [FastClonerInclude(typeof(TypedIntRepo))] on the root.
        Repo<List<int>> repo = new TypedIntRepo { Name = "T", Item = [1, 2, 3], IsReadOnly = false };

        // Act
        Repo<List<int>>? clone = repo.FastDeepClone();

        // Assert
        await Assert.That(clone).IsTypeOf<TypedIntRepo>();
        TypedIntRepo typed = (TypedIntRepo)clone!;
        await Assert.That(typed.Item).IsNotNull();
        await Assert.That(typed.Item!.Count).IsEqualTo(3);
        await Assert.That(typed.Item).IsNotSameReferenceAs(repo.Item); // deep-cloned, not shared
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task AbstractGenericRoot_Should_Dispatch_ClosedSubtype()
    {
        // Arrange - abstract generic hierarchies discover closed constructions of subtypes
        Registry<string> registry = new StringRegistry { Name = "S", Value = "v", RegisteredAt = DateTime.UtcNow };

        // Act
        Registry<string>? clone = registry.FastDeepClone();

        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<StringRegistry>();
        StringRegistry stringRegistry = (StringRegistry)clone!;
        await Assert.That(stringRegistry.Value).IsEqualTo("v");
        await Assert.That(stringRegistry.Name).IsEqualTo("S");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task GenericRoot_GenericSubtype_Should_Be_AutoDiscovered_FromUsage()
    {
        // Arrange - AutoTypedRepo<U> has no [FastClonerInclude]; the closed construction
        // below is discovered automatically from its usage in this compilation.
        Repo<List<long>> repo = new AutoTypedRepo<long> { Name = "A", Item = [1L, 2L], AutoFlag = true };

        // Act
        Repo<List<long>>? clone = repo.FastDeepClone();

        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<AutoTypedRepo<long>>();

        AutoTypedRepo<long> typed = (AutoTypedRepo<long>)clone!;
        await Assert.That(typed.AutoFlag).IsTrue();
        await Assert.That(typed.Name).IsEqualTo("A");
        await Assert.That(typed.Item).IsNotNull();
        await Assert.That(typed.Item!.Count).IsEqualTo(2);
        await Assert.That(typed.Item).IsNotSameReferenceAs(repo.Item); // deep-cloned, not shared
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task GenericRoot_NullSource_Should_Return_Null()
    {
        // Arrange
        Repo<string>? repo = null;

        // Act
        Repo<string>? clone = repo.FastDeepClone();

        // Assert
        await Assert.That(clone).IsNull();
    }

    #endregion

    #region Tests - Member cloning dispatches through the base reference

    [Test]
    [SourceGeneratorCompatible]
    public async Task MemberCloning_Should_Dispatch_Subtypes_Through_BaseReference()
    {
        // Arrange
        DeviceHolder holder = new DeviceHolder
        {
            Favorite = new Phone { Name = "Fav", SerialNumber = 1, OperatingSystem = "Android" },
            All =
            [
                new Device { Name = "Root", SerialNumber = 2 },
                new Smartwatch { Name = "Watch", SerialNumber = 3, HasHeartRateSensor = true },
                new SatellitePhone { Name = "Sat", SerialNumber = 4, OperatingSystem = "OS", FrequencyMhz = 100.0 }
            ]
        };

        // Act
        DeviceHolder? clone = holder.FastDeepClone();

        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone!.Favorite).IsTypeOf<Phone>();
        await Assert.That(((Phone)clone.Favorite!).OperatingSystem).IsEqualTo("Android");

        await Assert.That(clone.All).IsNotNull();
        await Assert.That(clone.All!.Count).IsEqualTo(3);
        await Assert.That(clone.All[0]).IsTypeOf<Device>();
        await Assert.That(clone.All[1]).IsTypeOf<Smartwatch>();
        await Assert.That(clone.All[2]).IsTypeOf<SatellitePhone>();
        await Assert.That(((SatellitePhone)clone.All[2]).FrequencyMhz).IsEqualTo(100.0);
    }

    #endregion

    #region Tests - Subtypes with their own [FastClonerClonable]

    [Test]
    [SourceGeneratorCompatible]
    public async Task SubtypeWithOwnClonableAttribute_Should_Dispatch_To_Its_Extension()
    {
        // Arrange
        Tv tv = new Tv
        {
            Name = "LivingRoom",
            DiagonalInches = 55
        };

        // Act - clone through the polymorphic root
        MediaHub hub = tv;
        MediaHub? clone = hub.FastDeepClone();

        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<Tv>();
        await Assert.That(((Tv)clone!).DiagonalInches).IsEqualTo(55);
        await Assert.That(clone.Name).IsEqualTo("LivingRoom");

        // And through the subtype's own extension as well
        Tv? directClone = tv.FastDeepClone();
        await Assert.That(directClone).IsNotNull();
        await Assert.That(directClone!.DiagonalInches).IsEqualTo(55);
    }

    #endregion

    #region Tests - Explicit registration and unknown subtypes

    [Test]
    [SourceGeneratorCompatible]
    public async Task IncludedExternalSubtype_WithDisabledAutoDiscovery_Should_Clone()
    {
        // Arrange
        ExternalSmartwatch watch = new ExternalSmartwatch
        {
            Name = "Pixel Watch",
            FirmwareVersion = "1.2.3"
        };

        // Act
        IncludedGadget gadget = watch;
        IncludedGadget? clone = gadget.FastDeepClone();

        // Assert
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<ExternalSmartwatch>();
        await Assert.That(clone).IsNotSameReferenceAs(watch);

        ExternalSmartwatch clonedWatch = (ExternalSmartwatch)clone!;
        await Assert.That(clonedWatch.Name).IsEqualTo("Pixel Watch");
        await Assert.That(clonedWatch.FirmwareVersion).IsEqualTo("1.2.3");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task UnknownSubtype_WithRuntimeAvailable_Should_FallBack_To_RuntimeCloner()
    {
        // Arrange - UndiscoveredGadget derives from IncludedGadget but is not registered
        // (auto-discovery is disabled), so the dispatcher does not know it.
        UndiscoveredGadget gadget = new UndiscoveredGadget
        {
            Name = "Unknown",
            SecretFeature = "classified"
        };

        // Act
        IncludedGadget baseRef = gadget;
        IncludedGadget? clone = baseRef.FastDeepClone();

        // Assert - the runtime fallback must still produce a correct clone of the actual type
        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsTypeOf<UndiscoveredGadget>();
        await Assert.That(clone).IsNotSameReferenceAs(gadget);
        await Assert.That(clone!.Name).IsEqualTo("Unknown");
        await Assert.That(((UndiscoveredGadget)clone).SecretFeature).IsEqualTo("classified");
    }

    #endregion
}
