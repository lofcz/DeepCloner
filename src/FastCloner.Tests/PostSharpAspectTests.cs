using FastCloner.SourceGenerator.Shared;

namespace FastCloner.Tests;

// =================================================================================================
// Reproduction and regression tests for https://github.com/lofcz/FastCloner/issues/48
// "Incompatible with PostSharp's INotifyPropertyChanged aspect"
//
// These types replicate, in compilable C#, the IL shape PostSharp weaves into types instrumented
// with instance-scoped aspects such as [NotifyPropertyChanged] (PostSharp.Patterns.Model.
// NotifyPropertyChangedAttribute). Verified by decompiling PostSharp.Patterns.Model.dll 2026.0.16:
//
//   1. The weaver injects a PRIVATE INSTANCE FIELD storing the per-instance aspect runtime state
//      (aspects derive from InstanceLevelAspect, i.e. from System.Attribute). The field is
//      assigned ONLY by code the weaver injects into constructors.
//   2. Property accessors of the instrumented type are rewritten to dispatch through that field.
//      Invoking them when the field is null (instance created without a constructor) throws
//      NullReferenceException inside weaver code — the reported failure on types without a public
//      parameterless constructor.
//   3. PostSharp replaces MemberwiseClone on instrumented types (ICloneAwareAspect /
//      AspectInitializationReason.Clone): the woven clone copies fields, then re-creates the
//      weaver state on the clone from the prototype. FastCloner uses this hook when present.
//   4. Weaving happens AFTER compilation, so the source generator's view of an instrumented type
//      contains only source members: auto-properties (woven at IL level) and no state field.
//
// PostSharp's MSBuild weaving requires a commercial license key (build error PS0242) and cannot
// run here, so the weaving output is simulated by hand with identical semantics. Two shapes are
// covered:
//   - manual woven accessors (what the woven IL looks like up close), and
//   - auto-properties + injected state (what instrumented source code looks like to the SG).
// =================================================================================================

/// <summary>
/// Per-instance aspect runtime state, mirroring an InstanceLevelAspect-derived aspect instance
/// (e.g. NotifyPropertyChangedAttribute) as decompiled from PostSharp.Patterns.Model. PostSharp
/// aspects are attributes; deriving from <see cref="Attribute"/> is faithful to the real shape.
/// </summary>
public sealed class SimulatedInpcAspect : Attribute
{
    public const int StatusUninitialized = 0;
    public const int StatusInitialized = 1;
    public const int StatusConstructed = 2;

    /// <summary>InstanceLevelAspect.Instance — back-reference to the target object.</summary>
    public object? Instance;

    /// <summary>Aspect initialization status; field semantics from the decompiled aspect.</summary>
    public int InitializationStatus;

    /// <summary>Stands in for the aspect's PropertyChanged subscriber storage.</summary>
    public List<string> RaisedChanges = [];

    /// <summary>Instance identity, so tests can observe how aspect state is propagated.</summary>
    public Guid Id = Guid.NewGuid();

    // --- IInstanceScopedAspect ---
    public SimulatedInpcAspect CreateInstance()
    {
        return new SimulatedInpcAspect();
    }

    public void RuntimeInitializeInstance()
    {
        InitializationStatus = StatusInitialized;
    }

    public void OnInstanceConstructed()
    {
        InitializationStatus = StatusConstructed;
    }

    // --- ICloneAwareAspect ---
    public void OnCloned(SimulatedInpcAspect source)
    {
        InitializationStatus = StatusConstructed;
        RaisedChanges = [.. source.RaisedChanges];
    }

    // --- woven advice (NotifyPropertyChangedAttribute.OnPropertyGet/OnFieldSet) ---
    public T OnPropertyGet<T>(object target, Func<object, T> read)
    {
        if (InitializationStatus != StatusUninitialized)
        {
            // PostSharp dereferences per-instance state here (PropertyLocationRecordStore lookups
            // and child-change tracking via the aspect's Instance back-reference).
            _ = Instance!.GetHashCode();
        }

        return read(target);
    }

    public void OnFieldSet<T>(object target, Action<object, T> write, T value)
    {
        if (InitializationStatus == StatusUninitialized)
        {
            // Aspect degrades to a plain field set when only partially initialized.
            write(target, value);
            return;
        }

        // PostSharp: PropertyChangesTracker.Current.PushOnStack(((InstanceLevelAspect)this).Instance);
        //            LocationBindingExtensions.SetValue(binding, ((InstanceLevelAspect)this).Instance, value);
        // i.e. the woven setter does not write the field itself — the aspect does, through its
        // back-reference. A null aspect field (uninitialized object) or a stale back-reference
        // (deep-cloned/shared aspect state) breaks the write.
        _ = Instance!.GetHashCode();
        write(Instance!, value);
        RaisedChanges.Add("set");
    }
}

/// <summary>
/// View model instrumented with the simulated aspect and WITHOUT a public parameterless
/// constructor — the shape from issue #48 on which cloning always failed. The aspect state field
/// is declared after the properties so property members are processed first: populating a fresh
/// instance through property setters must not happen before (or without) weaver state existing.
/// </summary>
[FastClonerClonable]
public sealed class WovenViewModelWithoutParameterlessCtor
{
    private string nameField = "";
    private int countField;

    public WovenViewModelWithoutParameterlessCtor(string name, int count)
    {
        InitializeAspects(); // injected ctor prologue
        nameField = name;
        countField = count;
        aspect!.OnInstanceConstructed(); // injected ctor epilogue
    }

    public string Name
    {
        // Woven accessor bodies dispatch through the aspect field, exactly like PostSharp rewrites
        // them; the aspect performs the actual field access through its location "binding".
        get => aspect!.OnPropertyGet(this, static t => ((WovenViewModelWithoutParameterlessCtor)t).nameField);
        set => aspect!.OnFieldSet(this, static (t, v) => ((WovenViewModelWithoutParameterlessCtor)t).nameField = v, value);
    }

    public int Count
    {
        get => aspect!.OnPropertyGet(this, static t => ((WovenViewModelWithoutParameterlessCtor)t).countField);
        set => aspect!.OnFieldSet(this, static (t, v) => ((WovenViewModelWithoutParameterlessCtor)t).countField = v, value);
    }

    // ==== injected by the weaver (declared last: fields initialized in ctors only) ====
    private static readonly SimulatedInpcAspect Prototype = CreatePrototype();

    private static SimulatedInpcAspect CreatePrototype()
    {
        SimulatedInpcAspect prototype = new();
        prototype.RuntimeInitializeInstance();
        return prototype;
    }

    private SimulatedInpcAspect? aspect;

    private void InitializeAspects()
    {
        aspect = Prototype.CreateInstance();
        aspect.Instance = this;
        aspect.RuntimeInitializeInstance();
    }

    // diagnostics for tests
    internal bool IsAspectInitialized => aspect is not null;
    internal Guid AspectId => aspect!.Id;
    internal object? AspectBackReference => aspect!.Instance;
}

/// <summary>
/// The shape instrumented source code has at compile time: plain auto-properties (woven into
/// dispatching accessors only after compilation) plus the injected aspect state field, and no
/// public parameterless constructor. This is what the source generator sees for a real
/// PostSharp-instrumented view model.
/// </summary>
[FastClonerClonable]
public sealed class WovenAutoPropViewModelWithoutParameterlessCtor
{
    public string Title { get; set; } = "";

    public int Weight { get; set; }

    public WovenAutoPropViewModelWithoutParameterlessCtor(string title, int weight)
    {
        Title = title;
        Weight = weight;
        aspect = Prototype.CreateInstance();
        aspect.Instance = this;
        aspect.RuntimeInitializeInstance();
        aspect.OnInstanceConstructed();
    }

    // ==== injected by the weaver ====
    private static readonly SimulatedInpcAspect Prototype = CreatePrototype();

    private static SimulatedInpcAspect CreatePrototype()
    {
        SimulatedInpcAspect prototype = new();
        prototype.RuntimeInitializeInstance();
        return prototype;
    }

    private SimulatedInpcAspect? aspect;

    internal bool IsAspectInitialized => aspect is not null;
    internal Guid AspectId => aspect!.Id;
}

/// <summary>
/// Control: identical weaving, but WITH a public parameterless constructor. Constructors of
/// instrumented types initialize the aspect state, so instance-creation via `new T()` is safe.
/// </summary>
[FastClonerClonable]
public sealed class WovenViewModelWithParameterlessCtor
{
    private static readonly SimulatedInpcAspect Prototype = CreatePrototype();

    private static SimulatedInpcAspect CreatePrototype()
    {
        SimulatedInpcAspect prototype = new();
        prototype.RuntimeInitializeInstance();
        return prototype;
    }

    private SimulatedInpcAspect? aspect;

    private string nameField = "";

    public WovenViewModelWithParameterlessCtor()
    {
        InitializeAspects();
        aspect!.OnInstanceConstructed();
    }

    public string Name
    {
        get => aspect!.OnPropertyGet(this, static t => ((WovenViewModelWithParameterlessCtor)t).nameField);
        set => aspect!.OnFieldSet(this, static (t, v) => ((WovenViewModelWithParameterlessCtor)t).nameField = v, value);
    }

    internal bool IsAspectInitialized => aspect is not null;
    internal Guid AspectId => aspect!.Id;

    private void InitializeAspects()
    {
        aspect = Prototype.CreateInstance();
        aspect.Instance = this;
        aspect.RuntimeInitializeInstance();
    }
}

/// <summary>
/// PostSharp instruments MemberwiseClone of enhanced types as well (ICloneAwareAspect /
/// AspectInitializationReason.Clone): the woven MemberwiseClone performs the base copy and then
/// re-creates the aspect state on the clone from the prototype, notifying OnCloned. Replicated
/// here so the runtime cloner can use the hook as its copy primitive.
/// </summary>
[FastClonerClonable]
public sealed class WovenViewModelWithCloneIntercept
{
    private static readonly SimulatedInpcAspect Prototype = CreatePrototype();

    private static SimulatedInpcAspect CreatePrototype()
    {
        SimulatedInpcAspect prototype = new();
        prototype.RuntimeInitializeInstance();
        return prototype;
    }

    private SimulatedInpcAspect? aspect;

    private string nameField = "";

    public WovenViewModelWithCloneIntercept(string name)
    {
        aspect = Prototype.CreateInstance();
        aspect.Instance = this;
        aspect.RuntimeInitializeInstance();
        nameField = name;
        aspect.OnInstanceConstructed();
    }

    public string Name
    {
        get => aspect!.OnPropertyGet(this, static t => ((WovenViewModelWithCloneIntercept)t).nameField);
        set => aspect!.OnFieldSet(this, static (t, v) => ((WovenViewModelWithCloneIntercept)t).nameField = v, value);
    }

    public new object MemberwiseClone()
    {
        WovenViewModelWithCloneIntercept clone = (WovenViewModelWithCloneIntercept)base.MemberwiseClone();
        clone.aspect = Prototype.CreateInstance();
        clone.aspect.Instance = clone;
        clone.aspect.RuntimeInitializeInstance();
        clone.aspect.OnCloned(aspect!);
        return clone;
    }

    internal bool IsAspectInitialized => aspect is not null;
    internal Guid AspectId => aspect!.Id;
    internal object? AspectBackReference => aspect!.Instance;
}

public class PostSharpAspectTests(int maxRecursionDepth) : BaseTestFixture(maxRecursionDepth)
{
    [Test]
    public async Task Reflection_Clone_Instrumented_Type_Without_Parameterless_Ctor_Should_Succeed_With_Usable_State()
    {
        WovenViewModelWithoutParameterlessCtor vm = new("original", 7);

        WovenViewModelWithoutParameterlessCtor clone = vm.DeepClone();

        // The clone is populated through field storage, never through woven accessors, and keeps
        // weaver state instead of crashing with NullReferenceException inside aspect code.
        await Assert.That(clone.Name).IsEqualTo("original");
        await Assert.That(clone.Count).IsEqualTo(7);
        await Assert.That(clone.IsAspectInitialized).IsTrue();

        // Weaver state is shared with the source (MemberwiseClone semantics without the weaver's
        // own clone hook) — it must not be deep-cloned, which would corrupt it.
        await Assert.That(clone.AspectId).IsEqualTo(vm.AspectId);
        await Assert.That(clone.AspectBackReference).IsSameReferenceAs(vm);

        // Reads through woven accessors work on the clone.
        WovenViewModelWithoutParameterlessCtor reread = clone;
        await Assert.That(reread.Name).IsEqualTo("original");
    }

    [Test]
    public async Task SourceGenerator_Clone_Instrumented_Type_Without_Parameterless_Ctor_Should_Not_Throw()
    {
        // Before the fix this threw NullReferenceException inside the simulated aspect advice:
        // the SG created the instance via GetUninitializedObject (no ctor => no aspect state)
        // and then populated it by invoking the woven property setter.
        WovenViewModelWithoutParameterlessCtor vm = new("original", 7);

        WovenViewModelWithoutParameterlessCtor? clone = vm.FastDeepClone();

        await Assert.That(clone).IsNotNull();
        await Assert.That(clone!.Name).IsEqualTo("original");
        await Assert.That(clone.Count).IsEqualTo(7);
        await Assert.That(clone.IsAspectInitialized).IsTrue();

        // On instances created without a constructor the SG never invokes property setters; data
        // flows through the backing fields, and weaver state is shared rather than left null.
        await Assert.That(clone.AspectId).IsEqualTo(vm.AspectId);
    }

    [Test]
    public async Task SourceGenerator_Clone_AutoProp_Instrumented_Type_Without_Parameterless_Ctor_Should_Populate_Storage()
    {
        // The realistic PostSharp shape as seen at compile time: auto-properties woven after
        // compilation. The SG must write <Title>k__BackingField directly instead of invoking the
        // (woven) setter on an instance that never ran a constructor.
        WovenAutoPropViewModelWithoutParameterlessCtor vm = new("hello", 3);

        WovenAutoPropViewModelWithoutParameterlessCtor? clone = vm.FastDeepClone();

        await Assert.That(clone).IsNotNull();
        await Assert.That(clone!.Title).IsEqualTo("hello");
        await Assert.That(clone.Weight).IsEqualTo(3);
        await Assert.That(clone.IsAspectInitialized).IsTrue();
        await Assert.That(clone.AspectId).IsEqualTo(vm.AspectId);
    }

    [Test]
    public async Task Reflection_Clone_AutoProp_Instrumented_Type_Without_Parameterless_Ctor_Should_Work()
    {
        WovenAutoPropViewModelWithoutParameterlessCtor vm = new("hello", 3);

        WovenAutoPropViewModelWithoutParameterlessCtor clone = vm.DeepClone();

        await Assert.That(clone.Title).IsEqualTo("hello");
        await Assert.That(clone.Weight).IsEqualTo(3);
        await Assert.That(clone.IsAspectInitialized).IsTrue();
        await Assert.That(clone.AspectId).IsEqualTo(vm.AspectId);
    }

    [Test]
    public async Task Reflection_Clone_Instrumented_Type_With_Parameterless_Ctor_Should_Succeed()
    {
        WovenViewModelWithParameterlessCtor vm = new() { Name = "original" };

        WovenViewModelWithParameterlessCtor clone = vm.DeepClone();

        await Assert.That(clone.Name).IsEqualTo("original");
        await Assert.That(clone.IsAspectInitialized).IsTrue();
        // The reflection path copies via MemberwiseClone: without a weaver clone hook the aspect
        // state is shared (MemberwiseClone semantics), never deep-cloned.
        await Assert.That(clone.AspectId).IsEqualTo(vm.AspectId);
    }

    [Test]
    public async Task SourceGenerator_Clone_Instrumented_Type_With_Parameterless_Ctor_Should_Produce_Independent_Clone()
    {
        WovenViewModelWithParameterlessCtor vm = new() { Name = "original" };

        WovenViewModelWithParameterlessCtor? clone = vm.FastDeepClone();

        await Assert.That(clone).IsNotNull();
        await Assert.That(clone!.Name).IsEqualTo("original");
        await Assert.That(clone.IsAspectInitialized).IsTrue();
        // Constructor-created state must be preserved — not overwritten with the source's state.
        await Assert.That(clone.AspectId).IsNotEqualTo(vm.AspectId);

        clone.Name = "changed";
        await Assert.That(clone.Name).IsEqualTo("changed");
        await Assert.That(vm.Name).IsEqualTo("original");
    }

    [Test]
    public async Task Reflection_Clone_Instrumented_Type_With_Clone_Intercept_Should_Use_Weaver_Hook()
    {
        WovenViewModelWithCloneIntercept vm = new("original");

        WovenViewModelWithCloneIntercept clone = vm.DeepClone();

        await Assert.That(clone.Name).IsEqualTo("original");
        await Assert.That(clone.IsAspectInitialized).IsTrue();
        // The woven MemberwiseClone re-created weaver state on the clone: fresh aspect instance
        // bound to the clone. The member-copy pass must leave it untouched.
        await Assert.That(clone.AspectId).IsNotEqualTo(vm.AspectId);
        await Assert.That(clone.AspectBackReference).IsSameReferenceAs(clone);

        clone.Name = "changed";
        await Assert.That(clone.Name).IsEqualTo("changed");
        await Assert.That(vm.Name).IsEqualTo("original");
    }

    [Test]
    public async Task SourceGenerator_Clone_Instrumented_Type_With_Clone_Intercept_Should_Not_Throw()
    {
        WovenViewModelWithCloneIntercept vm = new("original");

        WovenViewModelWithCloneIntercept? clone = vm.FastDeepClone();

        await Assert.That(clone).IsNotNull();
        await Assert.That(clone!.Name).IsEqualTo("original");
        await Assert.That(clone.IsAspectInitialized).IsTrue();
    }

    [Test]
    public async Task DeepCloneTo_Instrumented_Type_Should_Keep_Target_Weaver_State()
    {
        WovenViewModelWithParameterlessCtor source = new() { Name = "source" };
        WovenViewModelWithParameterlessCtor target = new() { Name = "target" };
        Guid targetAspectId = target.AspectId;

        source.DeepCloneTo(target);

        await Assert.That(target.Name).IsEqualTo("source");
        // The destination keeps its own constructor-initialized weaver state instead of receiving
        // a deep clone of the source's state.
        await Assert.That(target.AspectId).IsEqualTo(targetAspectId);
    }
}
