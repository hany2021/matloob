namespace Matloob.Domain.Common;

/// <summary>
/// Tri-state value used by PATCH-style domain methods to distinguish
/// "leave this column alone" from "set it to null". The default value
/// (<see cref="NoChange"/>) is the leave-alone case, which lets call sites
/// omit fields they don't intend to touch.
///
/// Example:
/// <code>
/// establishment.UpdateBasicInfo(
///     name: FieldChange.SetTo("Acme"),
///     // description left unchanged
///     additionalContactNumber: FieldChange.Clear&lt;string?&gt;()); // sets it to null
/// </code>
/// </summary>
public readonly struct FieldChange<T>
{
    public bool IsSet { get; }
    public T? Value { get; }

    private FieldChange(T? value, bool isSet)
    {
        Value = value;
        IsSet = isSet;
    }

    public static FieldChange<T> NoChange => default;

    public static FieldChange<T> SetTo(T? value) => new(value, isSet: true);
}

/// <summary>Static factory helpers — keep call sites short.</summary>
public static class FieldChange
{
    public static FieldChange<T> SetTo<T>(T? value) => FieldChange<T>.SetTo(value);
    public static FieldChange<T> Clear<T>() => FieldChange<T>.SetTo(default);
}
