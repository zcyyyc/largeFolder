using System;
using System.Collections.Generic;
using System.IO;
using LargeFolder;

public static class GeometryTests
{
    private static int checks;
    private static string sandbox;

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Expected one unique geometry sandbox.");
            sandbox = Path.GetFullPath(args[0]);
            if (!String.Equals(sandbox.TrimEnd('\\'), Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(sandbox).StartsWith("geometry-sandbox-", StringComparison.Ordinal))
                throw new ArgumentException("All test files must remain in a unique geometry sandbox under tests.");
            if (!Directory.Exists(sandbox)) throw new DirectoryNotFoundException(sandbox);
            TestEdgesAndCorners();
            TestLimitsAndCumulativeMovement();
            TestInvalidInputs();
            TestPreviewLayout();
            TestLegacyConfiguration();
            TestPersistenceAndNormalization();
            Console.WriteLine("All " + checks + " geometry/storage checks passed. Test files: " + sandbox);
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

    private static bool Equal(double a, double b)
    {
        return Math.Abs(a - b) < 0.000001;
    }

    private static bool Finite(double value)
    {
        return !Double.IsNaN(value) && !Double.IsInfinity(value);
    }

    private static CardBounds Resize(double horizontal, double vertical, bool left, bool top, bool right, bool bottom)
    {
        return CardGeometry.Resize(-300, -100, 400, 300, horizontal, vertical,
            left, top, right, bottom, 142, 112, 1200, 1000);
    }

    private static void Bounds(CardBounds bounds, double left, double top, double width, double height, string description)
    {
        Check(Equal(bounds.Left, left) && Equal(bounds.Top, top) && Equal(bounds.Width, width) && Equal(bounds.Height, height), description);
    }

    private static void TestEdgesAndCorners()
    {
        Bounds(Resize(25, 30, true, false, false, false), -275, -100, 375, 300, "left edge keeps original right edge and vertical bounds");
        Bounds(Resize(25, 30, false, true, false, false), -300, -70, 400, 270, "top edge keeps original bottom edge and horizontal bounds");
        Bounds(Resize(25, 30, false, false, true, false), -300, -100, 425, 300, "right edge keeps original left edge and vertical bounds");
        Bounds(Resize(25, 30, false, false, false, true), -300, -100, 400, 330, "bottom edge keeps original top edge and horizontal bounds");
        Bounds(Resize(25, 30, true, true, false, false), -275, -70, 375, 270, "top-left corner anchors bottom-right");
        Bounds(Resize(25, 30, false, true, true, false), -300, -70, 425, 270, "top-right corner anchors bottom-left");
        Bounds(Resize(25, 30, true, false, false, true), -275, -100, 375, 330, "bottom-left corner anchors top-right");
        Bounds(Resize(25, 30, false, false, true, true), -300, -100, 425, 330, "bottom-right corner anchors top-left");
        Bounds(Resize(25, 30, false, false, false, false), -300, -100, 400, 300, "no selected edges leaves bounds unchanged");
        Check(CardGeometry.MinCompactWidth == 142 && CardGeometry.MinCompactHeight == 112 &&
            CardGeometry.MinExpandedWidth == 350 && CardGeometry.MinExpandedHeight == 170 &&
            CardGeometry.MaxWidth == 1200 && CardGeometry.MaxHeight == 1000, "shared UI size limits are correct");
    }

    private static void TestLimitsAndCumulativeMovement()
    {
        Bounds(Resize(9999, 9999, true, true, false, false), -42, 88, 142, 112, "left/top minimum clamp preserves original opposite edges");
        Bounds(Resize(-9999, -9999, true, true, false, false), -1100, -800, 1200, 1000, "left/top maximum clamp preserves original opposite edges");
        Bounds(Resize(-9999, -9999, false, false, true, true), -300, -100, 142, 112, "right/bottom minimum clamp keeps origin");
        Bounds(Resize(9999, 9999, false, false, true, true), -300, -100, 1200, 1000, "right/bottom maximum clamp keeps origin");
        Bounds(Resize(258, 188, true, true, false, false), -42, 88, 142, 112, "exact minimum boundary has no rounding drift");
        Bounds(Resize(800, 700, false, false, true, true), -300, -100, 1200, 1000, "exact maximum boundary has no rounding drift");
        CardBounds first = Resize(20, 15, true, true, false, false);
        CardBounds second = Resize(40, 30, true, true, false, false);
        Check(Equal(first.Width, 380) && Equal(second.Width, 360) && Equal(second.Left, -260) && Equal(second.Height, 270),
            "cumulative deltas are always measured from drag-start bounds");
        CardBounds screen = CardGeometry.Resize(-900, 20, 400, 300, 9999, 9999, false, false, true, true, 142, 112, 600, 430);
        Bounds(screen, -900, 20, 600, 430, "caller can reduce maximum dimensions for the active screen");
        CardBounds fractional = CardGeometry.Resize(-100.5, -50.25, 400.75, 300.5, 1.25, -2.5, true, true, false, false, 142, 112, 1200, 1000);
        Bounds(fractional, -99.25, -52.75, 399.5, 303, "fractional DIP coordinates and movement remain precise");
        Bounds(Resize(25, 30, true, true, true, true), -275, -70, 375, 270, "ambiguous opposite edge flags deterministically prefer left/top");
    }

    private static void TestInvalidInputs()
    {
        Bounds(Resize(Double.NaN, Double.PositiveInfinity, true, true, false, false), -300, -100, 400, 300,
            "non-finite pointer deltas are ignored");
        CardBounds invalid = CardGeometry.Resize(Double.NaN, Double.NegativeInfinity, -4, Double.NaN,
            0, 0, true, true, false, false, 142, 112, 1200, 1000);
        Bounds(invalid, 0, 0, 142, 112, "invalid origin and dimensions produce usable finite geometry");
        invalid = CardGeometry.Resize(0, 0, 200, 200, 0, 0, false, false, true, true,
            Double.NaN, -2, Double.NaN, Double.NegativeInfinity);
        Bounds(invalid, 0, 0, 1, 1, "invalid limits normalize to a finite positive interval");
        invalid = CardGeometry.Resize(0, 0, 200, 200, 0, 0, false, false, true, true, 142, 112, 40, 20);
        Bounds(invalid, 0, 0, 142, 112, "maximum below minimum safely normalizes to minimum");
        invalid = CardGeometry.Resize(Double.MaxValue, -Double.MaxValue, Double.MaxValue, Double.MaxValue,
            Double.MaxValue, -Double.MaxValue, true, true, false, false, 142, 112, 1200, 1000);
        Check(Finite(invalid.Left) && Finite(invalid.Top) && Finite(invalid.Width) && Finite(invalid.Height) &&
            invalid.Width >= 142 && invalid.Width <= 1200 && invalid.Height >= 112 && invalid.Height <= 1000,
            "extreme finite inputs cannot return infinite coordinates or dimensions");
    }

    private static void Layout(double width, double height, int count, int columns, int rows, int capacity, bool hasMore, string description)
    {
        CardPreviewLayout layout = CardGeometry.Preview(width, height, count);
        Check(layout.Columns == columns && layout.Rows == rows && layout.Capacity == capacity && layout.HasMore == hasMore, description);
    }

    private static void TestPreviewLayout()
    {
        Layout(142, 168, 4, 2, 2, 4, false, "142x168 compact card displays all four items without footer");
        Layout(142, 192, 6, 2, 2, 4, true, "142x192 compact card reserves footer and shows first four of six items");
        Layout(254, 112, 4, 4, 1, 4, false, "254x112 compact card displays four items in one row without footer");
        Layout(142, 136, 4, 2, 1, 2, true, "142x136 compact card shows one row and footer for remaining items");
        Layout(142, 168, 5, 2, 1, 2, true, "footer reservation recomputes capacity when it removes the second row");
        Layout(253.99, 112, 4, 3, 1, 3, true, "column threshold uses floor and does not overflow the available width");
        Layout(142, 168, -10, 2, 2, 4, false, "negative item counts normalize to empty without footer");
        Layout(Double.NaN, Double.PositiveInfinity, 3, 2, 1, 2, true, "non-finite preview dimensions fall back to finite compact minimums");
        Layout(-4, 0, 0, 2, 1, 2, false, "nonpositive preview dimensions remain a usable minimum grid");
        Layout(1, 1, 3, 2, 1, 2, true, "small positive preview dimensions enforce at least two columns and one row");
        CardPreviewLayout huge = CardGeometry.Preview(Double.MaxValue, Double.MaxValue, Int32.MaxValue);
        Check(huge.Columns == Int32.MaxValue && huge.Rows == Int32.MaxValue && huge.Capacity == Int32.MaxValue && !huge.HasMore,
            "extreme preview sizes saturate capacity without integer overflow");
    }

    private static void TestLegacyConfiguration()
    {
        string root = Path.Combine(sandbox, "legacy");
        Directory.CreateDirectory(Path.Combine(root, "data"));
        string config = Path.Combine(root, "data", "config.json");
        File.WriteAllText(config, "{\"Groups\":[{\"Id\":\"legacy-group\",\"Name\":\"Legacy\",\"Color\":\"#EAF3EB\",\"Left\":-250,\"Top\":-75,\"Items\":[]}],\"AlwaysOnTop\":true,\"StartWithWindows\":false,\"CloseOnBlur\":true}");
        DataStore store = new DataStore(root);
        AppData legacy = store.Load();
        FolderGroup group = legacy.Groups[0];
        Check(group.CompactWidth == 0 && group.CompactHeight == 0 && group.ExpandedWidth == 0 && group.ExpandedHeight == 0,
            "old configuration without size members retains zero automatic defaults");
        Check(group.Id == "legacy-group" && group.Name == "Legacy" && group.Left == -250 && group.Top == -75 && legacy.AlwaysOnTop,
            "old identifiers, appearance, negative coordinates and preferences survive load");
        store.Save(legacy);
        string json = File.ReadAllText(config);
        Check(!json.Contains("CompactWidth") && !json.Contains("CompactHeight") && !json.Contains("ExpandedWidth") && !json.Contains("ExpandedHeight"),
            "automatic size fields stay omitted when saving a legacy configuration");
    }

    private static void TestPersistenceAndNormalization()
    {
        string root = Path.Combine(sandbox, "persistence");
        DataStore store = new DataStore(root);
        FolderGroup group = DataStore.NewGroup("Resize test", 0);
        group.Left = -1800.5;
        group.Top = -200.25;
        group.CompactWidth = 222.5;
        group.CompactHeight = 255.75;
        group.ExpandedWidth = 655.25;
        group.ExpandedHeight = 500.5;
        group.Items.Add(new ItemEntry { Id = "item-a", Path = Path.Combine(sandbox, "reference-a"),
            OriginalPath = Path.Combine(sandbox, "original-a"), AddedAt = new DateTime(2026, 1, 1) });
        AppData data = new AppData { Groups = new List<FolderGroup> { group } };
        store.Save(data);
        AppData loaded = store.Load();
        FolderGroup saved = loaded.Groups[0];
        Check(saved.Left == -1800.5 && saved.Top == -200.25, "negative multimonitor coordinates round-trip unchanged");
        Check(saved.CompactWidth == 222.5 && saved.CompactHeight == 255.75 && saved.ExpandedWidth == 655.25 && saved.ExpandedHeight == 500.5,
            "all four custom size fields persist independently with fractional DIP precision");
        Check(saved.Items.Count == 1 && saved.Items[0].Id == "item-a" && saved.Items[0].OriginalPath == group.Items[0].OriginalPath,
            "saving geometry preserves item identity and restore metadata");
        saved.CompactWidth = 333;
        saved.CompactHeight = 210;
        store.Save(loaded);
        saved = store.Load().Groups[0];
        Check(saved.CompactWidth == 333 && saved.CompactHeight == 210 && saved.ExpandedWidth == 655.25 && saved.ExpandedHeight == 500.5,
            "changing compact dimensions does not overwrite expanded dimensions");
        loaded = store.Load();
        loaded.Groups[0].ExpandedWidth = 800;
        loaded.Groups[0].ExpandedHeight = 600;
        store.Save(loaded);
        saved = store.Load().Groups[0];
        Check(saved.CompactWidth == 333 && saved.CompactHeight == 210 && saved.ExpandedWidth == 800 && saved.ExpandedHeight == 600,
            "changing expanded dimensions does not overwrite compact dimensions");
        group.CompactWidth = Double.NaN;
        group.CompactHeight = Double.PositiveInfinity;
        group.ExpandedWidth = -20;
        group.ExpandedHeight = Double.NegativeInfinity;
        store.Save(data);
        saved = store.Load().Groups[0];
        Check(saved.CompactWidth == 0 && saved.CompactHeight == 0 && saved.ExpandedWidth == 0 && saved.ExpandedHeight == 0,
            "invalid persisted dimensions normalize to zero before serialization");
        group.CompactWidth = 1;
        group.CompactHeight = 9000;
        group.ExpandedWidth = 0;
        group.ExpandedHeight = 0.5;
        store.Save(data);
        saved = store.Load().Groups[0];
        Check(saved.CompactWidth == 1 && saved.CompactHeight == 9000 && saved.ExpandedWidth == 0 && saved.ExpandedHeight == 0.5,
            "storage does not impose UI limits or replace automatic zero sizes");
        Check(!File.Exists(Path.Combine(sandbox, "reference-a")) && !File.Exists(Path.Combine(sandbox, "original-a")),
            "geometry persistence never moves or creates referenced files");
    }
}
