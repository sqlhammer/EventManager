namespace EventManager.Domain;

/// <summary>
/// A 64-bit Snowflake identifier (D-26): time-sortable, minted at origin, used for every
/// cross-app / event-log identity. Wraps a <see cref="long"/> for type safety at call sites.
/// Generation lives in EventManager.Sync; this type is the shared identity value.
/// </summary>
public readonly record struct Snowflake(long Value) : IComparable<Snowflake>
{
    public int CompareTo(Snowflake other) => Value.CompareTo(other.Value);

    public static implicit operator long(Snowflake id) => id.Value;
    public static explicit operator Snowflake(long value) => new(value);

    public override string ToString() => Value.ToString();
}
