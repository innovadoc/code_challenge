namespace Claims.Tests;

// T3: tests for MYT-3108. They describe the rules in FINDING.md.
public class MyTests
{
    private static ClaimsRepository Repo() => new(Db.CreateSeeded());
    private static long[] Ids(IEnumerable<Claim> claims) => claims.Select(c => c.Id).ToArray();

    // ---------- Sorting ----------

    [Theory]
    [InlineData("amount, (SELECT name FROM members)")]                                    // extra sort key reads another table
    [InlineData("(CASE WHEN (SELECT COUNT(*) FROM members) > 1 THEN amount ELSE id END)")] // blind injection, no quote or semicolon
    [InlineData("amount;DROP TABLE claims")]
    [InlineData("submitted_at")]                                                          // real column, but not a public option
    [InlineData("member_id")]
    [InlineData("")]
    [InlineData(null)]
    public void Search_SortByOutsideTheAllowedOptions_IsRejected(string? sortBy)
    {
        Assert.ThrowsAny<ArgumentException>(() => Repo().Search(1, "City Pharmacy", sortBy, "asc"));
    }

    [Theory]
    [InlineData("date", "asc", new long[] { 104, 105 })]
    [InlineData("date", "desc", new long[] { 105, 104 })]
    [InlineData("amount", "asc", new long[] { 104, 105 })]
    [InlineData("amount", "desc", new long[] { 105, 104 })]
    [InlineData("status", "asc", new long[] { 105, 104 })]
    [InlineData("status", "desc", new long[] { 104, 105 })]
    public void Search_AllowedSortOptions_OrderTheResults(string sortBy, string sortDir, long[] expected)
    {
        Assert.Equal(expected, Ids(Repo().Search(1, "City Pharmacy", sortBy, sortDir)));
    }

    // ---------- Search ----------

    [Fact]
    public void Search_FindsTextContainedInDescriptionOrProvider_InAnyCase()
    {
        Assert.Equal(new long[] { 101, 103 }, Ids(Repo().Search(1, "dental", "date", "asc")));
        Assert.Equal(new long[] { 104, 105 }, Ids(Repo().Search(1, "PHARM", "date", "asc")));
    }

    [Fact]
    public void Search_TextWithApostrophe_FindsTheProvider()
    {
        Assert.Equal(new long[] { 102 }, Ids(Repo().Search(1, "O'Brien")));
    }

    [Fact]
    public void Search_InjectionInText_IsTreatedAsPlainText()
    {
        Assert.Empty(Repo().Search(1, "' OR 1=1 --"));
        Assert.Empty(Repo().Search(1, "%' OR '1'='1"));
    }

    [Fact]
    public void Search_ReturnsOnlyTheClaimsOfTheGivenMember()
    {
        Assert.Equal(new long[] { 101, 102, 103, 104, 105 }, Ids(Repo().Search(1, "", "date", "asc")));
        Assert.Empty(Repo().Search(1, "crown"));
        Assert.Equal(new long[] { 201 }, Ids(Repo().Search(2, "dental")));
    }

    [Fact]
    public void Search_WildcardCharactersInText_AreMatchedLiterally()
    {
        Assert.Equal(new long[] { 104 }, Ids(Repo().Search(1, "100%")));
        Assert.Equal(new long[] { 104 }, Ids(Repo().Search(1, "%")));
        Assert.Empty(Repo().Search(1, "100_"));
        Assert.Empty(Repo().Search(1, "_"));
    }

    [Fact]
    public void Search_BracketInText_IsMatchedLiterally()
    {
        var db = Db.CreateSeeded();
        using (var insert = db.CreateCommand())
        {
            insert.CommandText =
                "INSERT INTO claims VALUES (106, 1, 'City Lab', 'Blood panel [urgent]', 40.00, 'paid', '2026-11-01')";
            insert.ExecuteNonQuery();
        }

        Assert.Equal(new long[] { 106 }, Ids(new ClaimsRepository(db).Search(1, "[urgent]")));
    }

    // ---------- Limits against abusive input ----------

    [Fact]
    public void Search_NegativePage_IsRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => Repo().Search(1, "City Pharmacy", "date", "asc", -1));
    }

    [Fact]
    public void Search_HugePageNumber_ReturnsNothingInsteadOfWrappingToTheFirstPage()
    {
        Assert.Empty(Repo().Search(1, "City Pharmacy", "date", "asc", int.MaxValue));
    }

    [Fact]
    public void Search_TextLongerThanTheLimit_IsRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() => Repo().Search(1, new string('a', 201)));
    }

    [Fact]
    public void Search_Returns20ResultsPerPage()
    {
        var db = Db.CreateSeeded();
        using (var insert = db.CreateCommand())
        {
            insert.CommandText = string.Join(";", Enumerable.Range(0, 25).Select(i =>
                $"INSERT INTO claims VALUES ({900 + i}, 9, 'Bulk', 'Bulk', {i}, 'paid', '2026-01-01')"));
            insert.ExecuteNonQuery();
        }
        var repo = new ClaimsRepository(db);

        Assert.Equal(Enumerable.Range(900, 20).Select(i => (long)i), Ids(repo.Search(9, "Bulk", "amount", "asc", 0)));
        Assert.Equal(Enumerable.Range(920, 5).Select(i => (long)i), Ids(repo.Search(9, "Bulk", "amount", "asc", 1)));
    }
}
