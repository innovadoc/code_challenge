namespace Claims;

// Where failures are recorded for troubleshooting. The repository depends on this, not on a file.
public interface IErrorLog
{
    void Log(Exception exception, string context);
}
