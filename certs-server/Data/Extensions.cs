using System;

using Microsoft.EntityFrameworkCore;

namespace CertsServer.Data;

public static class Extensions
{
    public static IServiceCollection AddSqliteModelCreating<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddSingleton<ModelCreatingOptions<TDbContext>>(new ModelCreatingOptions<TDbContext>
        {
            ModelCreatingAction = modelBuilder => ModelCreatingExtensions.ApplySqliteConfigurations(modelBuilder)
        });

        return services;
    }
}
