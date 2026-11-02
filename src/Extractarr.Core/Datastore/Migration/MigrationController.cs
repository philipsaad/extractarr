using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using Extractarr.Common.Instrumentation;

namespace Extractarr.Core.Datastore.Migration;

public interface IMigrationController
{
    void Migrate();
}

public class MigrationController : IMigrationController
{
    private readonly IDatabase _database;
    private static readonly Logger Logger = ExtractarrLogger.GetLogger<MigrationController>();

    public MigrationController(IDatabase database)
    {
        _database = database;
    }

    public void Migrate()
    {
        Logger.Info("*** Migrating SQLite database: {0} ***", _database.ConnectionString);

        var serviceProvider = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSQLite()
                .WithGlobalConnectionString(_database.ConnectionString)
                .ScanIn(typeof(MigrationController).Assembly).For.Migrations())
            .BuildServiceProvider(false);

        using var scope = serviceProvider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        runner.MigrateUp();

        Logger.Info("*** SQLite database migration completed successfully ***");
    }
}

