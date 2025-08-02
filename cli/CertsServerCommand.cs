using System;
using System.CommandLine;

using Microsoft.EntityFrameworkCore.Storage;

namespace CertsServer.Cli;

public class CertsServerCommand : RootCommand
{
    public CertsServerCommand() : base("CertsServer commandline")
    {
        Add(new MigrationsCommand());
        Add(new DatabaseCommand());
    }
}
