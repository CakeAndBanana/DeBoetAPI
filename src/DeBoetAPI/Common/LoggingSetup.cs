using Serilog;

namespace DeBoetAPI.Common;

public sealed class FileLoggingOptions
{
    public const string SectionName = "FileLogging";

    public bool Enabled { get; set; } = true;

    public string Directory { get; set; } = "logs";

    public string FileName { get; set; } = "deboetapi-.log";

    public int RetainedFileCountLimit { get; set; } = 30;

    public long FileSizeLimitBytes { get; set; } = 100L * 1024 * 1024;
}

public static class LoggingSetup
{
    private const string OutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}{NewLine}  {Message:lj}{NewLine}{Exception}";

    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    public static void AddAppLogging(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(FileLoggingOptions.SectionName).Get<FileLoggingOptions>()
                      ?? new FileLoggingOptions();

        var (writeToFile, logPath, failure) = ResolveLogPath(options);

        if (failure is not null)
        {
            Console.Error.WriteLine(failure);
        }

        builder.Services.AddSerilog((services, config) =>
        {
            config
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .WriteTo.Console(outputTemplate: ConsoleTemplate);

            if (writeToFile)
            {
                config.WriteTo.File(
                    path: logPath!,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: options.RetainedFileCountLimit,
                    fileSizeLimitBytes: options.FileSizeLimitBytes,
                    rollOnFileSizeLimit: true,
                    shared: true,
                    outputTemplate: OutputTemplate);
            }
        });
    }

    private static (bool WriteToFile, string? Path, string? Failure) ResolveLogPath(FileLoggingOptions options)
    {
        if (!options.Enabled)
        {
            return (false, null, null);
        }

        try
        {
            var directory = Path.IsPathRooted(options.Directory)
                ? options.Directory
                : Path.Combine(AppContext.BaseDirectory, options.Directory);

            System.IO.Directory.CreateDirectory(directory);
            return (true, Path.Combine(directory, options.FileName), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return (false, null,
                $"File logging is disabled: could not prepare '{options.Directory}' ({ex.Message}). " +
                "Console logging continues.");
        }
    }

}
