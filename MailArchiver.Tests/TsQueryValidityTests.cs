using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MailArchiver.Services.Core;
using Npgsql;
using Xunit;

namespace MailArchiver.Tests;

// DB-backed robustness tests: every tsquery the parser emits MUST be a syntactically
// valid PostgreSQL tsquery, otherwise the optimized search throws and silently falls
// back to the (slower) EF path. Runs only when MAILARCHIVER_TEST_DB is set (CI / opt-in).
public class TsQueryValidityTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("MAILARCHIVER_TEST_DB");

    public static IEnumerable<object[]> Inputs => new[]
    {
        "rechnung", "rechnung mahnung", "auto OR fahrrad", "rechnung -mahnung",
        "wd red 8 tb", "auto rad OR bike", "-mahnung", "\"exact phrase\"",
        "subject:invoice", "a&b|c", "(foo)", "***", "- - -", "OR OR OR",
        "x27); --", "a:*b", "buero strasse", "cafe", "8TB WD80EFPX",
        "a;b:c,d.e@f", "!!!", "()()", "wd & red | !mahnung", "___%%%",
    }.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Inputs))]
    public async Task Parser_tsquery_is_accepted_by_postgres(string input)
    {
        if (Conn == null) return; // integration test: only runs when MAILARCHIVER_TEST_DB is set (CI)
        var parsed = EmailCoreService.ParseSearchTermForTsQuery(input);
        if (string.IsNullOrEmpty(parsed.tsQuery)) return;
        await using var c = new NpgsqlConnection(Conn);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT to_tsquery('simple', @q)", c);
        cmd.Parameters.AddWithValue("q", parsed.tsQuery);
        var ex = await Record.ExceptionAsync(() => cmd.ExecuteScalarAsync());
        Assert.True(ex == null, $"Parser emitted an invalid tsquery for input [{input}] -> [{parsed.tsQuery}]: {ex?.Message}");
    }
}
