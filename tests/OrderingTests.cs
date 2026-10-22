using System;
using System.Collections.Generic;
using System.IO;
using LargeFolder;

public static class OrderingTests
{
    private static int checks;
    private static string sandbox;

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Expected one unique ordering sandbox.");
            sandbox = Path.GetFullPath(args[0]);
            if (!String.Equals(sandbox.TrimEnd('\\'), Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(sandbox).StartsWith("ordering-sandbox-", StringComparison.Ordinal))
                throw new ArgumentException("All test files must remain in a unique ordering sandbox under tests.");

            TestDirections();
            TestNoChanges();
            TestIdentityAndIsolation();
            TestPersistence();
            Console.WriteLine("All " + checks + " ordering checks passed. Test files: " + sandbox);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAILED: " + description);
        checks++;
        Console.WriteLine("PASS " + description);
    }

    private static FolderGroup Group(params string[] ids)
    {
        FolderGroup group = DataStore.NewGroup("Order test", 0);
        foreach (string id in ids)
            group.Items.Add(new ItemEntry { Id = id, Path = Path.Combine(sandbox, id + ".txt"),
                OriginalPath = Path.Combine(sandbox, "original-" + id + ".txt"), AddedAt = new DateTime(2026, 1, 2, 3, 4, 5) });
        return group;
    }

    private static string Order(FolderGroup group)
    {
        List<string> ids = new List<string>();
        foreach (ItemEntry entry in group.Items) ids.Add(entry == null ? "<null>" : entry.Id);
        return String.Join(",", ids.ToArray());
    }

    private static void TestDirections()
    {
        FolderGroup g = Group("a", "b", "c", "d", "e");
        Check(ItemOrdering.MoveRelative(g, "d", "b", false) && Order(g) == "a,d,b,c,e", "backward move before a target");
        g = Group("a", "b", "c", "d", "e");
        Check(ItemOrdering.MoveRelative(g, "d", "b", true) && Order(g) == "a,b,d,c,e", "backward move after a target");
        g = Group("a", "b", "c", "d", "e");
        Check(ItemOrdering.MoveRelative(g, "b", "d", false) && Order(g) == "a,c,b,d,e", "forward move before a target");
        g = Group("a", "b", "c", "d", "e");
        Check(ItemOrdering.MoveRelative(g, "b", "d", true) && Order(g) == "a,c,d,b,e", "forward move after a target");
        g = Group("a", "b", "c", "d");
        Check(ItemOrdering.MoveRelative(g, "d", "a", false) && Order(g) == "d,a,b,c", "last entry moves to first position");
        Check(ItemOrdering.MoveRelative(g, "d", "c", true) && Order(g) == "a,b,c,d", "first entry moves after last entry");
        Check(ItemOrdering.MoveRelative(g, "b", "a", false) && Order(g) == "b,a,c,d", "adjacent entries can swap backward");
        Check(ItemOrdering.MoveRelative(g, "b", "a", true) && Order(g) == "a,b,c,d", "adjacent entries can swap forward");
        Check(ItemOrdering.MoveToEnd(g, "b") && Order(g) == "a,c,d,b", "drop on empty area moves entry to end");
        Check(ItemOrdering.MoveRelative(g, "B", "A", false) && Order(g) == "b,a,c,d", "opaque IDs are matched consistently without case ambiguity");
    }

    private static void TestNoChanges()
    {
        FolderGroup g = Group("a", "b", "c");
        Check(!ItemOrdering.MoveRelative(g, "a", "b", false) && Order(g) == "a,b,c", "entry already directly before target is unchanged");
        Check(!ItemOrdering.MoveRelative(g, "b", "a", true) && Order(g) == "a,b,c", "entry already directly after target is unchanged");
        Check(!ItemOrdering.MoveRelative(g, "b", "B", true) && Order(g) == "a,b,c", "self drop is unchanged");
        Check(!ItemOrdering.MoveToEnd(g, "c") && Order(g) == "a,b,c", "last entry dropped at end is unchanged");
        Check(!ItemOrdering.MoveRelative(g, "missing", "b", false) && Order(g) == "a,b,c", "missing source is unchanged");
        Check(!ItemOrdering.MoveRelative(g, "a", "missing", false) && Order(g) == "a,b,c", "missing target is unchanged");
        Check(!ItemOrdering.MoveToEnd(g, "missing") && Order(g) == "a,b,c", "missing end source is unchanged");
        Check(!ItemOrdering.MoveRelative(g, null, "b", true) && !ItemOrdering.MoveRelative(g, "a", " ", true) &&
            !ItemOrdering.MoveToEnd(g, "") && Order(g) == "a,b,c", "blank IDs do not alter order");
        Check(!ItemOrdering.MoveRelative(null, "a", "b", true) && !ItemOrdering.MoveToEnd(null, "a"), "null group is rejected");
        g = Group();
        Check(!ItemOrdering.MoveRelative(g, "a", "b", true) && !ItemOrdering.MoveToEnd(g, "a") && g.Items.Count == 0, "empty group is unchanged");
        g.Items = null;
        Check(!ItemOrdering.MoveRelative(g, "a", "b", true) && !ItemOrdering.MoveToEnd(g, "a") && g.Items == null, "null item list is unchanged");
        g = Group("a");
        Check(!ItemOrdering.MoveRelative(g, "a", "a", true) && !ItemOrdering.MoveToEnd(g, "a") && Order(g) == "a", "single entry is unchanged");
        g = Group("a", "b", "c", "b");
        Check(!ItemOrdering.MoveRelative(g, "c", "b", false) && !ItemOrdering.MoveToEnd(g, "b") && Order(g) == "a,b,c,b", "duplicate source or target IDs reject changes");
        g = Group("a", "b", "c", "C");
        Check(!ItemOrdering.MoveRelative(g, "b", "a", false) && !ItemOrdering.MoveToEnd(g, "a") && Order(g) == "a,b,c,C", "unrelated case-insensitive duplicates reject the whole ambiguous group");
        g = Group("a", "b", "");
        Check(!ItemOrdering.MoveRelative(g, "b", "a", false) && !ItemOrdering.MoveToEnd(g, "a") && Order(g) == "a,b,", "invalid entry ID is rejected without mutation");
        g = Group("a", "b"); g.Items.Add(null);
        Check(!ItemOrdering.MoveRelative(g, "b", "a", false) && !ItemOrdering.MoveToEnd(g, "a") && Order(g) == "a,b,<null>", "null entry is rejected without mutation");
    }

    private static void TestIdentityAndIsolation()
    {
        FolderGroup g = Group("a", "b", "c");
        FolderGroup other = Group("x", "y");
        List<ItemEntry> list = g.Items;
        ItemEntry a = list[0], b = list[1], c = list[2];
        string path = b.Path, originalPath = b.OriginalPath;
        DateTime addedAt = b.AddedAt;
        ItemOrdering.MoveRelative(g, "b", "a", false);
        Check(Object.ReferenceEquals(g.Items, list), "reordering preserves the list object");
        Check(Object.ReferenceEquals(g.Items[0], b) && Object.ReferenceEquals(g.Items[1], a) && Object.ReferenceEquals(g.Items[2], c), "reordering preserves every entry object");
        Check(b.Path == path && b.OriginalPath == originalPath && b.AddedAt == addedAt && b.Id == "b", "paths, restoration metadata, time and ID are untouched");
        ItemOrdering.MoveToEnd(g, "b");
        Check(Object.ReferenceEquals(g.Items[2], b) && Order(g) == "a,c,b", "moving to end also preserves identity");
        Check(!ItemOrdering.MoveRelative(g, "a", "y", true) && Order(g) == "a,c,b" && Order(other) == "x,y", "drop target from another group cannot move anything");
        Check(Order(other) == "x,y", "other group order stays unchanged");
    }

    private static void TestPersistence()
    {
        DataStore store = new DataStore(Path.Combine(sandbox, "persistence"));
        AppData data = new AppData();
        FolderGroup g = Group("a", "b", "c", "d");
        data.Groups.Add(g);
        data.Groups.Add(Group("x", "y"));
        ItemOrdering.MoveRelative(g, "d", "a", false);
        ItemOrdering.MoveToEnd(g, "b");
        string beforeSave = Order(g);
        store.Save(data);
        AppData loaded = new DataStore(store.RootDirectory).Load();
        Check(beforeSave == "d,a,c,b" && Order(loaded.Groups[0]) == beforeSave, "Save and Load preserve a manually chosen order");
        Check(Order(loaded.Groups[1]) == "x,y", "Save and Load preserve other groups independently");
        Check(loaded.Groups[0].Items[3].Path == Path.Combine(sandbox, "b.txt") &&
            loaded.Groups[0].Items[3].OriginalPath == Path.Combine(sandbox, "original-b.txt"), "saved paths and restoration metadata remain associated with moved entry");
        Check(!Directory.Exists(Path.Combine(sandbox, "b.txt")) && !File.Exists(Path.Combine(sandbox, "b.txt")), "ordering and persistence never create or move target files");
    }
}
