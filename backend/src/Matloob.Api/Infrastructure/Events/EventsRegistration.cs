namespace Matloob.Api.Infrastructure.Events;

/// <summary>
/// Composition root for the events / outbox infrastructure. Wires the
/// scoped <see cref="IOutboxWriter"/> and binds <see cref="OutboxOptions"/>.
/// The dispatcher background service is registered alongside it in a
/// later commit of this phase.
/// </summary>
public static class EventsRegistration
{
    public static IServiceCollection AddMatloobOutbox(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName));

        // Writer is request-scoped: same lifetime as the AppDbContext it
        // writes onto, so its in-memory staging list disappears together
        // with the request when an endpoint fails before SaveChanges.
        services.AddScoped<IOutboxWriter, EfOutboxWriter>();

        return services;
    }
}
