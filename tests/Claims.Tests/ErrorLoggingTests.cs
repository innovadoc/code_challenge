using Microsoft.Data.Sqlite;

namespace Claims.Tests;

// Every exception raised by the search is recorded for troubleshooting and still reaches the caller.
public class ErrorLoggingTests
{
    private sealed class RecordingLog : IErrorLog
    {
        public List<(Exception Exception, string Context)> Entries { get; } = new();
        public void Log(Exception exception, string context) => Entries.Add((exception, context));
    }

    [Fact]
    public void Search_RejectedSort_IsLoggedAndRethrown()
    {
        var log = new RecordingLog();
        var repo = new ClaimsRepository(Db.CreateSeeded(), log);

        var thrown = Assert.ThrowsAny<ArgumentException>(() => repo.Search(1, "dental", "amount, (SELECT 1)"));

        var entry = Assert.Single(log.Entries);
        Assert.Same(thrown, entry.Exception);
        Assert.Contains("amount, (SELECT 1)", entry.Context);
    }

    [Fact]
    public void Search_DatabaseFailure_IsLoggedAndRethrown()
    {
        var db = Db.CreateSeeded();
        using (var drop = db.CreateCommand())
        {
            drop.CommandText = "DROP TABLE claims";
            drop.ExecuteNonQuery();
        }
        var log = new RecordingLog();

        var thrown = Assert.Throws<SqliteException>(() => new ClaimsRepository(db, log).Search(1, "dental"));

        var entry = Assert.Single(log.Entries);
        Assert.Same(thrown, entry.Exception);
        Assert.Contains("memberId=1", entry.Context);
    }

    [Fact]
    public void Search_Success_LogsNothing()
    {
        var log = new RecordingLog();

        new ClaimsRepository(Db.CreateSeeded(), log).Search(1, "dental");

        Assert.Empty(log.Entries);
    }

    [Fact]
    public void Search_LogContext_OmitsTheSearchTextAndCannotForgeLogLines()
    {
        var log = new RecordingLog();
        var repo = new ClaimsRepository(Db.CreateSeeded(), log);

        Assert.ThrowsAny<ArgumentException>(() => repo.Search(1, "private diagnosis", "x\r\n2026-01-01 | forged entry"));

        var context = Assert.Single(log.Entries).Context;
        Assert.DoesNotContain("private diagnosis", context);
        Assert.DoesNotContain('\r', context);
        Assert.DoesNotContain('\n', context);
    }

    [Fact]
    public void FileErrorLog_AppendsEachEntryToTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "claims-log-" + Guid.NewGuid().ToString("N"), "errors.log");
        try
        {
            var log = new FileErrorLog(path);

            log.Log(new InvalidOperationException("first failure"), "context one");
            log.Log(new ArgumentException("second failure"), "context two");

            var content = File.ReadAllText(path);
            Assert.Contains("System.InvalidOperationException: first failure", content);
            Assert.Contains("context one", content);
            Assert.Contains("System.ArgumentException: second failure", content);
            Assert.Contains("context two", content);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void Search_WithoutAnExplicitLog_WritesToTheDefaultLogFile()
    {
        var marker = "bad-" + Guid.NewGuid().ToString("N");

        Assert.ThrowsAny<ArgumentException>(() => new ClaimsRepository(Db.CreateSeeded()).Search(1, "dental", marker));

        // Other tests may be appending at the same moment, so the read must not lock the file.
        using var stream = new FileStream(FileErrorLog.DefaultPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        Assert.Contains(marker, reader.ReadToEnd());
    }
}
