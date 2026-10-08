using Degerli.ContentPipeline;
using Degerli.Core;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateLogger();

try
{
    // Offline, builder-run C4 entrypoint (03 §9): `contentpipeline draft [--symbol=]`.
    // AI drafting runs only here, never in the API or on a user request (FR-RES-019,
    // NFR-RES-006); review/publish happen later through the admin endpoints (FR-RES-020).
    Log.Information("Content pipeline started; shared core {CoreAssembly}.", CoreAssembly.Name);
    return await ContentPipelineCli.RunAsync(args, output: Console.Out, error: Console.Error);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Content pipeline failed.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
