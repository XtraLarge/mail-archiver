using System;
using System.Collections.Generic;
using System.Linq;
using MailArchiver.Models.ViewModels;
using MailArchiver.Services.Core;
using Xunit;

namespace MailArchiver.Tests;

// Regression tests for the folder-tree builder (multi-account, case-variant INBOX, dovecot '.' hierarchy).
public class FolderTreeTests
{
    private static List<FolderTreeNode> Build(params (string, int)[] folders)
        => EmailCoreService.BuildFolderTree(folders.Select(f => (f.Item1, f.Item2)).ToList());

    private static IEnumerable<FolderTreeNode> Flatten(IEnumerable<FolderTreeNode> nodes)
    {
        foreach (var n in nodes)
        {
            yield return n;
            foreach (var c in Flatten(n.Children)) yield return c;
        }
    }

    [Fact]
    public void Case_variant_inbox_is_not_duplicated()
    {
        // "INBOX" (account A) + "Inbox" (account B) + "INBOX.2024" (account C, dovecot)
        var roots = Build(("INBOX", 10), ("Inbox", 5), ("INBOX.2024", 3));
        var inboxes = roots.Where(r => string.Equals(r.FullPath, "INBOX", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(inboxes);                  // exactly one INBOX at root (previously two)
        Assert.Equal(15, inboxes[0].TotalCount); // case-variant counts merged, none lost
        var year = Assert.Single(inboxes[0].Children);
        Assert.Equal("2024", year.Name);
    }

    [Fact]
    public void Dovecot_dot_hierarchy_nests()
    {
        var roots = Build(("INBOX", 1), ("INBOX.2024", 2), ("INBOX.2024.Sent", 1));
        var inbox = Assert.Single(roots);
        var year = Assert.Single(inbox.Children);
        Assert.Equal("2024", year.Name);
        Assert.Equal("Sent", Assert.Single(year.Children).Name);
    }

    [Fact]
    public void No_phantom_parent_when_parent_folder_absent()
    {
        // "Foo/Bar" without a "Foo" folder must stay a full-name root, not invent a "Foo" parent.
        var n = Assert.Single(Build(("Foo/Bar", 1)));
        Assert.Equal("Foo/Bar", n.FullPath);
        Assert.Equal(0, n.Level);
    }

    [Fact]
    public void Every_node_is_emitted_exactly_once()
    {
        var roots = Build(("INBOX", 1), ("Inbox", 1), ("Sent", 1), ("INBOX.A", 1), ("Inbox/B", 1));
        var all = Flatten(roots).ToList();
        Assert.Equal(all.Count, all.Distinct().Count()); // no node object appears twice
    }
}
