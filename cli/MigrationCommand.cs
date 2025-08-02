using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Security.Cryptography.X509Certificates;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;

namespace CertsServer.Cli;

public class MigrationsCommand : Command
{
    public MigrationsCommand() : base("migrations", "About migrations")
    {
        Add(new ListCommand());
        Add(new ScriptCommand());
    }

    public class ScriptCommand : Command
    {
        public ScriptCommand() : base("script", "")
        {
            var fromOption = new Option<string>("--from");
            var toOption = new Option<string>("--to");
            var outputOption = new Option<string>("--output", ["-o"])
            {
                DefaultValueFactory = _ => "./migrate.sql"
            };

            Add(fromOption);
            Add(toOption);
            Add(outputOption);

            this.SetAction(parseResult =>
            {
                var output = parseResult.Configuration.Output;

                try
                {
                    var from = parseResult.GetValue(fromOption);
                    var to = parseResult.GetValue(toOption);
                    var outputPath = parseResult.GetValue(outputOption)!;

                    var dbContext = DbContextFactory.Create(parseResult.Configuration.Output);

                    var migrator = dbContext.GetService<IMigrator>();

                    var script = migrator.GenerateScript(from, to);

                    using (var fs = new FileStream(outputPath, FileMode.OpenOrCreate, FileAccess.Write))
                    {
                        using var sw = new StreamWriter(fs);
                        sw.Write(script);
                    }

                    var file = new FileInfo(outputPath);
                    output.WriteLine("Output: {0}", file.FullName);

                    return 0;
                }
                catch (System.Exception ex)
                {
                    output.WriteLine(ex.ToString());
                    return 1;
                }
            });
        }
    }

    public class ListCommand : Command
    {
        public ListCommand() : base("list", "")
        {
            this.SetAction((parseResult) =>
            {
                try
                {
                    var output = parseResult.Configuration.Output;
                    var dbContext = DbContextFactory.Create(parseResult.Configuration.Output);

                    var applied = dbContext.Database.GetAppliedMigrations();
                    if (applied.IsNullOrEmpty() == false)
                    {
                        output.WriteLine("Applied:");
                        foreach (var m in applied)
                        {
                            output.WriteLine(m);
                        }
                    }

                    var pendings = dbContext.Database.GetPendingMigrations();
                    if (pendings.IsNullOrEmpty() == false)
                    {
                        output.WriteLine("Pending:");
                        foreach (var m in pendings)
                        {
                            output.WriteLine(m);
                        }
                    }

                    return 0;
                }

                catch (Exception ex)
                {
                    parseResult.Configuration.Output.WriteLine(ex.ToString());
                    return 1;
                }
            });
        }

        const string INITIAL = "InitialCreate";
    }
}
