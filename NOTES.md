# MYT-3108 notes

## What the agent proposed

The agent made three changes to `ClaimsRepository.Search` and reported the injection as fixed.

- **Search text as a query parameter.** Correct, and kept. This is what closes the injection through `text`.
- **Sort input "sanitized" by removing quotes and semicolons.** Not a fix. `sortBy` was still concatenated into
  `ORDER BY`, and an attack needs neither character: `amount, (SELECT name FROM members)` and
  `(CASE WHEN (SELECT COUNT(*) FROM members) > 1 THEN amount ELSE id END)` both ran. The injection was still open.
  It also accepted any column name, such as `submitted_at` or `member_id`.
- **"Defense in depth": apostrophes removed and exact matching.** Neither adds security once the text is a
  parameter, and both break product rules: `O'Brien` found nothing and `dental` found nothing.

Its four tests passed because one of them asserted the broken behavior (`dental` returns no results) and none
tried an injection through `sortBy`.

## What I changed and why

- **Sorting uses an allow-list.** Only `date`, `amount` and `status` are accepted; anything else, including
  `null` and real column names, throws `ArgumentException`. The SQL is one of six constant strings chosen by a
  `switch`, so no caller input is ever part of the SQL text and nothing is built per call.
- **Search is "contains" again, in any case, with apostrophes kept.** The text is still a parameter. The
  characters `%`, `_` and `[` are escaped (`ESCAPE '\'`), so a user cannot inject wildcards: `100%` finds only
  the claim that contains "100%".
- **Limits on abusive input.** A negative `page` is rejected, a very large `page` no longer overflows back to
  the first page, and text longer than 200 characters is rejected. The 200 limit is my assumption, not a rule
  in `FINDING.md`.
- **Exceptions are logged and rethrown.** `Search` writes every exception to `logs/claims-errors.log` through
  an `IErrorLog` interface (`FileErrorLog` is the default). The search text is not logged because it can hold
  health information, and line breaks in `sortBy` are replaced so log lines cannot be forged. This goes beyond
  the single file the README names: it added `IErrorLog.cs`, `FileErrorLog.cs` and `ErrorLoggingTests.cs`.
- **One agent test corrected.** `Search_ExactMatchOnly_BlocksWildcardEnumeration` contradicted `FINDING.md`.
  It is now `Search_PartialText_FindsContainingClaims` and asserts that `dental` finds claims 101 and 103.

Not changed: an invalid `sortDir` still falls back to `desc` (it is not injectable, only two constants can
result), and results with equal sort values have no tie-breaker across pages.

## Evidence

- **Tests written first and seen failing.** Against the agent's code, 12 of my first 19 tests failed, each for
  the expected reason (no exception on injected `sortBy`, empty results for `dental` and `O'Brien`). The 8
  logging and limit tests then failed before their code existed.
- **Current result:** `dotnet test tests/Claims.Tests` passes 33 of 33.
- **Scorer:** `dotnet run --project check` reports sorting 4/4, search 4/4, the extra `100%` check "yes",
  my tests 23/23 on my code with 15 of 23 failing on the agent's version, and the agent's tests 4 passed.
  Before I corrected the agent's test, that last line read 3 passed, 1 failed.
- **Coverage of the `Claims` project:** 85.1% of lines and 92.1% of branches; `ClaimsRepository` itself is at
  100% of lines and 90% of branches.
- **Still pending to close the finding:** the merge through the pipeline and the new security scan. Neither
  was run here.
