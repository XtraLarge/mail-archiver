using System.Collections.Generic;
using System.Linq;
using MailArchiver.Services.Core;
using Xunit;

namespace MailArchiver.Tests;

// Unit tests for EmailCoreService.ParseSearchTermForTsQuery (pure, static).
public class SearchTermParserTests
{
    private static (string tsQuery, List<string> phrases,
        Dictionary<string, List<string>> fieldSearches,
        Dictionary<string, List<string>> fieldPhrases,
        List<(string term, bool negated)> substrings) Parse(string? s)
        => EmailCoreService.ParseSearchTermForTsQuery(s!);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_yields_null_query_and_empty_parts(string? input)
    {
        var r = Parse(input);
        Assert.Null(r.tsQuery);
        Assert.Empty(r.phrases);
        Assert.Empty(r.fieldSearches);
        Assert.Empty(r.fieldPhrases);
        Assert.Empty(r.substrings);
    }

    [Fact]
    public void Single_long_word_gets_prefix() => Assert.Equal("rechnung:*", Parse("rechnung").tsQuery);

    [Theory]
    [InlineData("wd", "wd")]
    [InlineData("tb", "tb")]
    [InlineData("8", "8")]
    public void Short_word_is_exact_no_prefix(string input, string expected)
        => Assert.Equal(expected, Parse(input).tsQuery);

    [Fact]
    public void Multi_word_is_AND() => Assert.Equal("(rechnung:* & mahnung:*)", Parse("rechnung mahnung").tsQuery);

    [Fact]
    public void Mixed_length_words_and()
        => Assert.Equal("(wd & red:* & 8 & tb)", Parse("wd red 8 tb").tsQuery);

    [Theory]
    [InlineData("auto OR fahrrad")]
    [InlineData("auto or fahrrad")]
    [InlineData("auto ODER fahrrad")]
    [InlineData("auto | fahrrad")]
    public void Or_keyword_creates_alternatives(string input)
        => Assert.Equal("auto:* | fahrrad:*", Parse(input).tsQuery);

    [Fact]
    public void And_then_or_groups_correctly()
        => Assert.Equal("(auto:* & rad:*) | bike:*", Parse("auto rad OR bike").tsQuery);

    [Theory]
    [InlineData("-mahnung")]
    [InlineData("!mahnung")]
    public void Leading_minus_or_bang_excludes(string input)
        => Assert.Equal("!mahnung:*", Parse(input).tsQuery);

    [Fact]
    public void And_with_exclude() => Assert.Equal("(rechnung:* & !mahnung:*)", Parse("rechnung -mahnung").tsQuery);

    [Fact]
    public void Substring_mode_populates_substrings_not_tsquery()
    {
        var r = Parse("*teil*");
        Assert.Null(r.tsQuery);
        Assert.Equal(new List<(string, bool)> { ("teil", false) }, r.substrings);
    }

    [Fact]
    public void Negated_substring()
        => Assert.Equal(new List<(string, bool)> { ("teil", true) }, Parse("-*teil*").substrings);

    [Fact]
    public void Quoted_phrase_captured()
    {
        var r = Parse("\"exact phrase\"");
        Assert.Null(r.tsQuery);
        Assert.Equal(new List<string> { "exact phrase" }, r.phrases);
    }

    [Fact]
    public void Field_term_captured()
    {
        var r = Parse("subject:invoice");
        Assert.True(r.fieldSearches.ContainsKey("subject"));
        Assert.Equal(new List<string> { "invoice" }, r.fieldSearches["subject"]);
        Assert.Null(r.tsQuery);
    }

    [Fact]
    public void Field_phrase_captured()
        => Assert.Equal(new List<string> { "my inv" }, Parse("subject:\"my inv\"").fieldPhrases["subject"]);

    [Fact]
    public void Unknown_field_is_ignored()
        => Assert.Null(Parse("bogus:value").tsQuery);

    [Fact]
    public void Colon_token_is_treated_as_field_prefix_and_dropped_if_invalid()
        => Assert.Null(Parse("a:*b").tsQuery);

    [Fact]
    public void Complex_combination()
    {
        var r = Parse("\"car insurance\" rechnung -spam subject:invoice *teil*");
        Assert.Contains("car insurance", r.phrases);
        Assert.Contains("invoice", r.fieldSearches["subject"]);
        Assert.Contains(("teil", false), r.substrings);
        Assert.Equal("(rechnung:* & !spam:*)", r.tsQuery);
    }

    [Theory]
    [InlineData("a&b|c", "abc:*")]
    [InlineData("(foo)", "foo:*")]
    public void Tsquery_operators_in_words_are_sanitized(string input, string expected)
        => Assert.Equal(expected, Parse(input).tsQuery);

    [Theory]
    [InlineData("x27); --")]
    [InlineData("\\\\\\\\")]
    [InlineData("****")]
    [InlineData("- - -")]
    [InlineData("OR OR OR")]
    [InlineData("x27 OR x271x27=x271")]
    public void Adversarial_input_never_throws(string input)
    {
        var ex = Record.Exception(() => Parse(input));
        Assert.Null(ex);
    }
}
