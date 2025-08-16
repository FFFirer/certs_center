using System;
using System.CommandLine;
using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;

namespace CertsServer.Cli;

public class DatabaseCommand : Command
{
    readonly static Option<string> ConnectionStringOption = new Option<string>("--connection");

    public DatabaseCommand() : base("database")
    {

        Add(new UpdateCommand());
        Add(new DropCommand());
        Add(new DescribeCommand());
    }

    public class DescribeCommand : Command
    {
        public DescribeCommand() : base("describe")
        {
            Add(ConnectionStringOption);
            SetAction(parseResult =>
            {
                var connectionString = parseResult.GetValue(ConnectionStringOption);

                return DescribeDatabase(parseResult.Configuration.Output, connectionString);
            });
        }

        private void DescribeSqliteDatabases(TextWriter output, DbConnection connection)
        {
            var command = connection.CreateCommand();
            command.CommandText = $@"SELECT name FROM sqlite_master WHERE type='table';";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                output.WriteLine(reader["name"]);
            }
        }

        private int DescribeDatabase(TextWriter output, string? connectionString)
        {
            try
            {
                var dbContext = DbContextFactory.Create(output, connectionString);

                Action<TextWriter, DbConnection> describeDatabase = dbContext.Database.IsSqlite()
                    ? DescribeSqliteDatabases
                    : throw new NotSupportedException("Not supported database for describe");

                using var connection = dbContext.Database.GetDbConnection();

                try
                {
                    connection.Open();
                    describeDatabase?.Invoke(output, connection);
                }
                finally
                {
                    connection?.Close();
                }

                return 0;
            }
            catch (System.Exception ex)
            {
                output.WriteLine(ex.ToString());
                return 1;
            }
        }
    }

    public class DropCommand : Command
    {
        public DropCommand() : base("drop")
        {
            Add(ConnectionStringOption);

            SetAction(parseResult =>
            {
                var connection = parseResult.GetValue(ConnectionStringOption);

                return DropDatabase(parseResult.Configuration.Output, connection);
            });
        }

        public static int DropDatabase(TextWriter writer, string? connectionString)
        {
            try
            {
                var dbContext = DbContextFactory.Create(writer, connectionString);

                var applied = dbContext.Database.GetAppliedMigrations();

                writer.WriteLine("Applied migrations:");
                foreach (var p in applied)
                {
                    writer.WriteLine(p);
                }

                dbContext.Database.EnsureDeleted();


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

    public class UpdateCommand : Command
    {
        public UpdateCommand() : base("update")
        {
            var targetOption = new Option<string>("--target");

            Add(ConnectionStringOption);
            Add(targetOption);

            SetAction(parseResult =>
            {
                var target = parseResult.GetValue(targetOption);
                var connection = parseResult.GetValue(ConnectionStringOption);

                return UpdateDatabase(parseResult.Configuration.Output, target, connection);
            });
        }

        public static int UpdateDatabase(TextWriter writer, string? target, string? connectionString)
        {
            try
            {
                var dbContext = DbContextFactory.Create(writer, connectionString);

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
