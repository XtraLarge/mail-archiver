using System.Collections.Generic;
using System.Linq;
using MailArchiver.Services.Core;
using Xunit;

namespace MailArchiver.Tests;

// Unit tests for the unified search-clause parser (words / phrases / fields / substrings,
// with OR-groups and negation). Word tsquery composition is checked via BuildWordTsQuery.
public class SearchTermParserTests
{
    private static List<List<EmailCoreService.SearchClause>> Parse(string? s)
        => EmailCoreService.ParseSearchClauses(s!);

    // combined tsquery for pure-word queries
    private static string Ts(string? s)
        => EmailCoreService.BuildWordTsQuery(EmailCoreService.ParseSearchClauses(s!));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_yields_no_groups(string? input)
        => Assert.Empty(Parse(input));

    // ---- word tsquery (AND / OR / NOT / conditional prefix) ----
    [Fact] public void Single_long_word_gets_prefix() => Assert.Equal("rechnung:*", Ts("rechnung"));

    [Theory]
    [InlineData("wd")]
    [InlineData("tb")]
    public void Short_word_is_exact_no_prefix(string input) => Assert.Equal(input, Ts(input));

    [Fact] public void Multi_word_is_AND() => Assert.Equal("rechnung:* & mahnung:*", Ts("rechnung mahnung"));
    [Fact] public void Mixed_length_words() => Assert.Equal("wd & red:* & 8 & tb", Ts("wd red 8 tb"));

    [Theory]
    [InlineData("auto OR fahrrad")]
    [InlineData("auto or fahrrad")]
    [InlineData("auto ODER fahrrad")]
    [InlineData("auto | fahrrad")]
    public void Or_creates_group(string input) => Assert.Equal("(auto:* | fahrrad:*)", Ts(input));

    [Fact] public void Or_binds_neighbours() => Assert.Equal("invoice:* & (car:* | bike:*)", Ts("invoice car OR bike"));
    [Fact] public void And_then_or() => Assert.Equal("auto:* & (rad:* | bike:*)", Ts("auto rad OR bike"));
    [Fact] public void Chained_or() => Assert.Equal("(aaa:* | bbb:* | ccc:*)", Ts("aaa OR bbb OR ccc"));

    [Theory]
    [InlineData("-mahnung")]
    [InlineData("!mahnung")]
    public void Exclude_negates(string input) => Assert.Equal("!mahnung:*", Ts(input));

    [Fact] public void And_with_exclude() => Assert.Equal("rechnung:* & !mahnung:*", Ts("rechnung -mahnung"));

    // ---- typed clauses ----
    [Fact]
    public void Phrase_clause()
    {
        var c = Assert.Single(Assert.Single(Parse("\"exact phrase\"")));
        Assert.Equal(EmailCoreService.ClauseKind.Phrase, c.Kind);
        Assert.Equal("exact phrase", c.Text);
    }

    [Fact]
    public void Field_clause()
    {
        var c = Assert.Single(Assert.Single(Parse("subject:invoice")));
        Assert.Equal(EmailCoreService.ClauseKind.Field, c.Kind);
        Assert.Equal("Subject", c.Column);
        Assert.Equal("invoice", c.Text);
    }

    [Fact]
    public void Substring_clause()
    {
        var c = Assert.Single(Assert.Single(Parse("*teil*")));
        Assert.Equal(EmailCoreService.ClauseKind.Substring, c.Kind);
        Assert.Equal("teil", c.Text);
        Assert.False(c.Negated);
    }

    [Fact]
    public void Negated_substring() => Assert.True(Assert.Single(Assert.Single(Parse("-*teil*"))).Negated);

    [Fact]
    public void Unknown_field_is_ignored() => Assert.Empty(Parse("bogus:value"));

    // ---- OR across non-word types (Codex regression tests) ----
    [Fact]
    public void Or_across_fields_is_one_group()
    {
        var groups = Parse("from:alice OR from:bob");
        var g = Assert.Single(groups);                       // one OR-group, not two AND-groups
        Assert.Equal(2, g.Count);
        Assert.All(g, c => Assert.Equal(EmailCoreService.ClauseKind.Field, c.Kind));
        Assert.Equal(new[] { "alice", "bob" }, g.Select(c => c.Text));
    }

    [Fact]
    public void Or_across_substrings_is_one_group()
    {
        var g = Assert.Single(Parse("*invoice* OR *receipt*"));
        Assert.Equal(2, g.Count);
        Assert.All(g, c => Assert.Equal(EmailCoreService.ClauseKind.Substring, c.Kind));
    }

    [Fact]
    public void Mixed_word_or_field_is_one_group()
    {
        var g = Assert.Single(Parse("invoice OR from:acme"));
        Assert.Equal(2, g.Count);
        Assert.Contains(g, c => c.Kind == EmailCoreService.ClauseKind.Word && c.Text == "invoice");
        Assert.Contains(g, c => c.Kind == EmailCoreService.ClauseKind.Field && c.Text == "acme");
    }

    [Fact]
    public void Complex_combination_groups()
    {
        // phrase, word, !word, field, substring -> 5 AND-groups (no OR)
        var groups = Parse("\"car insurance\" rechnung -spam subject:invoice *teil*");
        Assert.Equal(5, groups.Count);
        Assert.All(groups, g => Assert.Single(g));
        Assert.Contains(groups.SelectMany(g => g), c => c.Kind == EmailCoreService.ClauseKind.Phrase);
        Assert.Contains(groups.SelectMany(g => g), c => c.Kind == EmailCoreService.ClauseKind.Field && c.Text == "invoice");
        Assert.Contains(groups.SelectMany(g => g), c => c.Kind == EmailCoreService.ClauseKind.Substring && c.Text == "teil");
        Assert.Contains(groups.SelectMany(g => g), c => c.Kind == EmailCoreService.ClauseKind.Word && c.Negated && c.Text == "spam");
    }

    [Theory]
    [InlineData("a&b|c")]
    [InlineData("(foo)")]
    [InlineData("x27); --")]
    [InlineData("- - -")]
    [InlineData("OR OR OR")]
    public void Adversarial_never_throws(string input)
        => Assert.Null(Record.Exception(() => Parse(input)));
}
