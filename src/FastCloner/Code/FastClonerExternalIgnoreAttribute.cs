namespace FastCloner.Code;

/// <summary>
/// Assembly-level attribute that registers external attribute types to be treated as
/// <see cref="FastClonerIgnoreAttribute"/> during cloning. This lets models that already carry
/// framework-level ignore attributes (e.g. <c>[JsonIgnore]</c>, <c>[BsonIgnore]</c>, <c>[NotMapped]</c>)
/// be honored by FastCloner without stacking additional attributes on every member.
/// </summary>
/// <example>
/// <code>
/// [assembly: FastClonerExternalIgnore(typeof(JsonIgnoreAttribute), typeof(BsonIgnoreAttribute))]
///
/// public class MyDto
/// {
///     [JsonIgnore]          // now also ignored by FastCloner
///     public string Secret { get; set; }
/// }
/// </code>
/// </example>
/// <remarks>
/// <para>
/// The registration applies to members declared in the assembly that carries this attribute,
/// which keeps behavior deterministic and identical between the reflection-based cloner and
/// the source generator: the source generator reads the same attribute at compile time, so no
/// runtime registration or cache invalidation is needed.
/// </para>
/// <para>
/// Attributes are matched by exact attribute type on presence only; constructors and properties
/// of the external attribute (e.g. <c>JsonIgnore(Condition = ...)</c>) are not evaluated.
/// </para>
/// <para>
/// Precedence: explicit member-level FastCloner attributes (e.g. <c>[FastClonerBehavior(CloneBehavior.Clone)]</c>)
/// always win over registered external attributes.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class FastClonerExternalIgnoreAttribute : Attribute
{
    /// <summary>
    /// Gets the external attribute types that will be treated as <see cref="FastClonerIgnoreAttribute"/>.
    /// </summary>
    public Type[] AttributeTypes { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="FastClonerExternalIgnoreAttribute"/>.
    /// </summary>
    /// <param name="attributeTypes">Attribute types such as <c>JsonIgnoreAttribute</c> that should be honored as ignore markers.</param>
    public FastClonerExternalIgnoreAttribute(params Type[] attributeTypes)
    {
        if (attributeTypes is null || attributeTypes.Length == 0)
            throw new ArgumentException("At least one attribute type must be registered.", nameof(attributeTypes));

        foreach (Type attributeType in attributeTypes)
        {
            if (attributeType is null)
                throw new ArgumentException("Attribute types must not contain null values.", nameof(attributeTypes));

            if (!typeof(Attribute).IsAssignableFrom(attributeType))
                throw new ArgumentException($"'{attributeType.FullName}' is not an Attribute type.", nameof(attributeTypes));
        }

        AttributeTypes = attributeTypes;
    }
}
