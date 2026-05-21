using Matloob.Api.Infrastructure.Events.Dispatcher;

namespace Matloob.Api.Infrastructure.Events;

/// <summary>
/// Composition root for the events / outbox infrastructure. Wires the
/// scoped <see cref="IOutboxWriter"/>, the scoped
/// <see cref="OutboxDispatcherService"/>, and the hosted
/// <see cref="OutboxDispatcherBackgroundService"/>.
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

        // Dispatcher worker (scoped, one per background pass) + the
        // hosted service that schedules it. The background service
        // short-circuits at startup when Outbox:DispatcherEnabled is
        // false, so registering it unconditionally is safe.
        services.AddScoped<OutboxDispatcherService>();
        services.AddHostedService<OutboxDispatcherBackgroundService>();

        return services;
    }
}
