using CertsServer.Cli;

var command = new CertsServerCommand();

try
{
    return command.Parse(args).Invoke();
}
catch(Exception ex)
{
    Console.WriteLine(ex.Message);
    throw;
    // return 1;
}