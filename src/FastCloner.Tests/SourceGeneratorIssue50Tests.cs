using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using FastCloner.SourceGenerator.Shared;
using System.Threading.Tasks;

namespace FastCloner.Tests;

/// <summary>
/// Simple mutable reference type for issue #50 tests
/// </summary>
[FastClonerClonable]
public class Issue50Item
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

/// <summary>
/// Tests Collection&lt;T&gt; with clonable elements - the original issue #50 repro.
/// Collection&lt;T&gt; has no capacity ctor and CollectionsMarshal does not apply to it.
/// </summary>
[FastClonerClonable]
public class CollectionOfClonableContainer
{
    public Collection<Issue50Item> Items { get; set; } = [];
}

/// <summary>
/// Tests BindingList&lt;T&gt; with clonable elements (issue #50).
/// </summary>
[FastClonerClonable]
public class BindingListContainer
{
    public BindingList<Issue50Item> Items { get; set; } = [];
}

/// <summary>
/// Tests Collection&lt;T&gt; with safe elements - guards against the aliasing bug where
/// new Collection&lt;T&gt;(source) WRAPS the source list instead of copying it.
/// </summary>
[FastClonerClonable]
public class CollectionOfIntContainer
{
    public Collection<int> Numbers { get; set; } = [];
}

/// <summary>
/// A Dictionary subclass - does not inherit the capacity or copy constructors of its base.
/// </summary>
public class DerivedItemDictionary : Dictionary<string, Issue50Item>;

[FastClonerClonable]
public class DerivedDictionaryContainer
{
    public DerivedItemDictionary? Items { get; set; }
}

/// <summary>
/// A Dictionary subclass with safe key/value types - guards the copy-ctor fast path,
/// which must not assume new Derived(source) exists.
/// </summary>
public class DerivedIntDictionary : Dictionary<string, int>;

[FastClonerClonable]
public class DerivedIntDictionaryContainer
{
    public DerivedIntDictionary? Items { get; set; }
}

/// <summary>
/// An ObservableCollection subclass - no inherited constructors besides the implicit parameterless one.
/// </summary>
public class DerivedObservableCollection : ObservableCollection<Issue50Item>;

[FastClonerClonable]
public class DerivedObservableContainer
{
    public DerivedObservableCollection? Items { get; set; }
}

/// <summary>
/// A HashSet subclass with safe elements - does not inherit capacity/IEnumerable constructors.
/// </summary>
public class DerivedIntHashSet : HashSet<int>;

[FastClonerClonable]
public class DerivedHashSetContainer
{
    public DerivedIntHashSet? Numbers { get; set; }
}

/// <summary>
/// Tests getter-only Collection&lt;T&gt; property populated in place via Clear + Add.
/// </summary>
[FastClonerClonable]
public class GetterOnlyCollectionContainer
{
    public Collection<Issue50Item> Items { get; } = [];
}

/// <summary>
/// Tests getter-only BindingList&lt;T&gt; property populated in place via Clear + Add.
/// </summary>
[FastClonerClonable]
public class GetterOnlyBindingListContainer
{
    public BindingList<Issue50Item> Items { get; } = [];
}

/// <summary>
/// Tests Stack&lt;T&gt; with safe elements - the IEnumerable copy-ctor would reverse the stack.
/// </summary>
[FastClonerClonable]
public class IntStackContainer
{
    public Stack<int>? Numbers { get; set; }
}

/// <summary>
/// A collection with no parameterless constructor and no Add method: the generator cannot
/// verify a usable API surface and must fall back to the runtime cloner instead of
/// emitting code that does not compile.
/// </summary>
public class FrozenCollection<T> : IEnumerable<T>
{
    private readonly List<T> inner;
    public FrozenCollection(IEnumerable<T> items) => inner = [.. items];
    public IEnumerator<T> GetEnumerator() => inner.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

[FastClonerClonable]
public class FrozenCollectionContainer
{
    public FrozenCollection<Issue50Item>? Items { get; set; }
}

public class SourceGeneratorIssue50Tests
{
    [Test]
    [SourceGeneratorCompatible]
    public async Task Collection_With_Clonable_Elements_Should_Deep_Clone()
    {
        CollectionOfClonableContainer original = new CollectionOfClonableContainer
        {
            Items = [new Issue50Item { Id = 1, Name = "First" }, new Issue50Item { Id = 2, Name = "Second" }]
        };

        CollectionOfClonableContainer clone = original.FastDeepClone();

        await Assert.That(clone).IsNotNull();
        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items.Count).IsEqualTo(2);
        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(original.Items[0]);
        await Assert.That(clone.Items[0].Id).IsEqualTo(1);
        await Assert.That(clone.Items[1].Name).IsEqualTo("Second");

        clone.Items[0].Name = "Changed";
        await Assert.That(original.Items[0].Name).IsEqualTo("First");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task BindingList_With_Clonable_Elements_Should_Deep_Clone()
    {
        BindingListContainer original = new BindingListContainer
        {
            Items = [new Issue50Item { Id = 1, Name = "First" }, new Issue50Item { Id = 2, Name = "Second" }]
        };

        BindingListContainer clone = original.FastDeepClone();

        await Assert.That(clone).IsNotNull();
        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items.Count).IsEqualTo(2);
        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(original.Items[0]);
        await Assert.That(clone.Items[1].Name).IsEqualTo("Second");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Collection_With_Safe_Elements_Should_Not_Alias_Source()
    {
        CollectionOfIntContainer original = new CollectionOfIntContainer { Numbers = [1, 2, 3] };

        CollectionOfIntContainer clone = original.FastDeepClone();

        await Assert.That(clone.Numbers).IsNotSameReferenceAs(original.Numbers);
        await Assert.That(clone.Numbers.SequenceEqual(original.Numbers)).IsTrue();

        // new Collection<T>(IList<T>) wraps the passed list; mutating the clone must not leak into the original
        clone.Numbers.Add(4);
        await Assert.That(original.Numbers.Count).IsEqualTo(3);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Derived_Dictionary_Should_Deep_Clone()
    {
        DerivedDictionaryContainer original = new DerivedDictionaryContainer
        {
            Items = new DerivedItemDictionary
            {
                ["a"] = new Issue50Item { Id = 1, Name = "A" },
                ["b"] = new Issue50Item { Id = 2, Name = "B" }
            }
        };

        DerivedDictionaryContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotNull();
        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items!.Count).IsEqualTo(2);
        await Assert.That(clone.Items["a"]).IsNotSameReferenceAs(original.Items!["a"]);
        await Assert.That(clone.Items["b"].Name).IsEqualTo("B");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Derived_Dictionary_With_Safe_Values_Should_Clone()
    {
        DerivedIntDictionaryContainer original = new DerivedIntDictionaryContainer
        {
            Items = new DerivedIntDictionary { ["a"] = 1, ["b"] = 2 }
        };

        DerivedIntDictionaryContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotNull();
        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items!["a"]).IsEqualTo(1);
        await Assert.That(clone.Items["b"]).IsEqualTo(2);

        clone.Items["c"] = 3;
        await Assert.That(original.Items!.Count).IsEqualTo(2);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Derived_ObservableCollection_Should_Deep_Clone()
    {
        DerivedObservableContainer original = new DerivedObservableContainer
        {
            Items = [new Issue50Item { Id = 1, Name = "First" }]
        };

        DerivedObservableContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotNull();
        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items!.Count).IsEqualTo(1);
        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(original.Items![0]);
        await Assert.That(clone.Items[0].Name).IsEqualTo("First");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Derived_HashSet_With_Safe_Elements_Should_Clone()
    {
        DerivedHashSetContainer original = new DerivedHashSetContainer
        {
            Numbers = [1, 2, 3]
        };

        DerivedHashSetContainer clone = original.FastDeepClone();

        await Assert.That(clone.Numbers).IsNotNull();
        await Assert.That(clone.Numbers).IsNotSameReferenceAs(original.Numbers);
        await Assert.That(clone.Numbers!.SetEquals(original.Numbers!)).IsTrue();

        clone.Numbers.Add(4);
        await Assert.That(original.Numbers!.Count).IsEqualTo(3);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task GetterOnly_Collection_Should_Be_Populated()
    {
        GetterOnlyCollectionContainer original = new GetterOnlyCollectionContainer();
        original.Items.Add(new Issue50Item { Id = 1, Name = "First" });
        original.Items.Add(new Issue50Item { Id = 2, Name = "Second" });

        GetterOnlyCollectionContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items.Count).IsEqualTo(2);
        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(original.Items[0]);
        await Assert.That(clone.Items[1].Name).IsEqualTo("Second");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task GetterOnly_BindingList_Should_Be_Populated()
    {
        GetterOnlyBindingListContainer original = new GetterOnlyBindingListContainer();
        original.Items.Add(new Issue50Item { Id = 1, Name = "First" });

        GetterOnlyBindingListContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items.Count).IsEqualTo(1);
        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(original.Items[0]);
        await Assert.That(clone.Items[0].Name).IsEqualTo("First");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Stack_With_Safe_Elements_Should_Preserve_Order()
    {
        IntStackContainer original = new IntStackContainer { Numbers = new Stack<int>() };
        original.Numbers.Push(1);
        original.Numbers.Push(2);
        original.Numbers.Push(3);

        IntStackContainer clone = original.FastDeepClone();

        await Assert.That(clone.Numbers).IsNotNull();
        await Assert.That(clone.Numbers).IsNotSameReferenceAs(original.Numbers);
        // Enumeration is pop order: 3, 2, 1. A naive new Stack<int>(source) would yield 1, 2, 3.
        await Assert.That(clone.Numbers!.ToArray().SequenceEqual([3, 2, 1])).IsTrue();
    }

    [Test]
    public async Task Unverifiable_Collection_Should_Fall_Back_To_Runtime_Cloner()
    {
        FrozenCollectionContainer original = new FrozenCollectionContainer
        {
            Items = new FrozenCollection<Issue50Item>([new Issue50Item { Id = 1, Name = "First" }])
        };

        FrozenCollectionContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotNull();
        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);

        Issue50Item[] clonedItems = clone.Items!.ToArray();
        await Assert.That(clonedItems.Length).IsEqualTo(1);
        await Assert.That(clonedItems[0]).IsNotSameReferenceAs(original.Items!.First());
        await Assert.That(clonedItems[0].Name).IsEqualTo("First");
    }
}
