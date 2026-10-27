using OMSIScript.LanguageServer;

if (args.Contains("--version"))
{
    Console.WriteLine(typeof(Server).Assembly.GetName().Version);
    return 0;
}

var server = new Server(Console.OpenStandardInput(), Console.OpenStandardOutput());
await server.RunAsync();
return server.ExitCode;
