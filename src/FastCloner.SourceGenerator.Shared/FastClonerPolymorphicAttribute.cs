using System;

namespace FastCloner.SourceGenerator.Shared;

/// <summary>
/// Marks a clonable class as a polymorphic cloning root: cloning through a base-type reference
/// preserves the runtime type of the instance. The generated FastDeepClone dispatches to the
/// matching subtype cloner, using the same discovery model as abstract clonable hierarchies:
/// concrete subtypes in the compilation are found automatically, external ones can be registered
/// with <see cref="FastClonerIncludeAttribute"/>, and discovery can be disabled with
/// <see cref="FastClonerDisableAutoDiscoveryAttribute"/>.
/// <br/><br/>
/// Abstract clonable classes are implicitly polymorphic; applying this attribute to them is
/// accepted as explicit self-documentation and has no additional effect.
/// <br/><br/>
/// Cost contract: the exact root type is checked first, so cloning an instance whose runtime type
/// is the marked type itself costs the same as cloning a plain clonable type plus a single type
/// check. Only calls that actually hold a subtype at runtime pay for subtype dispatch.
/// </summary>
/// <example>
/// <code>
/// [FastClonerClonable]
/// [FastClonerPolymorphic]
/// public class Device
/// {
///     public string? Name { get; set; }
/// }
///
/// public class Phone : Device
/// {
///     public string? OS { get; set; }
/// }
///
/// Device device = new Phone { Name = "Pixel", OS = "Android" };
/// Device clone = device.FastDeepClone(); // clone is a Phone, OS included
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class FastClonerPolymorphicAttribute : Attribute
{
}
