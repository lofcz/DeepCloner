<div align="center">

<img width="512" alt="FastCloner" src="https://github.com/user-attachments/assets/9b6b82a3-892a-4607-9c57-6580ca856a37" />

# FastCloner

**The fastest and most reliable .NET deep cloning library.**    

[![FastCloner](https://shields.io/nuget/v/FastCloner?v=304&icon=nuget&label=FastCloner)](https://www.nuget.org/packages/FastCloner)
[![FastCloner](https://shields.io/nuget/v/FastCloner.SourceGenerator?v=304&icon=nuget&label=FastCloner.SourceGenerator)](https://www.nuget.org/packages/FastCloner.SourceGenerator)
[![License:MIT](https://img.shields.io/badge/License-MIT-34D058.svg)](https://opensource.org/license/mit)

FastCloner is a zero-dependency deep cloning library for .NET, from <code>.NET 4.6</code> to <code>.NET 10+</code>. It combines source generation with optimized reflection fallback, so deep cloning _just works_.
 
</div>

## ✨ Features

- **The Fastest** - [Benchmarked](https://github.com/lofcz/FastCloner?tab=readme-ov-file#performance) to beat all other libraries with [third-party](https://github.com/FoundatioFx/Foundatio/pull/469#issuecomment-4013424812) [independent](https://github.com/AnderssonPeter/Dolly?tab=readme-ov-file#benchmarks) [benchmarks](https://github.com/arika0093/IDeepCloneable?tab=readme-ov-file#performance) verifying the performance. **300x** speed-up vs `Newtonsoft.Json` and **160x** vs `System.Text.Json`
- **The Most Correct** - Built for the cases clone libraries get wrong: polymorphism, circular/shared references, readonly and immutable members, deep graphs, delegates, events, collections... Backed by [1,000+ tests](https://github.com/lofcz/FastCloner/tree/next/FastCloner.Tests), with documented [limitations](https://github.com/lofcz/FastCloner?tab=readme-ov-file#limitations)
- **Hybrid AOT** - Uses generated clone code wherever possible, with targeted fallback to the runtime engine only where safety or correctness requires it
- **Automatic type discovery** - The generator follows usages of generic and abstract types and emits concrete clone paths automatically
- **Embeddable** - No dependencies outside the standard library. Source generator and reflection parts can be installed independently
- **Precise control** - Override clone behavior per type or member with `Clone`, `Reference`, `Shallow`, or `Ignore`, at compile time or runtime
- **Selective tracking** - FastCloner avoids identity and cycle-tracking overhead by default, but enables it when graph shape or `[FastClonerPreserveIdentity]` requires it
- **Easy Integration** - `FastDeepClone()` for AOT cloning, `DeepClone()` for reflection cloning. FastCloner respects standard .NET attributes like `[NonSerialized]`, so you can adopt it without depending on library-specific annotations
- **Production Ready** - Used by projects like [Foundatio](https://github.com/FoundatioFx/Foundatio), [Jobbr](https://jobbr.readthedocs.io/en/latest), [TarkovSP](https://sp-tarkov.com), [SnapX](https://github.com/SnapXL/SnapX), and [WinPaletter](https://github.com/Abdelrhman-AK/WinPaletter), with over [1M downloads on NuGet](https://www.nuget.org/packages/fastCloner#usedby-body-tab)
## Getting Started

Install the package via NuGet:

```powershell
dotnet add package FastCloner # Reflection
dotnet add package FastCloner.SourceGenerator # AOT
```

### Clone via Reflection

```csharp
using FastCloner.Code;
var clone = FastCloner.FastCloner.DeepClone(new { Hello = "world", MyList = new List<int> { 1 } });
```

For convenience, add the following method to your project. We intentionally don't ship this extension to make switching from/to FastCloner easier:

```cs
[return: NotNullIfNotNull(nameof(obj))]
public static T? DeepClone<T>(this T? obj)
{
    return FastCloner.FastCloner.DeepClone(obj);
}
```

### Clone via Source Generator

```cs
[FastClonerClonable]
public class GenericClass<T>
{
    public T Value { get; set; }
}

public class MyClass
{
    public string StrVal { get; set; }
}

// [FastClonerClonable] is only required on types where you call .FastDeepClone()
var original = new GenericClass<List<MyClass>> { Value = new List<MyClass> { new MyClass { StrVal = "hello world" } } };
var clone = original.FastDeepClone();
```

## Advanced Usage

### Customizing Clone Behavior

FastCloner supports behavior attributes that control how types and members are cloned:

| Behavior | Effect |
|----------|--------|
| `Clone` | Deep recursive copy |
| `Reference` | Return original instance unchanged |
| `Shallow` | `MemberwiseClone` without recursion |
| `Ignore` | Return `default` |

#### Compile-time (Attributes)

Apply attributes to **types** or **members**. Member-level attributes override type-level:

```csharp
[FastClonerReference]  // Type-level: all usages preserve reference
public class SharedService { }

public class MyClass
{
    public SharedService Svc { get; set; }      // Uses type-level → Reference
    
    [FastClonerBehavior(CloneBehavior.Clone)]   // Member-level override → Clone
    public SharedService ClonedSvc { get; set; }
    
    [FastClonerIgnore]                          // → null/default
    public CancellationToken Token { get; set; }
    
    [FastClonerShallow]                         // → Reference copied directly
    public ParentNode Parent { get; set; }
}
```

Shorthand attributes: `[FastClonerIgnore]`, `[FastClonerShallow]`, `[FastClonerReference]`  
Explicit: `[FastClonerBehavior(CloneBehavior.X)]`

#### Runtime (Reflection only)

Configure type behavior dynamically. Runtime settings are checked **before** attributes:

```csharp
FastCloner.FastCloner.SetTypeBehavior<MySingleton>(CloneBehavior.Reference);
FastCloner.FastCloner.ClearTypeBehavior<MySingleton>();    // Reset one
FastCloner.FastCloner.ClearAllTypeBehaviors();             // Reset all
```

> **Note**: Changing runtime behavior invalidates the cache. Try to configure once at startup, or use compile-time attributes when possible.

#### External ignore attributes

Models often already carry framework-level ignore attributes (`[JsonIgnore]`, `[BsonIgnore]`, `[NotMapped]`, ...). Instead of stacking `[FastClonerIgnore]` on every such member, register those attribute types once per assembly:

```csharp
[assembly: FastClonerExternalIgnore(typeof(JsonIgnoreAttribute), typeof(BsonIgnoreAttribute))]
```

Members carrying a registered attribute are treated as if they had `[FastClonerIgnore]`. The registration is declared on the assembly whose members it governs, so it is resolved statically:

- **works identically for the reflection cloner and the source generator** - the source generator reads the same attribute at compile time,
- **zero runtime cost** - decisions are baked into generated/compiled cloners like any `[FastClonerIgnore]`; no cache invalidation, no per-clone checks, AOT-safe,
- registrations match the exact attribute type or any of its base classes; attribute presence only (constructors and properties like `JsonIgnore(Condition = ...)` are not evaluated).

An explicit member-level `[FastClonerBehavior(...)]` always wins over a registered external attribute.

#### Precedence (highest to lowest)

1. Runtime `SetTypeBehavior<T>()` 
2. Member-level attribute
3. Registered external ignore attribute
4. Type-level attribute on member's type
5. Default behavior

### Cache Management

```csharp
FastCloner.FastCloner.ClearCache();  // Free memory from reflection cache
```


### Generic Classes and Abstract Types

The source generator automatically discovers which concrete types your generic classes and abstract hierarchies are used with:

**Generic types** - The generator scans your codebase for usages like `MyClass<int>` or `MyClass<Customer>` and generates specialized cloning code:

```cs
[FastClonerClonable]
public class Container<T>
{
    public T Value { get; set; }
}

// Source generator finds this usage and generates cloning code for Container<int>
var container = new Container<int> { Value = 42 };
var clone = container.FastDeepClone();
```

**Abstract classes** - The generator automatically finds all concrete derived types in your codebase:

```cs
[FastClonerClonable]
public abstract class Animal
{
    public string Name { get; set; }
}

public class Dog : Animal
{
    public string Breed { get; set; }
}

public class Cat : Animal
{
    public bool IsIndoor { get; set; }
}

// Cloning via the abstract type works - the generator discovered Dog and Cat
Animal pet = new Dog { Name = "Buddy", Breed = "Labrador" };
Animal clone = pet.FastDeepClone(); // Returns a cloned Dog
```

**Non-abstract base classes** - dispatch is not automatic for concrete types. Opt in per hierarchy root with `[FastClonerPolymorphic]`:

```cs
[FastClonerClonable]
[FastClonerPolymorphic]
public class Device
{
    public string Name { get; set; }
}

public class Phone : Device
{
    public string OS { get; set; }
}

Device device = new Phone { Name = "Pixel", OS = "Android" };
Device clone = device.FastDeepClone();
```

### Explicitly Including Types

When a type is only used dynamically (not visible at compile time), use `[FastClonerInclude]` to ensure the generator creates cloning code for it:

```cs
[FastClonerClonable]
[FastClonerInclude(typeof(Customer), typeof(Order))] // Include types used dynamically
public class Wrapper<T>
{
    public T Value { get; set; }
}
```

For abstract classes, you can also use `[FastClonerInclude]` to add derived types that aren't in your codebase (e.g., from external assemblies):

```cs
[FastClonerClonable]
[FastClonerInclude(typeof(ExternalPlugin))] // Add external derived types
public abstract class Plugin
{
    public string Name { get; set; }
}
```

### Custom Cloning Context

For advanced scenarios, create a custom cloning context to explicitly register types you want to clone. This is useful when you need a centralized cloning entry point or want to clone types from external assemblies:

```cs
public class Customer
{
    public string Name { get; set; }
    public Address Address { get; set; }
}

public class Address
{
    public string City { get; set; }
}

// Create a context and register types to clone
[FastClonerRegister(typeof(Customer), typeof(Address))]
public partial class MyCloningContext : FastClonerContext { }
```

Using the context:
```cs
MyCloningContext ctx = new MyCloningContext();

// Clone with compile-time type safety
Customer clone = ctx.Clone(original);

// Check if a type is handled by this context
bool handled = ctx.IsHandled(typeof(Customer)); // true

// Try to clone (returns false for unregistered types)
if (ctx.TryClone(obj, out var cloned))
{
    // Successfully cloned
}
```

### Member Visibility

By default, all members are eligible for cloning regardless of access modifier. Apply `[FastClonerVisibility]` to a type to restrict cloning to a specific subset:

```csharp
[FastClonerVisibility(FastClonerMemberVisibility.Public | FastClonerMemberVisibility.Internal)]
public class Dto
{
    public int Id { get; set; }        // cloned
    internal string Tag;               // cloned
    private string _secret;            // skipped
}
```

The policy applies to both reflection and source-generated paths; excluded members are left at their default value on the clone.

The visibility filter runs before the behavior pipeline and is bypassed for any member carrying a member-level behavior attribute (`[FastClonerBehavior]`, `[FastClonerIgnore]`, `[FastClonerShallow]`, `[FastClonerReference]`), so those members are always included with their declared behavior.

### Nullability Trust

The generator can be instructed to fully trust nullability annotations. When `[FastClonerTrustNullability]` attribute is applied, FastCloner will skip null checks for non-nullable reference types (e.g., `string` vs `string?`), assuming the contract is valid.

```csharp
[FastClonerClonable]
[FastClonerTrustNullability] // Skip null checks for non-nullable members
public class HighPerformanceDto
{
    public string Id { get; set; } // No null check generated
    public string? Details { get; set; } // Null check still generated
}
```

This eliminates branching and improves performance slightly. If a non-nullable property is actually null at runtime, this may result in a `NullReferenceException` in the generated code.

### Safe Handles

When you have a struct that acts as a handle to internal state or a singleton (where identity matters), use `[FastClonerSafeHandle]`. This tells FastCloner to shallow-copy the readonly fields instead of deep-cloning them, preserving the original internal references.

```csharp
[FastClonerSafeHandle]
public struct MyHandle
{
    private readonly object _internalState; // Preserved (shared), not deep cloned
    public int Value; // Cloned normally
}
```

This is the default behavior for system types like `System.Net.Http.Headers.HeaderDescriptor` to prevent breaking internal framework logic. Use this attribute if your custom structs behave similarly.

### Stable Hash Opt-in

Hash-based collections (`HashSet<T>`, `Dictionary<TKey, TValue>`, …) are cloned via a fast memberwise path whenever the key/element type's `GetHashCode` is known to be value-based. FastCloner figures this out automatically (and falls back to rebuilding the collection for identity-based hashes), but it can also be told explicitly with `[FastClonerStableHash]`:

```csharp
[FastClonerStableHash]
public sealed class CompositeKey
{
    public int Major { get; }
    public int Minor { get; }
    public override int GetHashCode() => HashCode.Combine(Major, Minor);
    public override bool Equals(object? obj)
        => obj is CompositeKey other && other.Major == Major && other.Minor == Minor;
}
```

Use it when `GetHashCode` is a pure function of the type's fields (or returns a constant). The attribute skips the runtime probe entirely, which is useful for types the probe cannot construct (abstract bases, types whose default-state `GetHashCode` would throw, etc.) and as a way to lock in the fast path explicitly.

> **Do not** apply this attribute if `GetHashCode` depends on object identity (e.g. `RuntimeHelpers.GetHashCode(this)`, or hashes a per-instance handle that isn't preserved through cloning) — the cloned collection will be unable to find its own contents.

### Identity Preservation

By default, FastCloner prioritizes performance by not tracking object identity during cloning. This means if the same object instance appears multiple times in your graph, each reference becomes a separate clone.

For scenarios where you need to preserve object identity (e.g., shared references should remain shared in the clone), use `[FastClonerPreserveIdentity]`:

```csharp
[FastClonerClonable]
[FastClonerPreserveIdentity] // Enable identity tracking for this type
public class Document
{
    public User Author { get; set; }
    public User LastEditor { get; set; } // May reference the same User as Author
}

var doc = new Document { Author = user, LastEditor = user };
var clone = doc.FastDeepClone();
// clone.Author == clone.LastEditor (same cloned instance)
```

The attribute can be applied at type level or member level:

```csharp
[FastClonerClonable]
public class Container
{
    // Only this member tracks identity
    [FastClonerPreserveIdentity]
    public List<Node> Nodes { get; set; }
    
    // This member clones without identity tracking (faster)
    public List<Item> Items { get; set; }
}
```

You can also explicitly disable identity preservation for a member when the type has it enabled:

```csharp
[FastClonerClonable]
[FastClonerPreserveIdentity]
public class Graph
{
    public Node Root { get; set; }
    
    [FastClonerPreserveIdentity(false)] // Opt out for this member
    public List<string> Labels { get; set; }
}
```

> **Note**: Identity preservation adds overhead for tracking seen objects. Circular references are always detected regardless of this setting.

## Limitations

- Cloning unmanaged resources, such as `IntPtr`s may result in side-effects, as there is no metadata for the length of buffers such pointers often point to.
- `ReadOnly` and `Immutable` collections are tested to behave well if they follow basic conventions.
- With reflection, cloning deeply nested objects switches from recursion to iterative approach on the fly. The threshold for this can be configured by changing `FastCloner.MaxRecursionDepth`, iterative approach is marginally slower.

## Performance

FastCloner is the _fastest_ deep cloning library across both reflection-based and AOT workloads. It was benchmarked against every library capable of cloning objects I've been able to find:
```md
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26220.7271)
Intel Core i7-8700 CPU 3.20GHz (Max: 3.19GHz) (Coffee Lake), 1 CPU, 12 logical and 6 physical cores
.NET SDK 10.0.100

| Method             | Mean        | Error      | StdDev     | Median      | Ratio  | RatioSD | Rank | Gen0   | Gen1   | Allocated | Alloc Ratio |
|------------------- |------------:|-----------:|-----------:|------------:|-------:|--------:|-----:|-------:|-------:|----------:|------------:|
| FastCloner         |    10.25 ns |   0.219 ns |   0.183 ns |    10.24 ns |   1.00 |    0.02 |    1 | 0.0115 |      - |      72 B |        1.00 |
| DeepCopier         |    23.37 ns |   0.448 ns |   0.582 ns |    23.29 ns |   2.28 |    0.07 |    2 | 0.0115 |      - |      72 B |        1.00 |
| DeepCopy           |    40.56 ns |   3.589 ns |  10.583 ns |    43.56 ns |   3.96 |    1.03 |    3 | 0.0115 |      - |      72 B |        1.00 |
| DeepCopyExpression |   126.05 ns |   3.355 ns |   9.892 ns |   129.32 ns |  12.30 |    0.98 |    4 | 0.0356 |      - |     224 B |        3.11 |
| AutoMapper         |   135.07 ns |   6.097 ns |  17.976 ns |   143.16 ns |  13.18 |    1.76 |    5 | 0.0114 |      - |      72 B |        1.00 |
| DeepCloner         |   261.42 ns |  14.113 ns |  41.614 ns |   282.99 ns |  25.51 |    4.06 |    6 | 0.0367 |      - |     232 B |        3.22 |
| ObjectCloner       |   336.89 ns |  14.249 ns |  42.012 ns |   355.28 ns |  32.87 |    4.12 |    7 | 0.0534 |      - |     336 B |        4.67 |
| MessagePack        |   499.71 ns |  20.831 ns |  61.420 ns |   524.63 ns |  48.75 |    6.02 |    8 | 0.0315 |      - |     200 B |        2.78 |
| ProtobufNet        |   898.60 ns |  34.925 ns | 102.978 ns |   934.13 ns |  87.67 |   10.11 |    9 | 0.0782 |      - |     496 B |        6.89 |
| NClone             |   904.75 ns |  33.559 ns |  98.949 ns |   919.05 ns |  88.27 |    9.73 |    9 | 0.1488 |      - |     936 B |       13.00 |
| SystemTextJson     | 1,687.39 ns |  70.341 ns | 201.821 ns | 1,766.14 ns | 164.63 |   19.79 |   10 | 0.1755 |      - |    1120 B |       15.56 |
| NewtonsoftJson     | 3,147.66 ns | 109.097 ns | 321.676 ns | 3,269.96 ns | 307.10 |   31.67 |   11 | 0.7286 | 0.0038 |    4592 B |       63.78 |
| FastDeepCloner     | 3,970.90 ns | 155.503 ns | 458.505 ns | 4,128.09 ns | 387.41 |   45.01 |   12 | 0.2060 |      - |    1304 B |       18.11 |
| AnyCloneBenchmark  | 5,102.40 ns | 239.089 ns | 704.959 ns | 5,370.93 ns | 497.81 |   68.98 |   13 | 0.9003 |      - |    5656 B |       78.56 |
```

You can run the benchmark [locally](https://github.com/lofcz/FastCloner/blob/next/src/FastCloner.Benchmark/BenchMinimal.cs) to verify the results.

### Build Times & IDE Performance

The source generator is designed to work with Roslyn's incremental model. It uses `ForAttributeWithMetadataName`, turns Roslyn symbols into stable `TypeModel` / `MemberModel` records early, and keeps `ISymbol`, syntax nodes, and `Compilation` out of the output pipeline.

- **Incremental pipeline** - Type analysis happens during the transform step, so codegen re-runs only when decorated types or relevant usage data changes.
- **Stable models** - `TypeModel` and `MemberModel` hold precomputed data instead of Roslyn symbols, which keeps incremental caching effective across edits.
- **No `CompilationProvider` in output** - The output pipeline intentionally avoids it to reduce broad invalidation and unnecessary regeneration.
- **Deterministic collection equality** - `EquatableArray` is used so generator model collections compare cleanly in the incremental pipeline.
- **Inlining of one-off helpers** - Helpers used once are inlined to keep generated clone paths direct.

## Internalization

For consumers who wish to embed FastCloner directly without adding a dependency, use the [internalization builder project](https://github.com/lofcz/FastCloner/tree/next/src/FastCloner.Internalization.Builder).

Example command:

```bash
dotnet run --project src/FastCloner.Internalization.Builder/FastCloner.Internalization.Builder.csproj -- \
  --root-namespace MyLibrary.FastCloner \
  --output ../MyLibrary/FastCloner \
  --preprocessor "MODERN=true;" \
  --fqn all \
  --visibility internal \
  --public-api none \
  --runtime-only true \
  --self-check
```

CLI options:

- `--root-namespace <ns>`: Rewrites `FastCloner` namespaces to your target root namespace.
- `--preprocessor <SYMBOL=VALUE;...>`: Per-symbol preprocessor transformation input.
  - `VALUE=true|false` is recognized as boolean and enables full condition resolution/removal where possible.
  - any other value is used as direct replacement in `#if` expressions (e.g., `SOMETHING=random_text`).
  - This lets the builder resolve `#if` branches ahead of time and emit target-specific code.
- `--fqn <prefix1|prefix2|...>`: Fully qualifies matching external metadata types in generated code.
  - Use `all` to qualify all external metadata types.
  - Use prefixes such as `System|System.Collections` to limit qualification to selected namespaces.
  - This is useful when embedding FastCloner into a project that already defines colliding namespaces or type names.
- `--implicit-usings <ns1;ns2;...>`: Namespaces the target project already imports implicitly.  
  Generated global usings for these namespaces are omitted.  
  Default is empty, so generated code carries explicit usings.
- `--visibility <public|internal>`: Top-level visibility rewrite policy.
- `--public-api <none|fastcloner|extensions|behaviors|all>`: Keeps selected public surface when `--visibility internal` is used.
- `--runtime-only <true|false>`: Includes only runtime clone engine files.
- `--dry-run`: Prints planned output files and transform stats without writing.
- `--self-check`: Compiles generated source tree and reports compile errors.

## Contributing

If you are looking to add new functionality, please open an issue first to verify your intent is aligned with the scope of the project. The library is covered by over [800 tests](https://github.com/lofcz/FastCloner/tree/next/src/FastCloner.Tests), please run them against your work before proposing changes. Tests run in parallel to verify thread-safety of the library (with targeted exceptions). Run `dotnet test` from the cloned repo root. We also run benchmark regression analysis on every pull request to `next`; if a change causes a measurable performance regression, the PR should clearly justify that trade-off. When reporting issues, providing a minimal reproduction we can plug in as a new test greatly reduces turnaround time. We use [TUnit](https://github.com/thomhurst/TUnit) for testing.

Each PR gets an updated benchmark report comment from `github-actions`, so you can spot regressions early and iterate before merge.

<details>
<summary>Example benchmark report</summary>

| Status | Benchmark | Delta Time | Delta Alloc |
|---|---|---|---|
| 🟢 | DynamicWithArray | -15% faster | ~same |
| ⚪ | DynamicWithDictionary | +5% slower | ~same |
| ⚪ | DynamicWithNestedObject | ~same | ~same |
| ⚪ | FileSpec | -4% faster | ~same |
| 🟢 | LargeEventDocument_10MB | -7% faster | ~same |
| ⚪ | LargeLogBatch_10MB | ~same | ~same |
| ⚪ | MediumNestedObject | ~same | ~same |
| ⚪ | ObjectDictionary_50 | ~same | ~same |
| ⚪ | ObjectList_100 | -2% faster | ~same |
| 🟢 | SmallObject | -6% faster | ~same |
| ⚪ | SmallObjectWithCollections | -2% faster | ~same |
| ⚪ | StringArray_1000 | ~same | ~same |

</details>

<details>
<summary>Tests Troubleshooting</summary>

Rider: automatic tests discovery
  - Temporarily disable VSTest adapters support (`Build, Execution, Deployment > Unit Testing > VSTest`)
  - Enable Testing Platform support (`Build, Execution, Deployment > Unit Testing > Testing Platform`)
  - Re-enable VSTest adapters support
  - Rebuild / refresh the test explorer

</details>

## License

This library is licensed under the [MIT](https://github.com/lofcz/FastCloner/blob/next/LICENSE) license. 💜
