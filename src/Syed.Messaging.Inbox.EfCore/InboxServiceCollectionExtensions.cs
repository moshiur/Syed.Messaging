using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Syed.Messaging.Inbox.EfCore;

public static class InboxServiceCollectionExtensions
{
    public static IServiceCollection AddEfInboxStore<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<IInboxStore, EfInboxStore<TDbContext>>();
        return services;
    }
}
