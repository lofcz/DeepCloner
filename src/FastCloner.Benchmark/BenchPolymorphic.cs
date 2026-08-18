using BenchmarkDotNet.Attributes;
using FastCloner.SourceGenerator.Shared;

namespace FastCloner.Benchmark;

/// <summary>
/// Guards the zero-cost contract of [FastClonerPolymorphic]:
/// cloning the exact root type must cost the same as cloning a plain clonable
/// type of identical shape (plus a single well-predicted type check),
/// and subtype dispatch must add only the type comparisons, no allocations.
/// </summary>
[MemoryDiagnoser]
public class BenchPolymorphic
{
    private PlainDevice _plainDevice = null!;
    private PolyDevice _polyRootInstance = null!;
    private PolyDevice _polySubtypeInstance = null!;

    [GlobalSetup]
    public void Setup()
    {
        _plainDevice = new PlainDevice
        {
            Name = "Plain",
            SerialNumber = 42,
            Tags = ["a", "b", "c", "d", "e"]
        };

        _polyRootInstance = new PolyDevice
        {
            Name = "Root",
            SerialNumber = 42,
            Tags = ["a", "b", "c", "d", "e"]
        };

        _polySubtypeInstance = new PolyPhone
        {
            Name = "Phone",
            SerialNumber = 42,
            Tags = ["a", "b", "c", "d", "e"],
            OperatingSystem = "Android"
        };
    }

    [Benchmark(Baseline = true)]
    public PlainDevice PlainClone() => _plainDevice.FastDeepClone()!;

    [Benchmark]
    public PolyDevice PolymorphicRoot_ExactType() => _polyRootInstance.FastDeepClone()!;

    [Benchmark]
    public PolyDevice PolymorphicRoot_SubtypeDispatch() => _polySubtypeInstance.FastDeepClone()!;
}

[FastClonerClonable]
public class PlainDevice
{
    public string Name { get; set; } = string.Empty;
    public int SerialNumber { get; set; }
    public List<string> Tags { get; set; } = [];
}

[FastClonerClonable]
[FastClonerPolymorphic]
public class PolyDevice
{
    public string Name { get; set; } = string.Empty;
    public int SerialNumber { get; set; }
    public List<string> Tags { get; set; } = [];
}

public class PolyPhone : PolyDevice
{
    public string OperatingSystem { get; set; } = string.Empty;
}

public class PolyTablet : PolyDevice
{
    public bool HasStylus { get; set; }
}
