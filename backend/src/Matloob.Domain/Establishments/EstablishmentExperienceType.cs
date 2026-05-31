namespace Matloob.Domain.Establishments;

/// <summary>
/// Discriminator for an <see cref="EstablishmentExperience"/>'s category — the
/// legacy polymorphic morph collapsed to a two-value enum. <c>Event</c>
/// categories reference an event type; <c>Opportunity</c> categories reference
/// an opportunity category. Stored as the lowercase wire token.
/// </summary>
public enum EstablishmentExperienceType
{
    Event = 0,
    Opportunity = 1,
}

public static class EstablishmentExperienceTypeWire
{
    public static string ToWire(this EstablishmentExperienceType value) => value switch
    {
        EstablishmentExperienceType.Event => "event",
        EstablishmentExperienceType.Opportunity => "opportunity",
        _ => "event",
    };

    public static string Label(this EstablishmentExperienceType value) => value switch
    {
        EstablishmentExperienceType.Event => "Event",
        EstablishmentExperienceType.Opportunity => "Opportunity",
        _ => "Event",
    };

    public static bool TryParse(string? token, out EstablishmentExperienceType value)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case "event": value = EstablishmentExperienceType.Event; return true;
            case "opportunity": value = EstablishmentExperienceType.Opportunity; return true;
            default: value = EstablishmentExperienceType.Event; return false;
        }
    }
}
