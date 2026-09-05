using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using FastCloner.SourceGenerator.Shared;
using System.Threading.Tasks;

namespace FastCloner.Tests;

/// <summary>
/// Reference-type payload used as a nullable generic argument (issue #54).
/// </summary>
public record Issue54Payload(string Text);

/// <summary>
/// Value-type counterpart: nullability is a <c>Nullable&lt;T&gt;</c> and was already preserved.
/// </summary>
public record struct Issue54PayloadStruct(string Text);

public class Issue54Wrapper<T>
{
    public T Value { get; set; } = default!;
}

[FastClonerClonable]
public sealed class Issue54DictionaryContainer
{
    public Dictionary<string, Issue54Payload?>? Map { get; set; }
}

[FastClonerClonable]
public sealed class Issue54ListContainer
{
    public List<Issue54Payload?>? Items { get; set; }
}

[FastClonerClonable]
public sealed class Issue54ArrayContainer
{
    public Issue54Payload?[]? Items { get; set; }
}

[FastClonerClonable]
public sealed class Issue54JaggedArrayContainer
{
    public Issue54Payload?[][]? Items { get; set; }
}

[FastClonerClonable]
public sealed class Issue54NullableKeyContainer
{
    public Dictionary<string?, Issue54Payload>? Map { get; set; }
}

[FastClonerClonable]
public sealed class Issue54NestedContainer
{
    public Dictionary<string, List<Issue54Payload?>>? Map { get; set; }
}

[FastClonerClonable]
public sealed class Issue54ReadOnlyDictionaryContainer
{
    public IReadOnlyDictionary<string, Issue54Payload?>? Map { get; set; }
}

[FastClonerClonable]
public sealed class Issue54CustomGenericContainer
{
    public Issue54Wrapper<Issue54Payload?>? Item { get; set; }
}

[FastClonerClonable]
public sealed class Issue54BothVariantsContainer
{
    public Issue54Wrapper<Issue54Payload>? NonNullArg { get; set; }
    public Issue54Wrapper<Issue54Payload?>? NullableArg { get; set; }
}

[FastClonerClonable]
public sealed class Issue54NullableStringListContainer
{
    public List<string?>? Items { get; set; }
}

[FastClonerClonable]
public sealed class Issue54StructDictionaryContainer
{
    public Dictionary<string, Issue54PayloadStruct?>? Map { get; set; }
}

[FastClonerClonable]
[FastClonerTrustNullability]
public sealed class Issue54TrustNullabilityContainer
{
    public Dictionary<string, Issue54Payload?>? Map { get; set; }
}

[FastClonerClonable]
public sealed class Issue54HashSetContainer
{
    public HashSet<Issue54Payload?>? Items { get; set; }
}

[FastClonerClonable]
public sealed class Issue54QueueContainer
{
    public Queue<Issue54Payload?>? Items { get; set; }
}

[FastClonerClonable]
public sealed class Issue54StackContainer
{
    public Stack<Issue54Payload?>? Items { get; set; }
}

[FastClonerClonable]
public sealed class Issue54LinkedListContainer
{
    public LinkedList<Issue54Payload?>? Items { get; set; }
}

[FastClonerClonable]
public sealed class Issue54ReadOnlyListContainer
{
    public IReadOnlyList<Issue54Payload?>? Items { get; set; }
}

[FastClonerClonable]
public sealed class Issue54ObservableContainer
{
    public ObservableCollection<Issue54Payload?>? Items { get; set; }
}

[FastClonerClonable]
public sealed class Issue54NestedListContainer
{
    public List<List<Issue54Payload?>>? Items { get; set; }
}

[FastClonerClonable]
public sealed class Issue54MultiDimArrayContainer
{
    public Issue54Payload?[,]? Items { get; set; }
}

[FastClonerClonable]
public sealed class Issue54HashSetStringContainer
{
    public HashSet<string?>? Items { get; set; }
}

[FastClonerClonable]
public sealed class Issue54WrapperListContainer
{
    public Issue54Wrapper<List<Issue54Payload?>>? Item { get; set; }
}

[FastClonerClonable]
public sealed class Issue54ConcurrentDictionaryContainer
{
    public ConcurrentDictionary<string, Issue54Payload?>? Map { get; set; }
}

[FastClonerClonable]
public sealed class Issue54ImmutableListContainer
{
    public ImmutableList<Issue54Payload?>? Items { get; set; }
}

[FastClonerClonable]
public sealed class Issue54FieldContainer
{
    public Dictionary<string, Issue54Payload?>? Map;
}

[SourceGeneratorCompatible]
public class SourceGeneratorIssue54Tests
{
    [Test]
    [SourceGeneratorCompatible]
    public async Task Dictionary_WithNullableReferenceValue_Should_Clone()
    {
        Issue54Payload shared = new Issue54Payload("kept");
        Issue54DictionaryContainer original = new Issue54DictionaryContainer
        {
            Map = new Dictionary<string, Issue54Payload?>
            {
                ["a"] = new Issue54Payload("hello"),
                ["b"] = null,
                ["c"] = shared
            }
        };

        Issue54DictionaryContainer clone = original.FastDeepClone();

        await Assert.That(clone).IsNotNull();
        await Assert.That(clone.Map).IsNotNull();
        await Assert.That(clone.Map).IsNotSameReferenceAs(original.Map);
        await Assert.That(clone.Map!["a"]).IsNotNull();
        await Assert.That(clone.Map["a"]!.Text).IsEqualTo("hello");
        await Assert.That(clone.Map["a"]).IsNotSameReferenceAs(original.Map!["a"]);
        await Assert.That(clone.Map["b"]).IsNull();
        await Assert.That(clone.Map["c"]).IsNotSameReferenceAs(shared);
        await Assert.That(clone.Map["c"]!.Text).IsEqualTo("kept");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task List_WithNullableReferenceElement_Should_Clone()
    {
        Issue54ListContainer original = new Issue54ListContainer
        {
            Items = [new Issue54Payload("one"), null, new Issue54Payload("three")]
        };

        Issue54ListContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotNull();
        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items!.Count).IsEqualTo(3);
        await Assert.That(clone.Items[0]!.Text).IsEqualTo("one");
        await Assert.That(clone.Items[1]).IsNull();
        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(original.Items![0]);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Array_WithNullableReferenceElement_Should_Clone()
    {
        Issue54ArrayContainer original = new Issue54ArrayContainer
        {
            Items = [new Issue54Payload("x"), null]
        };

        Issue54ArrayContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items!.Length).IsEqualTo(2);
        await Assert.That(clone.Items[0]!.Text).IsEqualTo("x");
        await Assert.That(clone.Items[1]).IsNull();
        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(original.Items![0]);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task JaggedArray_WithNullableReferenceElement_Should_Clone()
    {
        Issue54JaggedArrayContainer original = new Issue54JaggedArrayContainer
        {
            Items =
            [
                [new Issue54Payload("row"), null],
                [new Issue54Payload("other")]
            ]
        };

        Issue54JaggedArrayContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items![0]).IsNotSameReferenceAs(original.Items![0]);
        await Assert.That(clone.Items[0]![0]!.Text).IsEqualTo("row");
        await Assert.That(clone.Items[0]![1]).IsNull();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Dictionary_WithNullableReferenceKey_Should_Clone()
    {
        Issue54Payload value = new Issue54Payload("v");
        Issue54NullableKeyContainer original = new Issue54NullableKeyContainer
        {
            Map = new Dictionary<string?, Issue54Payload>
            {
                ["k"] = value
            }
        };

        Issue54NullableKeyContainer clone = original.FastDeepClone();

        await Assert.That(clone.Map).IsNotSameReferenceAs(original.Map);
        await Assert.That(clone.Map!["k"].Text).IsEqualTo("v");
        await Assert.That(clone.Map["k"]).IsNotSameReferenceAs(value);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Nested_DictionaryOfListOfNullableReference_Should_Clone()
    {
        Issue54NestedContainer original = new Issue54NestedContainer
        {
            Map = new Dictionary<string, List<Issue54Payload?>>
            {
                ["xs"] = [new Issue54Payload("n"), null]
            }
        };

        Issue54NestedContainer clone = original.FastDeepClone();

        await Assert.That(clone.Map).IsNotSameReferenceAs(original.Map);
        await Assert.That(clone.Map!["xs"]).IsNotSameReferenceAs(original.Map!["xs"]);
        await Assert.That(clone.Map["xs"][0]!.Text).IsEqualTo("n");
        await Assert.That(clone.Map["xs"][1]).IsNull();
        await Assert.That(clone.Map["xs"][0]).IsNotSameReferenceAs(original.Map["xs"][0]);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task ReadOnlyDictionary_WithNullableReferenceValue_Should_Clone()
    {
        Issue54ReadOnlyDictionaryContainer original = new Issue54ReadOnlyDictionaryContainer
        {
            Map = new Dictionary<string, Issue54Payload?> { ["a"] = new Issue54Payload("ro") }
        };

        Issue54ReadOnlyDictionaryContainer clone = original.FastDeepClone();

        await Assert.That(clone.Map).IsNotSameReferenceAs(original.Map);
        await Assert.That(clone.Map!["a"]!.Text).IsEqualTo("ro");
        await Assert.That(clone.Map["a"]).IsNotSameReferenceAs(original.Map!["a"]);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task CustomGeneric_WithNullableTypeArgument_Should_Clone()
    {
        Issue54CustomGenericContainer original = new Issue54CustomGenericContainer
        {
            Item = new Issue54Wrapper<Issue54Payload?> { Value = new Issue54Payload("wrap") }
        };

        Issue54CustomGenericContainer clone = original.FastDeepClone();

        await Assert.That(clone.Item).IsNotSameReferenceAs(original.Item);
        await Assert.That(clone.Item!.Value).IsNotNull();
        await Assert.That(clone.Item.Value!.Text).IsEqualTo("wrap");
        await Assert.That(clone.Item.Value).IsNotSameReferenceAs(original.Item!.Value);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task CustomGeneric_BothNullabilityVariants_Should_CloneIndependently()
    {
        Issue54Payload shared = new Issue54Payload("shared");
        Issue54BothVariantsContainer original = new Issue54BothVariantsContainer
        {
            NonNullArg = new Issue54Wrapper<Issue54Payload> { Value = shared },
            NullableArg = new Issue54Wrapper<Issue54Payload?> { Value = shared }
        };

        Issue54BothVariantsContainer clone = original.FastDeepClone();

        await Assert.That(clone.NonNullArg).IsNotSameReferenceAs(original.NonNullArg);
        await Assert.That(clone.NullableArg).IsNotSameReferenceAs(original.NullableArg);
        await Assert.That(clone.NonNullArg!.Value.Text).IsEqualTo("shared");
        await Assert.That(clone.NullableArg!.Value!.Text).IsEqualTo("shared");
        await Assert.That(clone.NonNullArg.Value).IsNotSameReferenceAs(shared);
        await Assert.That(clone.NullableArg.Value).IsNotSameReferenceAs(shared);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task List_OfNullableString_Should_Clone()
    {
        Issue54NullableStringListContainer original = new Issue54NullableStringListContainer
        {
            Items = ["a", null, "c"]
        };

        Issue54NullableStringListContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items!.Count).IsEqualTo(3);
        await Assert.That(clone.Items[0]).IsEqualTo("a");
        await Assert.That(clone.Items[1]).IsNull();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Dictionary_WithNullableValueType_Should_Still_Clone()
    {
        Issue54StructDictionaryContainer original = new Issue54StructDictionaryContainer
        {
            Map = new Dictionary<string, Issue54PayloadStruct?>
            {
                ["a"] = new Issue54PayloadStruct("s"),
                ["b"] = null
            }
        };

        Issue54StructDictionaryContainer clone = original.FastDeepClone();

        await Assert.That(clone.Map).IsNotSameReferenceAs(original.Map);
        await Assert.That(clone.Map!["a"]!.Value.Text).IsEqualTo("s");
        await Assert.That(clone.Map["b"]).IsNull();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task TrustNullability_WithNullableGenericArgument_Should_Clone()
    {
        Issue54TrustNullabilityContainer original = new Issue54TrustNullabilityContainer
        {
            Map = new Dictionary<string, Issue54Payload?> { ["a"] = new Issue54Payload("t") }
        };

        Issue54TrustNullabilityContainer clone = original.FastDeepClone();

        await Assert.That(clone.Map).IsNotSameReferenceAs(original.Map);
        await Assert.That(clone.Map!["a"]!.Text).IsEqualTo("t");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task HashSet_WithNullableReferenceElement_Should_Clone()
    {
        Issue54Payload kept = new Issue54Payload("kept");
        Issue54HashSetContainer original = new Issue54HashSetContainer
        {
            Items = [new Issue54Payload("a"), null, kept]
        };

        Issue54HashSetContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items!.Count).IsEqualTo(3);
        await Assert.That(clone.Items.Contains(null)).IsTrue();
        await Assert.That(clone.Items.Any(x => x?.Text == "kept")).IsTrue();
        await Assert.That(clone.Items.Any(x => ReferenceEquals(x, kept))).IsFalse();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Queue_WithNullableReferenceElement_Should_Preserve_Fifo()
    {
        Issue54QueueContainer original = new Issue54QueueContainer { Items = new Queue<Issue54Payload?>() };
        original.Items.Enqueue(new Issue54Payload("first"));
        original.Items.Enqueue(null);
        original.Items.Enqueue(new Issue54Payload("last"));

        Issue54QueueContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        Issue54Payload?[] cloned = clone.Items!.ToArray();
        await Assert.That(cloned.Length).IsEqualTo(3);
        await Assert.That(cloned[0]!.Text).IsEqualTo("first");
        await Assert.That(cloned[1]).IsNull();
        await Assert.That(cloned[2]!.Text).IsEqualTo("last");
        await Assert.That(cloned[0]).IsNotSameReferenceAs(original.Items.Peek());
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Stack_WithNullableReferenceElement_Should_Preserve_Lifo()
    {
        Issue54StackContainer original = new Issue54StackContainer { Items = new Stack<Issue54Payload?>() };
        original.Items.Push(new Issue54Payload("bottom"));
        original.Items.Push(null);
        original.Items.Push(new Issue54Payload("top"));

        Issue54StackContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        Issue54Payload?[] cloned = clone.Items!.ToArray();
        await Assert.That(cloned[0]!.Text).IsEqualTo("top");
        await Assert.That(cloned[1]).IsNull();
        await Assert.That(cloned[2]!.Text).IsEqualTo("bottom");
        await Assert.That(clone.Items.Peek()!.Text).IsEqualTo("top");
        await Assert.That(original.Items.Peek()!.Text).IsEqualTo("top");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task LinkedList_WithNullableReferenceElement_Should_Clone()
    {
        Issue54LinkedListContainer original = new Issue54LinkedListContainer { Items = [] };
        original.Items.AddLast(new Issue54Payload("a"));
        original.Items.AddLast((Issue54Payload?)null);
        original.Items.AddLast(new Issue54Payload("c"));

        Issue54LinkedListContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items!.Count).IsEqualTo(3);
        await Assert.That(clone.Items.First!.Value!.Text).IsEqualTo("a");
        await Assert.That(clone.Items.First.Next!.Value).IsNull();
        await Assert.That(clone.Items.Last!.Value!.Text).IsEqualTo("c");
        await Assert.That(clone.Items.First.Value).IsNotSameReferenceAs(original.Items.First!.Value);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task ReadOnlyList_WithNullableReferenceElement_Should_Clone()
    {
        Issue54ReadOnlyListContainer original = new Issue54ReadOnlyListContainer
        {
            Items = new List<Issue54Payload?> { new Issue54Payload("ro"), null }
        };

        Issue54ReadOnlyListContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items!.Count).IsEqualTo(2);
        await Assert.That(clone.Items[0]!.Text).IsEqualTo("ro");
        await Assert.That(clone.Items[1]).IsNull();
        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(original.Items![0]);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task ObservableCollection_WithNullableReferenceElement_Should_Clone()
    {
        Issue54ObservableContainer original = new Issue54ObservableContainer
        {
            Items = [new Issue54Payload("o"), null]
        };

        Issue54ObservableContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items!.Count).IsEqualTo(2);
        await Assert.That(clone.Items[0]!.Text).IsEqualTo("o");
        await Assert.That(clone.Items[1]).IsNull();
        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(original.Items![0]);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task NestedList_OfNullableReference_Should_Clone()
    {
        Issue54NestedListContainer original = new Issue54NestedListContainer
        {
            Items =
            [
                [new Issue54Payload("inner"), null],
                []
            ]
        };

        Issue54NestedListContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items![0]).IsNotSameReferenceAs(original.Items![0]);
        await Assert.That(clone.Items[0][0]!.Text).IsEqualTo("inner");
        await Assert.That(clone.Items[0][1]).IsNull();
        await Assert.That(clone.Items[1].Count).IsEqualTo(0);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task MultiDimArray_WithNullableReferenceElement_Should_Clone()
    {
        Issue54MultiDimArrayContainer original = new Issue54MultiDimArrayContainer
        {
            Items = new Issue54Payload?[2, 2]
        };
        original.Items[0, 0] = new Issue54Payload("a");
        original.Items[1, 1] = null;

        Issue54MultiDimArrayContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items![0, 0]!.Text).IsEqualTo("a");
        await Assert.That(clone.Items[0, 0]).IsNotSameReferenceAs(original.Items![0, 0]);
        await Assert.That(clone.Items[1, 1]).IsNull();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task HashSet_OfNullableString_Should_Clone()
    {
        Issue54HashSetStringContainer original = new Issue54HashSetStringContainer
        {
            Items = ["a", null, "c"]
        };

        Issue54HashSetStringContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items!.SetEquals(["a", null, "c"])).IsTrue();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Wrapper_OfListOfNullableReference_Should_Clone()
    {
        Issue54WrapperListContainer original = new Issue54WrapperListContainer
        {
            Item = new Issue54Wrapper<List<Issue54Payload?>>
            {
                Value = [new Issue54Payload("w"), null]
            }
        };

        Issue54WrapperListContainer clone = original.FastDeepClone();

        await Assert.That(clone.Item).IsNotSameReferenceAs(original.Item);
        await Assert.That(clone.Item!.Value).IsNotSameReferenceAs(original.Item!.Value);
        await Assert.That(clone.Item.Value[0]!.Text).IsEqualTo("w");
        await Assert.That(clone.Item.Value[1]).IsNull();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task ConcurrentDictionary_WithNullableReferenceValue_Should_Clone()
    {
        Issue54ConcurrentDictionaryContainer original = new Issue54ConcurrentDictionaryContainer
        {
            Map = new ConcurrentDictionary<string, Issue54Payload?>(
                new Dictionary<string, Issue54Payload?>
                {
                    ["a"] = new Issue54Payload("cd"),
                    ["b"] = null
                })
        };

        Issue54ConcurrentDictionaryContainer clone = original.FastDeepClone();

        await Assert.That(clone.Map).IsNotSameReferenceAs(original.Map);
        await Assert.That(clone.Map!["a"]!.Text).IsEqualTo("cd");
        await Assert.That(clone.Map["a"]).IsNotSameReferenceAs(original.Map!["a"]);
        await Assert.That(clone.Map["b"]).IsNull();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task ImmutableList_WithNullableReferenceElement_Should_Clone()
    {
        Issue54ImmutableListContainer original = new Issue54ImmutableListContainer
        {
            Items = ImmutableList.Create<Issue54Payload?>(new Issue54Payload("im"), null)
        };

        Issue54ImmutableListContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items!.Count).IsEqualTo(2);
        await Assert.That(clone.Items[0]!.Text).IsEqualTo("im");
        await Assert.That(clone.Items[1]).IsNull();
        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(original.Items![0]);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Field_Dictionary_WithNullableReferenceValue_Should_Clone()
    {
        Issue54FieldContainer original = new Issue54FieldContainer
        {
            Map = new Dictionary<string, Issue54Payload?> { ["f"] = new Issue54Payload("field") }
        };

        Issue54FieldContainer clone = original.FastDeepClone();

        await Assert.That(clone.Map).IsNotSameReferenceAs(original.Map);
        await Assert.That(clone.Map!["f"]!.Text).IsEqualTo("field");
        await Assert.That(clone.Map["f"]).IsNotSameReferenceAs(original.Map!["f"]);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Dictionary_NullMap_Should_Stay_Null()
    {
        Issue54DictionaryContainer original = new Issue54DictionaryContainer { Map = null };
        Issue54DictionaryContainer clone = original.FastDeepClone();

        await Assert.That(clone).IsNotSameReferenceAs(original);
        await Assert.That(clone.Map).IsNull();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task List_Empty_Should_Clone_To_Empty()
    {
        Issue54ListContainer original = new Issue54ListContainer { Items = [] };
        Issue54ListContainer clone = original.FastDeepClone();

        await Assert.That(clone.Items).IsNotNull();
        await Assert.That(clone.Items).IsNotSameReferenceAs(original.Items);
        await Assert.That(clone.Items!.Count).IsEqualTo(0);
    }
}
