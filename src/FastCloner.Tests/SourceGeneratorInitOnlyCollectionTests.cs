using System.Collections.Generic;
using FastCloner.SourceGenerator.Shared;
using System.Threading.Tasks;

namespace FastCloner.Tests;

/// <summary>
/// Repro and class-of-problem coverage for init-only / required members that are
/// cloned through InternalFastDeepClone (collection elements, nested clonables).
/// Public FastDeepClone of an init-only type already used an object initializer;
/// the state-tracking construction path did <c>new T()</c> and then skipped init
/// (https://github.com/ZeroCoolDade/FastClonerBug).
/// </summary>
[FastClonerClonable]
public class InitOnlyLeaf
{
    public string Name { get; init; } = string.Empty;
    public int Id { get; init; }
    public bool Enabled { get; init; }
}

[FastClonerClonable]
public class SettableLeaf
{
    public string Name { get; set; } = string.Empty;
    public int Id { get; set; }
    public bool Enabled { get; set; }
}

[FastClonerClonable]
public class InitOnlyContainer
{
    public string ItemName { get; set; } = string.Empty;
    public int ItemId { get; init; } = int.MaxValue;
    public List<InitOnlyLeaf> InitOnlyList { get; set; } = [];
    public List<SettableLeaf> SettableList { get; set; } = [];
}

[FastClonerClonable]
public class NestedInitOnly
{
    public InitOnlyLeaf Child { get; init; } = new();
}

[FastClonerClonable]
public class NestedInitOnlyHolder
{
    public List<NestedInitOnly> Items { get; set; } = [];
}

[FastClonerClonable]
public record InitOnlyRecord
{
    public int Id { get; init; }
    public string? Name { get; init; }
}

[FastClonerClonable]
public class InitOnlyRecordHolder
{
    public List<InitOnlyRecord> Items { get; set; } = [];
}

[FastClonerClonable]
public class RequiredLeaf
{
    public required string Name { get; set; }
    public required int Id { get; init; }
}

[FastClonerClonable]
public class RequiredLeafHolder
{
    public List<RequiredLeaf> Items { get; set; } = [];
}

public class SourceGeneratorInitOnlyCollectionTests
{
    [Test]
    [SourceGeneratorCompatible]
    public async Task InitOnly_Leaf_Cloned_As_Collection_Element_Should_Copy_Values()
    {
        InitOnlyContainer original = new InitOnlyContainer
        {
            ItemName = "Item",
            ItemId = 999,
            InitOnlyList = [new InitOnlyLeaf { Id = 1234, Name = "NotWorkingTest", Enabled = true }],
            SettableList = [new SettableLeaf { Id = 5678, Name = "WorkingTest", Enabled = true }]
        };

        InitOnlyContainer clone = original.FastDeepClone();

        await Assert.That(clone).IsNotNull();
        await Assert.That(clone.ItemName).IsEqualTo("Item");
        await Assert.That(clone.ItemId).IsEqualTo(999);
        await Assert.That(clone.InitOnlyList).IsNotSameReferenceAs(original.InitOnlyList);
        await Assert.That(clone.InitOnlyList.Count).IsEqualTo(1);
        await Assert.That(clone.InitOnlyList[0]).IsNotSameReferenceAs(original.InitOnlyList[0]);
        await Assert.That(clone.InitOnlyList[0].Name).IsEqualTo("NotWorkingTest");
        await Assert.That(clone.InitOnlyList[0].Id).IsEqualTo(1234);
        await Assert.That(clone.InitOnlyList[0].Enabled).IsTrue();
        await Assert.That(clone.SettableList[0].Name).IsEqualTo("WorkingTest");
        await Assert.That(clone.SettableList[0].Id).IsEqualTo(5678);
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task InitOnly_Leaf_Direct_Clone_Should_Still_Copy_Values()
    {
        InitOnlyLeaf original = new InitOnlyLeaf { Id = 1234, Name = "Direct", Enabled = true };

        InitOnlyLeaf clone = original.FastDeepClone();

        await Assert.That(clone).IsNotNull();
        await Assert.That(clone).IsNotSameReferenceAs(original);
        await Assert.That(clone.Name).IsEqualTo("Direct");
        await Assert.That(clone.Id).IsEqualTo(1234);
        await Assert.That(clone.Enabled).IsTrue();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task InitOnly_Nested_Clonable_In_Collection_Should_Deep_Clone()
    {
        NestedInitOnlyHolder original = new NestedInitOnlyHolder
        {
            Items = [new NestedInitOnly { Child = new InitOnlyLeaf { Id = 7, Name = "Nested", Enabled = false } }]
        };

        NestedInitOnlyHolder clone = original.FastDeepClone();

        await Assert.That(clone.Items[0].Child).IsNotSameReferenceAs(original.Items[0].Child);
        await Assert.That(clone.Items[0].Child.Name).IsEqualTo("Nested");
        await Assert.That(clone.Items[0].Child.Id).IsEqualTo(7);
        await Assert.That(clone.Items[0].Child.Enabled).IsFalse();
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task InitOnly_Record_In_Collection_Should_Copy_Values()
    {
        InitOnlyRecordHolder original = new InitOnlyRecordHolder
        {
            Items = [new InitOnlyRecord { Id = 42, Name = "Rec" }]
        };

        InitOnlyRecordHolder clone = original.FastDeepClone();

        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(original.Items[0]);
        await Assert.That(clone.Items[0].Id).IsEqualTo(42);
        await Assert.That(clone.Items[0].Name).IsEqualTo("Rec");
    }

    [Test]
    [SourceGeneratorCompatible]
    public async Task Required_Members_In_Collection_Should_Copy_Values()
    {
        RequiredLeafHolder original = new RequiredLeafHolder
        {
            Items = [new RequiredLeaf { Name = "Req", Id = 9 }]
        };

        RequiredLeafHolder clone = original.FastDeepClone();

        await Assert.That(clone.Items[0]).IsNotSameReferenceAs(original.Items[0]);
        await Assert.That(clone.Items[0].Name).IsEqualTo("Req");
        await Assert.That(clone.Items[0].Id).IsEqualTo(9);
    }
}
