using Microsoft.Data.Sqlite;

namespace Claims;

public class ClaimsRepository
{
    private const int PageSize = 20;
    private const int MaxTextLength = 200;

    // MYT-3108: the SQL is built only from these constants. Caller input never becomes part of the SQL text:
    // values travel as parameters, and the sort (which cannot be a parameter) selects one of six fixed queries.
    private const string Select =
        "SELECT id, provider, description, amount, status, submitted_at FROM claims " +
        "WHERE member_id = @member AND (description LIKE @pattern ESCAPE '\\' OR provider LIKE @pattern ESCAPE '\\') " +
        "ORDER BY ";
    private const string Paging = " LIMIT 20 OFFSET @offset";

    private readonly SqliteConnection _db;
    private readonly IErrorLog _log;

    public ClaimsRepository(SqliteConnection db, IErrorLog? log = null)
    {
        _db = db;
        _log = log ?? new FileErrorLog();
    }

    // Search the claims of one member. Called by the claims page of the member website and by the support tool.
    public IReadOnlyList<Claim> Search(int memberId, string? text, string? sortBy = "date", string? sortDir = "desc", int page = 0)
    {
        try
        {
            return Run(memberId, text, sortBy, sortDir, page);
        }
        catch (Exception ex)
        {
            // The search text is left out on purpose: it can hold personal health information.
            _log.Log(ex, $"ClaimsRepository.Search memberId={memberId} textLength={text?.Length ?? 0} " +
                         $"sortBy={Printable(sortBy)} sortDir={Printable(sortDir)} page={page}");
            throw;
        }
    }

    private IReadOnlyList<Claim> Run(int memberId, string? text, string? sortBy, string? sortDir, int page)
    {
        var sql = SearchSql(sortBy, sortDir);
        if (page < 0)
            throw new ArgumentOutOfRangeException(nameof(page), "page cannot be negative.");
        text ??= "";
        if (text.Length > MaxTextLength)
            throw new ArgumentException($"text cannot be longer than {MaxTextLength} characters.", nameof(text));

        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@member", memberId);
        cmd.Parameters.AddWithValue("@pattern", ContainsPattern(text));
        cmd.Parameters.AddWithValue("@offset", (long)page * PageSize);

        var results = new List<Claim>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(new Claim(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                                  reader.GetDouble(3), reader.GetString(4), reader.GetString(5)));
        return results;
    }

    // Allow-list: only the three public sort options map to a query; anything else is rejected.
    private static string SearchSql(string? sortBy, string? sortDir)
    {
        var ascending = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        return sortBy switch
        {
            "date" => ascending ? Select + "submitted_at ASC" + Paging : Select + "submitted_at DESC" + Paging,
            "amount" => ascending ? Select + "amount ASC" + Paging : Select + "amount DESC" + Paging,
            "status" => ascending ? Select + "status ASC" + Paging : Select + "status DESC" + Paging,
            _ => throw new ArgumentException("sortBy must be one of: date, amount, status.", nameof(sortBy)),
        };
    }

    // "Contains" pattern where the user's text is matched literally: %, _ and [ (a wildcard in SQL Server) are escaped.
    private static string ContainsPattern(string text) =>
        "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";

    // Untrusted input written to the log: shortened, and without line breaks so it cannot forge log lines.
    private static string Printable(string? value)
    {
        if (value is null) return "null";
        var shortened = value.Length <= 80 ? value : value[..80] + "...";
        return "\"" + string.Concat(shortened.Select(c => char.IsControl(c) || c == (char)0x2028 || c == (char)0x2029 ? '?' : c)) + "\"";
    }
}
