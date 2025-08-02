using System;
using System.CommandLine;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CertsServer.Cli;

public class DatabaseCommand : Command
{
    public DatabaseCommand() : base("database")
    {
        Add(new UpdateCommand());
    }

    public class UpdateCommand : Command
    {
        public UpdateCommand() : base("update")
        {
            var targetOption = new Option<string>("--target");

            Add(targetOption);

            SetAction(parseResult =>
            {
                var target = parseResult.GetValue(targetOption);

                return UpdateDatabase(parseResult.Configuration.Output, target);
            });
        }

        public static int UpdateDatabase(TextWriter writer, string? target)
        {
            try
            {
                var dbContext = DbContextFactory.Create(writer);

                var pending = dbContext.Database.GetPendingMigrations();

                if (pending.IsNullOrEmpty())
                {
                    writer.WriteLine("No pending migrations.");
                    return 0;
                }

                writer.WriteLine("Pending migrations:");
                foreach (var p in pending)
                {
                    writer.WriteLine(p);
                }

                dbContext.Database.Migrate(target);
                writer.WriteLine("Done.");

                return 0;
            }
            catch (System.Exception ex)
            {
                writer.WriteLine(ex.ToString());
                return 1;
            }
        }
    }
}
