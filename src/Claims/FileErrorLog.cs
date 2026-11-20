namespace Claims;

// Appends one entry per failure to a text file: UTC time, context, and the exception with its stack trace.
public sealed class FileErrorLog : IErrorLog
{
    private static readonly object Gate = new();

    public static string DefaultPath { get; } = Path.Combine(AppContext.BaseDirectory, "logs", "claims-errors.log");

    private readonly string _path;

    public FileErrorLog(string? path = null) => _path = Path.GetFullPath(path ?? DefaultPath);

    public void Log(Exception exception, string context)
    {
        var entry = $"{DateTimeOffset.UtcNow:O} | {context} | {exception}{Environment.NewLine}";
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, entry);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failure to write the log must not replace the error the caller needs to see.
        }
    }
}
