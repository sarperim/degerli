using Degerli.Core;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateLogger();

try
{
    Log.Information("Content pipeline started; shared core {CoreAssembly}.", CoreAssembly.Name);
    // Drafting and baseline-regeneration stages are added by their domain tickets.
    return 0;
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
