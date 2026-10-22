using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using LargeFolder;

// Standalone .NET Framework 4 integration tests. Each run creates fresh, named sandboxes.
public static class StorageTests
{
    private static int checks;

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception("FAILED: " + description);
        checks++;
        Console.WriteLine("PASS " + description);
    }

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("Expected test directory and temporary test directory.");
            string testBase = Path.GetFullPath(args[0]);
            string workBase = Path.GetFullPath(args[1]);
            if (!String.Equals(testBase.TrimEnd('\\'), Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Test root must be the compiled test executable's directory.");
            if (!String.Equals(workBase.TrimEnd('\\'), Path.Combine(Path.GetTempPath(), "LargeFolder-tests").TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Secondary test files must remain in the dedicated temporary directory.");

            string token = Guid.NewGuid().ToString("N");
            string root = Path.Combine(testBase, "sandbox-" + token);
            string crossRoot = Path.Combine(workBase, "storage-test-" + token);
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(crossRoot);
            Console.WriteLine("Primary sandbox: " + root);
            Console.WriteLine("Temporary sandbox: " + crossRoot);
            Console.WriteLine("Cross-volume coverage: " + !String.Equals(Path.GetPathRoot(root), Path.GetPathRoot(crossRoot), StringComparison.OrdinalIgnoreCase));
            TestConfiguration(root);
            TestFileOperations(root, crossRoot);
            TestMoveJournal(root, crossRoot);
            TestDesktopShortcutCollection(root);
            Console.WriteLine("All " + checks + " checks passed. Only the two printed sandboxes were modified; the real desktop and application config were not changed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void TestConfiguration(string root)
    {
        DataStore store = new DataStore(Path.Combine(root, "configuration"));
        AppData data = store.Load();
        Check(data.Groups.Count == 1 && data.Groups[0].Name == "常用" && data.CloseOnBlur, "default configuration and group");
        Check(data.Groups[0].Color == "#EAF3EB" && data.Groups[0].Left >= 280, "soft default color and position away from desktop icons");
        data.Groups[0].Name = "backup value";
        data.AlwaysOnTop = true;
        store.Save(data);
        data.Groups[0].Name = "latest value";
        store.Save(data);
        Check(new DataStore(store.RootDirectory).Load().Groups[0].Name == "latest value", "configuration round trip");
        string config = Path.Combine(store.DataDirectory, "config.json");
        Check(File.Exists(config + ".bak"), "atomic save keeps backup");
        File.WriteAllText(config, "{ broken");
        data = store.Load();
        Check(data.Groups[0].Name == "backup value" && data.AlwaysOnTop, "corrupt main recovers last valid backup");
        Check(Directory.GetFiles(store.DataDirectory, "config.json.corrupt-*").Length == 1, "broken main preserved for recovery");
        Check(new DataStore(store.RootDirectory).Load().Groups[0].Name == "backup value", "recovered configuration persisted");
        File.WriteAllText(config, "{\"Groups\":[null,{\"Items\":null}]}");
        data = store.Load();
        Check(data.Groups.Count == 1 && data.Groups[0].Items != null && data.CloseOnBlur && data.Groups[0].Color == "#EAF3EB", "missing and null members normalized");
        File.WriteAllText(config, "null");
        File.WriteAllText(config + ".bak", "{ broken backup");
        data = store.Load();
        Check(data.Groups.Count == 1 && data.Groups[0].Name == "常用", "invalid main and invalid backup return fresh default");
        Check(Directory.GetFiles(store.DataDirectory, "config.json.tmp-*").Length == 0, "successful writes leave no temporary files");
    }

    private static void TestFileOperations(string root, string crossRoot)
    {
        DataStore store = new DataStore(Path.Combine(root, "operations"));
        FolderGroup group = DataStore.NewGroup("测试", 0);
        string source = Path.Combine(root, "source.txt");
        File.WriteAllText(source, "same volume test");
        Check(FileActions.AddReferences(group, new string[] { source, source.ToUpperInvariant() }).Count == 0 && group.Items.Count == 1 && File.Exists(source), "duplicate file references collapse without moving originals");
        string refDirectory = Path.Combine(root, "reference-directory");
        Directory.CreateDirectory(refDirectory);
        Check(FileActions.AddReferences(group, new string[] { refDirectory, refDirectory + "\\" }).Count == 0 && group.Items.Count == 2, "directory trailing-slash references deduplicate");
        Check(FileActions.AddReferences(group, new string[] { Path.Combine(root, "does-not-exist") }).Count == 1 && group.Items.Count == 2, "missing reference returns error without adding a broken item");
        Check(FileActions.CollectToStorage(group, new string[] { source }, store.StorageDirectory).Count == 0, "same-volume file collection succeeds");
        ItemEntry item = group.Items[0];
        Check(!File.Exists(source) && File.Exists(item.Path) && item.OriginalPath == source && group.Items.Count == 2, "collection updates existing reference and records original location");
        string journalPath = Path.Combine(store.DataDirectory, "move-journal.jsonl");
        List<MoveJournalRecord> records = ReadJournal(journalPath);
        Check(records.Count == 2 && records[0].Phase == "started" && records[1].Phase == "completed" &&
            records[0].Operation == "collect" && records[0].Source == source && records[0].Destination == item.Path &&
            records[0].OriginalPath == source && records[0].GroupId == group.Id && records[0].ItemId == item.Id &&
            records[0].OperationId == records[1].OperationId && !String.IsNullOrEmpty(records[0].Time) &&
            !File.Exists(Path.Combine(store.DataDirectory, "config.json")), "durable collect record contains recovery paths and IDs before configuration save");
        File.WriteAllText(source, "do not overwrite");
        Check(FileActions.Restore(item) != null && File.ReadAllText(source) == "do not overwrite" && File.Exists(item.Path), "file restore never overwrites occupied original location");
        File.Delete(source);
        Check(FileActions.Restore(item) == null && item.OriginalPath == null && File.ReadAllText(source) == "same volume test", "same-volume restore clears original-location marker");
        records = ReadJournal(journalPath);
        Check(records[records.Count - 2].Operation == "restore" && records[records.Count - 2].Phase == "started" &&
            records[records.Count - 2].GroupId == group.Id && records[records.Count - 2].ItemId == item.Id &&
            records[records.Count - 1].Phase == "completed", "restore also records a durable started and completed pair");

        string crossFolder = Path.Combine(crossRoot, "nested-folder");
        Directory.CreateDirectory(Path.Combine(crossFolder, "child"));
        File.WriteAllText(Path.Combine(crossFolder, "first.txt"), "one");
        File.WriteAllText(Path.Combine(crossFolder, "child", "second.txt"), "two");
        Check(FileActions.CollectToStorage(group, new string[] { crossFolder }, store.StorageDirectory).Count == 0, "secondary sandbox to storage directory collection succeeds");
        ItemEntry crossItem = group.Items[group.Items.Count - 1];
        Check(!Directory.Exists(crossFolder) && File.ReadAllText(Path.Combine(crossItem.Path, "child", "second.txt")) == "two", "secondary sandbox nested directory contents preserved");
        Directory.CreateDirectory(crossFolder);
        File.WriteAllText(Path.Combine(crossFolder, "keep.txt"), "do not merge");
        Check(FileActions.Restore(crossItem) != null && File.ReadAllText(Path.Combine(crossFolder, "keep.txt")) == "do not merge" && Directory.Exists(crossItem.Path), "directory restore refuses to merge into occupied original location");
        File.Delete(Path.Combine(crossFolder, "keep.txt"));
        Directory.Delete(crossFolder);
        Check(FileActions.Restore(crossItem) == null && File.ReadAllText(Path.Combine(crossFolder, "first.txt")) == "one", "storage to secondary sandbox directory restore succeeds");

        string crossFile = Path.Combine(crossRoot, "cross.txt");
        File.WriteAllText(crossFile, "cross-file");
        Check(FileActions.CollectToStorage(group, new string[] { crossFile }, store.StorageDirectory).Count == 0, "secondary sandbox to storage file collection succeeds");
        ItemEntry crossFileItem = group.Items[group.Items.Count - 1];
        Check(FileActions.Restore(crossFileItem) == null && File.ReadAllText(crossFile) == "cross-file", "storage to secondary sandbox file restore succeeds");

        string first = Path.Combine(crossRoot, "one", "same-name");
        string second = Path.Combine(crossRoot, "two", "same-name");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(first, "a.txt"), "first directory");
        File.WriteAllText(Path.Combine(second, "b.txt"), "second directory");
        int before = group.Items.Count;
        Check(FileActions.CollectToStorage(group, new string[] { first, second }, store.StorageDirectory).Count == 0, "two identically named source directories collected safely");
        ItemEntry firstItem = group.Items[before];
        ItemEntry secondItem = group.Items[before + 1];
        Check(Path.GetFileName(firstItem.Path) == "same-name" && Path.GetFileName(secondItem.Path) == "same-name (2)" &&
            File.ReadAllText(Path.Combine(firstItem.Path, "a.txt")) == "first directory" &&
            File.ReadAllText(Path.Combine(secondItem.Path, "b.txt")) == "second directory", "directory destination collision gets a separate name and preserves both originals");
        Check(FileActions.Restore(firstItem) == null && FileActions.Restore(secondItem) == null &&
            Directory.Exists(first) && Directory.Exists(second), "identically named directories restore to their own original locations");
        Check(FileActions.CollectToStorage(group, new string[] { store.RootDirectory }, store.StorageDirectory).Count == 1 && Directory.Exists(store.RootDirectory), "storage ancestor cannot be moved into itself");
        store.Save(new AppData { Groups = new List<FolderGroup> { group } });
        AppData reloaded = store.Load();
        Check(reloaded.Groups[0].Items.Count == group.Items.Count && reloaded.Groups[0].Items[0].OriginalPath == null, "collected and restored item metadata persists");
    }

    private static void TestMoveJournal(string root, string crossRoot)
    {
        DataStore store = new DataStore(Path.Combine(root, "journal-safety"));
        FolderGroup group = DataStore.NewGroup("日志测试", 0);
        string source = Path.Combine(crossRoot, "journal.txt");
        File.WriteAllText(source, "journal safety");
        string journalPath = Path.Combine(store.DataDirectory, "move-journal.jsonl");
        File.WriteAllText(journalPath, String.Empty);
        File.SetAttributes(journalPath, FileAttributes.ReadOnly);
        try
        {
            Check(FileActions.CollectToStorage(group, new string[] { source }, store.StorageDirectory).Count == 1 &&
                File.ReadAllText(source) == "journal safety" && group.Items.Count == 0,
                "unwritable journal rejects collection before any original is moved");
        }
        finally { File.SetAttributes(journalPath, FileAttributes.Normal); }

        Check(FileActions.CollectToStorage(group, new string[] { source }, store.StorageDirectory).Count == 0, "collection proceeds once journal is writable");
        ItemEntry item = group.Items[0];
        string storedPath = item.Path;
        File.SetAttributes(journalPath, FileAttributes.ReadOnly);
        try
        {
            Check(FileActions.Restore(item) != null && File.Exists(storedPath) && !File.Exists(source) && item.OriginalPath == source,
                "unwritable journal rejects restore and retains both path records");
        }
        finally { File.SetAttributes(journalPath, FileAttributes.Normal); }
        Check(FileActions.Restore(item) == null && File.ReadAllText(source) == "journal safety", "restore proceeds once journal is writable");

        string locked = Path.Combine(root, "locked-journal.txt");
        File.WriteAllText(locked, "locked original");
        int before = group.Items.Count;
        using (FileStream lockedStream = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Check(FileActions.CollectToStorage(group, new string[] { locked }, store.StorageDirectory).Count == 1 &&
                group.Items.Count == before, "failed move does not add a phantom collected item");
        }
        List<MoveJournalRecord> records = ReadJournal(journalPath);
        Check(File.Exists(locked) && records[records.Count - 2].Phase == "started" &&
            records[records.Count - 1].Phase == "interrupted" && records[records.Count - 1].Source == locked,
            "failed system move leaves a durable interrupted outcome and keeps original");

        File.AppendAllText(journalPath, "{incomplete-last-record");
        Check(FileActions.CollectToStorage(group, new string[] { locked }, store.StorageDirectory).Count == 0,
            "truncated previous journal tail does not block a subsequent safe move");
        records = ReadJournal(journalPath);
        Check(records[records.Count - 2].Phase == "started" && records[records.Count - 2].Source == locked &&
            records[records.Count - 1].Phase == "completed", "next record starts on a fresh line after a truncated journal tail");
        Check(FileActions.Restore(group.Items[group.Items.Count - 1]) == null, "journal safety fixture restored");
    }

    private static void TestDesktopShortcutCollection(string root)
    {
        string fixture = Path.Combine(root, "desktop-shortcuts");
        string desktop = Path.Combine(fixture, "Desktop");
        string publicDesktop = Path.Combine(fixture, "PublicDesktop");
        string outside = Path.Combine(fixture, "Desktop-backup");
        Directory.CreateDirectory(desktop); Directory.CreateDirectory(publicDesktop); Directory.CreateDirectory(outside);
        Directory.CreateDirectory(Path.Combine(desktop, "nested"));
        string[] desktopRoots = { desktop, publicDesktop };
        string shortcut = Path.Combine(desktop, "App.lnk");
        string website = Path.Combine(publicDesktop, "Site.URL");
        string externalLink = Path.Combine(outside, "Other.lnk");
        string nestedLink = Path.Combine(desktop, "nested", "Nested.lnk");
        string executable = Path.Combine(desktop, "Portable.exe");
        string document = Path.Combine(desktop, "Note.txt");
        string target = Path.Combine(fixture, "application.exe");
        File.WriteAllText(target, "application stays here");
        File.WriteAllText(shortcut, "target=" + target); File.WriteAllText(website, "[InternetShortcut]\r\nURL=https://example.invalid/");
        File.WriteAllText(externalLink, "external"); File.WriteAllText(nestedLink, "nested");
        File.WriteAllText(executable, "portable program"); File.WriteAllText(document, "document");
        string fakeLinkFolder = Path.Combine(desktop, "Folder.lnk"); Directory.CreateDirectory(fakeLinkFolder);
        Check(FileActions.IsDesktopShortcut(shortcut, desktopRoots) && FileActions.IsDesktopShortcut(website, desktopRoots), "personal and public desktop shortcut types are recognized case-insensitively");
        Check(!FileActions.IsDesktopShortcut(externalLink, desktopRoots) && !FileActions.IsDesktopShortcut(nestedLink, desktopRoots), "desktop siblings and nested folders are not auto-collected");
        Check(!FileActions.IsDesktopShortcut(executable, desktopRoots) && !FileActions.IsDesktopShortcut(fakeLinkFolder, desktopRoots) && !FileActions.IsDesktopShortcut(Path.Combine(desktop, "missing.lnk"), desktopRoots), "executables, disguised directories and missing files are not desktop shortcuts");
        DataStore store = new DataStore(Path.Combine(fixture, "app"));
        FolderGroup group = DataStore.NewGroup("agent", 0);
        var errors = FileActions.AddToGroup(group, new string[] { document, shortcut, shortcut.ToUpperInvariant(), website, nestedLink, executable, externalLink }, store.StorageDirectory, false, desktopRoots);
        Check(errors.Count == 0 && group.Items.Count == 6, "mixed add auto-collects shortcuts and deduplicates repeated input without errors");
        Check(group.Items[0].Path == document && group.Items[1].OriginalPath == shortcut && group.Items[2].OriginalPath == website && group.Items[3].Path == nestedLink, "mixed add preserves selection order and records original shortcut paths");
        Check(!File.Exists(shortcut) && !File.Exists(website) && File.Exists(group.Items[1].Path) && File.Exists(group.Items[2].Path), "collected shortcuts disappear from both desktop directories and remain accessible in storage");
        Check(File.ReadAllText(group.Items[1].Path) == "target=" + target && File.ReadAllText(target) == "application stays here", "shortcut bytes are preserved and the application target is untouched");
        Check(File.Exists(document) && File.Exists(executable) && File.Exists(nestedLink) && File.Exists(externalLink), "ordinary files and non-desktop shortcuts retain their original locations");
        string referenceLink = Path.Combine(desktop, "Reference.lnk"); File.WriteAllText(referenceLink, "keep desktop icon");
        Check(FileActions.AddToGroup(group, new string[] { referenceLink }, store.StorageDirectory, true, desktopRoots).Count == 0 && File.Exists(referenceLink) && group.Items[6].OriginalPath == null, "reference-only mode keeps a desktop shortcut in place");
        ItemEntry reference = group.Items[6]; string referenceId = reference.Id;
        Check(FileActions.AddToGroup(group, new string[] { referenceLink }, store.StorageDirectory, false, desktopRoots).Count == 0 && Object.ReferenceEquals(group.Items[6], reference) && reference.Id == referenceId && group.Items.Count == 7 && !File.Exists(referenceLink), "existing desktop references convert to collection without duplicate entries or order changes");
        Check(FileActions.Restore(reference) == null && File.ReadAllText(referenceLink) == "keep desktop icon" && reference.OriginalPath == null, "an auto-collected shortcut can be restored to its desktop location");
        store.Save(new AppData { Groups = new List<FolderGroup> { group } });
        AppData loaded = store.Load();
        Check(loaded.Groups[0].Items[1].OriginalPath == shortcut && loaded.Groups[0].Items[6].Id == referenceId, "auto-collection and restored reference metadata survive persistence");
    }

    private static List<MoveJournalRecord> ReadJournal(string path)
    {
        List<MoveJournalRecord> records = new List<MoveJournalRecord>();
        foreach (string line in File.ReadAllLines(path))
        {
            if (String.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                    records.Add((MoveJournalRecord)new DataContractJsonSerializer(typeof(MoveJournalRecord)).ReadObject(stream));
            }
            catch { /* Recovery tools can skip an incomplete final record after a crash. */ }
        }
        return records;
    }
}
