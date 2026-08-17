using Serilog;

namespace DeBoetAPI.Common;

public sealed class FileLoggingOptions
{
    public const string SectionName = "FileLogging";

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Where the log files live. Relative paths resolve against the application directory
    /// rather than the current working directory, which differs between `dotnet run`, a
    /// published binary and the container.
    /// </summary>
    public string Directory { get; set; } = "logs";

    /// <summary>Base name. The date is inserted before the extension: <c>deboetapi-20260817.log</c>.</summary>
    public string FileName { get; set; } = "deboetapi-.log";

    /// <summary>How many daily files to keep. Older ones are deleted as new days roll over.</summary>
    public int RetainedFileCountLimit { get; set; } = 30;

    /// <summary>
    /// Cap for a single day's file. Reaching it starts <c>…_001.log</c> rather than letting one
    /// bad day fill the disk — daily rollover alone gives no size ceiling.
    /// </summary>
    public long FileSizeLimitBytes { get; set; } = 100L * 1024 * 1024;
}

public static class LoggingSetup
{
    private const string OutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}{NewLine}  {Message:lj}{NewLine}{Exception}";

    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Console plus a file that starts a new one each calendar day. Registered through DI
    /// rather than the static <c>Log.Logger</c>, so tests that spin up several hosts in one
    /// process do not fight over a shared logger.
    /// </summary>
    public static void AddAppLogging(this WebApplicationBuilder builder)
    {
        var options = builder.Configuration.GetSection(FileLoggingOptions.SectionName).Get<FileLoggingOptions>()
                      ?? new FileLoggingOptions();

        var (writeToFile, logPath, failure) = ResolveLogPath(options);

        if (failure is not null)
        {
            // The logger does not exist yet, so this is the one place stderr is the right channel.
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

    /// <summary>
    /// Creates the log directory up front. A container with a read-only or wrongly-owned mount
    /// should still start and log to the console — losing the file sink is bad, but refusing to
    /// boot over it is worse.
    /// </summary>
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
