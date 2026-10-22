using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace LargeFolder
{
    [DataContract]
    public sealed class AppData
    {
        [DataMember(Order = 1)] public List<FolderGroup> Groups { get; set; }
        [DataMember(Order = 2)] public bool AlwaysOnTop { get; set; }
        [DataMember(Order = 3)] public bool StartWithWindows { get; set; }
        [DataMember(Order = 4)] public bool CloseOnBlur { get; set; }

        public AppData()
        {
            Groups = new List<FolderGroup>();
            CloseOnBlur = true;
        }

        [OnDeserializing]
        private void BeforeDeserialization(StreamingContext context)
        {
            Groups = new List<FolderGroup>();
            CloseOnBlur = true;
        }
    }

    [DataContract]
    public sealed class FolderGroup
    {
        [DataMember(Order = 1)] public string Id { get; set; }
        [DataMember(Order = 2)] public string Name { get; set; }
        [DataMember(Order = 3)] public string Color { get; set; }
        [DataMember(Order = 4)] public double Left { get; set; }
        [DataMember(Order = 5)] public double Top { get; set; }
        [DataMember(Order = 6)] public List<ItemEntry> Items { get; set; }
        [DataMember(Order = 7, EmitDefaultValue = false)] public double CompactWidth { get; set; }
        [DataMember(Order = 8, EmitDefaultValue = false)] public double CompactHeight { get; set; }
        [DataMember(Order = 9, EmitDefaultValue = false)] public double ExpandedWidth { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public double ExpandedHeight { get; set; }

        public FolderGroup()
        {
            Items = new List<ItemEntry>();
        }

        [OnDeserializing]
        private void BeforeDeserialization(StreamingContext context)
        {
            Items = new List<ItemEntry>();
        }
    }

    [DataContract]
    public sealed class ItemEntry
    {
        [DataMember(Order = 1)] public string Id { get; set; }
        [DataMember(Order = 2)] public string Path { get; set; }
        [DataMember(Order = 3, EmitDefaultValue = false)] public string OriginalPath { get; set; }
        [DataMember(Order = 4)] public DateTime AddedAt { get; set; }
    }

    /// <summary>Configuration and application-owned storage; never enumerates or changes the desktop.</summary>
    public sealed class DataStore
    {
        private readonly object syncRoot = new object();
        private readonly string configPath;
        private readonly string backupPath;

        public string RootDirectory { get; private set; }
        public string DataDirectory { get; private set; }
        public string StorageDirectory { get; private set; }

        public DataStore(string baseDirectory)
        {
            if (String.IsNullOrWhiteSpace(baseDirectory)) throw new ArgumentException("软件目录不能为空。", "baseDirectory");
            RootDirectory = System.IO.Path.GetFullPath(baseDirectory);
            DataDirectory = System.IO.Path.Combine(RootDirectory, "data");
            StorageDirectory = System.IO.Path.Combine(RootDirectory, "storage");
            configPath = System.IO.Path.Combine(DataDirectory, "config.json");
            backupPath = configPath + ".bak";
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(StorageDirectory);
            FileActions.JournalDirectory = DataDirectory;
        }

        public AppData Load()
        {
            lock (syncRoot)
            {
                AppData data = null;
                bool mainWasInvalid = false;
                if (File.Exists(configPath))
                {
                    try { data = ReadData(configPath); }
                    catch (Exception ex) { mainWasInvalid = true; Log(ex); }
                }

                if (data != null) return Normalize(data);

                if (File.Exists(backupPath))
                {
                    try { data = ReadData(backupPath); }
                    catch (Exception ex) { Log(ex); }
                }

                data = Normalize(data ?? new AppData());
                // Keep a broken main file for diagnosis and preserve a valid backup during recovery.
                if (mainWasInvalid)
                {
                    try
                    {
                        File.Copy(configPath, configPath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture), false);
                    }
                    catch (Exception ex) { Log(ex); }
                }
                try { WriteData(data, false); }
                catch (Exception ex) { Log(ex); }
                return data;
            }
        }

        public void Save(AppData data)
        {
            if (data == null) throw new ArgumentNullException("data");
            lock (syncRoot)
            {
                WriteData(Normalize(data), true);
            }
        }

        public void Log(Exception ex)
        {
            if (ex == null) return;
            try
            {
                lock (syncRoot)
                {
                    Directory.CreateDirectory(DataDirectory);
                    File.AppendAllText(System.IO.Path.Combine(DataDirectory, "error.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + Environment.NewLine +
                        ex.ToString() + Environment.NewLine + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { /* Logging must not hide the original failure. */ }
        }

        public static FolderGroup NewGroup(string name, int index)
        {
            string[] colors = { "#EAF3EB", "#E7EFF8", "#FAF0DF", "#EFEAF7", "#F8E9ED" };
            index = Math.Max(0, index);
            return new FolderGroup
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = String.IsNullOrWhiteSpace(name) ? "未命名" : name.Trim(),
                Color = colors[index % colors.Length],
                Left = 280 + (index % 3) * 260,
                Top = 80 + ((index / 3) % 3) * 260,
                Items = new List<ItemEntry>()
            };
        }

        private static AppData ReadData(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length == 0 || stream.Length > 32L * 1024L * 1024L)
                    throw new SerializationException("配置文件为空或过大。");
                AppData result = new DataContractJsonSerializer(typeof(AppData)).ReadObject(stream) as AppData;
                if (result == null) throw new SerializationException("配置文件没有有效内容。");
                return result;
            }
        }

        private void WriteData(AppData data, bool keepBackup)
        {
            Directory.CreateDirectory(DataDirectory);
            string tempPath = configPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(AppData)).WriteObject(stream, data);
                    stream.Flush(true);
                }
                if (File.Exists(configPath))
                    File.Replace(tempPath, configPath, keepBackup ? backupPath : null, true);
                else
                    File.Move(tempPath, configPath);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                catch (Exception ex) { Log(ex); }
            }
        }

        private static AppData Normalize(AppData data)
        {
            if (data.Groups == null) data.Groups = new List<FolderGroup>();
            HashSet<string> groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<FolderGroup> validGroups = new List<FolderGroup>();
            foreach (FolderGroup group in data.Groups)
            {
                if (group == null) continue;
                if (!FileActions.IsSafeGroupId(group.Id) || !groupIds.Add(group.Id))
                {
                    group.Id = Guid.NewGuid().ToString("N");
                    groupIds.Add(group.Id);
                }
                if (String.IsNullOrWhiteSpace(group.Name)) group.Name = "未命名";
                if (String.IsNullOrWhiteSpace(group.Color)) group.Color = "#EAF3EB";
                if (Double.IsNaN(group.Left) || Double.IsInfinity(group.Left)) group.Left = 280;
                if (Double.IsNaN(group.Top) || Double.IsInfinity(group.Top)) group.Top = 80;
                group.CompactWidth = NormalizeSize(group.CompactWidth);
                group.CompactHeight = NormalizeSize(group.CompactHeight);
                group.ExpandedWidth = NormalizeSize(group.ExpandedWidth);
                group.ExpandedHeight = NormalizeSize(group.ExpandedHeight);
                List<ItemEntry> validItems = new List<ItemEntry>();
                HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (group.Items != null)
                {
                    foreach (ItemEntry item in group.Items)
                    {
                        if (item == null || String.IsNullOrWhiteSpace(item.Path)) continue;
                        string comparisonPath;
                        try { comparisonPath = FileActions.CanonicalPath(item.Path); }
                        catch { comparisonPath = item.Path; }
                        if (!paths.Add(comparisonPath)) continue;
                        if (String.IsNullOrWhiteSpace(item.Id)) item.Id = Guid.NewGuid().ToString("N");
                        if (item.AddedAt == default(DateTime)) item.AddedAt = DateTime.Now;
                        validItems.Add(item);
                    }
                }
                group.Items = validItems;
                validGroups.Add(group);
            }
            if (validGroups.Count == 0) validGroups.Add(NewGroup("常用", 0));
            data.Groups = validGroups;
            return data;
        }

        // Zero remains the legacy automatic size. Window constraints are applied by the UI.
        private static double NormalizeSize(double value)
        {
            return Double.IsNaN(value) || Double.IsInfinity(value) || value < 0 ? 0 : value;
        }
    }

    public static class FileActions
    {
        private static readonly object JournalSync = new object();

        // Set by DataStore. A started record is flushed to disk before any original is moved.
        public static string JournalDirectory { get; set; }

        public static string[] GetDesktopDirectories()
        {
            return new string[] {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            };
        }

        public static bool IsDesktopShortcut(string path)
        {
            return IsDesktopShortcut(path, GetDesktopDirectories());
        }

        internal static bool IsDesktopShortcut(string path, IEnumerable<string> desktopDirectories)
        {
            try {
                string candidate = CanonicalPath(path);
                string extension = System.IO.Path.GetExtension(candidate);
                if ((!String.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase) &&
                    !String.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase)) || !File.Exists(candidate)) return false;
                string parent = System.IO.Path.GetDirectoryName(candidate);
                foreach (string desktop in desktopDirectories ?? new string[0])
                    if (!String.IsNullOrWhiteSpace(desktop) && String.Equals(parent, CanonicalPath(desktop), StringComparison.OrdinalIgnoreCase)) return true;
            } catch { }
            return false;
        }

        /// <summary>Desktop shortcuts are collected; Ctrl/reference-only mode keeps all originals.</summary>
        public static List<string> AddToGroup(FolderGroup group, IEnumerable<string> paths, string storageDirectory, bool referencesOnly)
        {
            return AddToGroup(group, paths, storageDirectory, referencesOnly, GetDesktopDirectories());
        }

        internal static List<string> AddToGroup(FolderGroup group, IEnumerable<string> paths, string storageDirectory, bool referencesOnly, IEnumerable<string> desktopDirectories)
        {
            List<string> errors = new List<string>();
            if (paths == null) return errors;
            HashSet<string> processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths) {
                try { if (!processed.Add(CanonicalPath(path))) continue; }
                catch { errors.AddRange(AddReferences(group, new string[] { path })); continue; }
                if (!referencesOnly && IsDesktopShortcut(path, desktopDirectories))
                    errors.AddRange(CollectToStorage(group, new string[] { path }, storageDirectory));
                else errors.AddRange(AddReferences(group, new string[] { path }));
            }
            return errors;
        }

        public static List<string> AddReferences(FolderGroup group, IEnumerable<string> paths)
        {
            List<string> errors = new List<string>();
            if (group == null) { errors.Add("没有选中的大文件夹。"); return errors; }
            if (group.Items == null) group.Items = new List<ItemEntry>();
            if (paths == null) return errors;
            HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ItemEntry item in group.Items)
            {
                if (item == null || String.IsNullOrWhiteSpace(item.Path)) continue;
                try { known.Add(CanonicalPath(item.Path)); }
                catch { known.Add(item.Path); }
            }
            foreach (string candidate in paths)
            {
                try
                {
                    string path = CanonicalPath(candidate);
                    if (!Exists(path)) throw new FileNotFoundException("文件不存在或无法访问。", path);
                    if (!known.Add(path)) continue;
                    group.Items.Add(NewItem(path, null));
                }
                catch (Exception ex) { errors.Add(DisplayPath(candidate) + "：" + ex.Message); }
            }
            return errors;
        }

        /// <summary>Call only after the user explicitly chooses to move originals into storage.</summary>
        public static List<string> CollectToStorage(FolderGroup group, IEnumerable<string> paths, string storageDirectory)
        {
            List<string> errors = new List<string>();
            if (group == null) { errors.Add("没有选中的大文件夹。"); return errors; }
            if (group.Items == null) group.Items = new List<ItemEntry>();
            if (paths == null) return errors;
            string groupDirectory;
            string journalDirectory;
            try
            {
                if (String.IsNullOrWhiteSpace(group.Id)) group.Id = Guid.NewGuid().ToString("N");
                if (!IsSafeGroupId(group.Id)) throw new IOException("大文件夹编号无效。");
                string storageRoot = CanonicalPath(storageDirectory);
                groupDirectory = CanonicalPath(System.IO.Path.Combine(storageRoot, group.Id));
                journalDirectory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(storageRoot), "data");
                Directory.CreateDirectory(groupDirectory);
            }
            catch (Exception ex) { errors.Add("无法打开收纳目录：" + ex.Message); return errors; }

            HashSet<string> processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in paths)
            {
                string source = null;
                string destination = null;
                ItemEntry existing = null;
                string original = null;
                MoveJournalRecord journal = null;
                bool journalStarted = false;
                bool moveCompleted = false;
                try
                {
                    source = CanonicalPath(candidate);
                    if (!processed.Add(source)) continue;
                    if (!Exists(source)) throw new FileNotFoundException("文件不存在或无法访问。", source);
                    if (IsInside(source, groupDirectory))
                    {
                        errors.AddRange(AddReferences(group, new string[] { source }));
                        continue;
                    }
                    bool directory = Directory.Exists(source);
                    if (directory && IsInside(groupDirectory, source))
                        throw new IOException("不能把收纳目录本身或它的上级文件夹收纳到自身内部。");
                    string name = System.IO.Path.GetFileName(source);
                    if (String.IsNullOrEmpty(name)) throw new IOException("不能收纳整个磁盘或共享根目录。");
                    existing = FindItem(group, source);
                    original = existing != null && !String.IsNullOrWhiteSpace(existing.OriginalPath)
                        ? existing.OriginalPath : source;
                    destination = UniqueDestination(groupDirectory, name, directory);
                    if (existing == null) existing = NewItem(source, null);
                    else if (String.IsNullOrWhiteSpace(existing.Id)) existing.Id = Guid.NewGuid().ToString("N");
                    journal = NewJournalRecord("collect", group.Id, existing.Id, source, destination, original);
                    AppendJournal(journalDirectory, journal, "started", null);
                    journalStarted = true;
                    MoveOriginal(source, destination, directory);
                    if (Exists(source) || !Exists(destination))
                        throw new IOException("系统移动未完整完成。");
                    TrackCollected(group, existing, destination, original);
                    moveCompleted = true;
                    AppendJournal(journalDirectory, journal, "completed", null);
                }
                catch (Exception ex)
                {
                    if (journalStarted) TryAppendJournal(journalDirectory, journal, "interrupted", ex.Message);
                    // Shell moves between disks can be interrupted after moving some children.
                    // Keep both locations discoverable through Path + OriginalPath; never delete either side.
                    if (!String.IsNullOrEmpty(destination) && Exists(destination))
                    {
                        TrackCollected(group, existing, destination, original ?? source);
                        errors.Add(DisplayPath(candidate) + (moveCompleted ? "：原件已收纳，但完成日志无法写入；移动前记录仍保留。" : "：移动未完整完成，已保留收纳条目。") + "请检查原位置“" +
                            (original ?? source) + "”和收纳位置“" + destination + "”。请保留并核对两处现有内容。原因：" + ex.Message);
                    }
                    else errors.Add(DisplayPath(candidate) + "：" + ex.Message);
                }
            }
            return errors;
        }

        /// <summary>Returns null on success; a readable message on failure. Never overwrites an occupied original path.</summary>
        public static string Restore(ItemEntry item)
        {
            if (item == null) return "没有选中的项目。";
            if (String.IsNullOrWhiteSpace(item.OriginalPath)) return "该项目是引用，没有需要还原的原文件。";
            string source = null;
            string target = null;
            MoveJournalRecord journal = null;
            bool journalStarted = false;
            bool moveCompleted = false;
            try
            {
                source = CanonicalPath(item.Path);
                target = CanonicalPath(item.OriginalPath);
                if (!Exists(source)) return "收纳位置中的文件不存在或无法访问：" + source;
                if (String.Equals(source, target, StringComparison.OrdinalIgnoreCase))
                {
                    item.Path = target;
                    item.OriginalPath = null;
                    return null;
                }
                if (Exists(target)) return "原位置已有同名文件或文件夹，未覆盖任何内容：" + target + "。请先核对两处内容，再移开重名项。";
                bool directory = Directory.Exists(source);
                if (directory && IsInside(target, source)) return "还原位置不能位于待还原文件夹内部。";
                string parent = System.IO.Path.GetDirectoryName(target);
                if (String.IsNullOrEmpty(parent)) return "原位置无效，不能还原到磁盘根目录。";
                Directory.CreateDirectory(parent);
                if (String.IsNullOrWhiteSpace(item.Id)) item.Id = Guid.NewGuid().ToString("N");
                journal = NewJournalRecord("restore", JournalGroupId(source), item.Id, source, target, target);
                AppendJournal(JournalDirectory, journal, "started", null);
                journalStarted = true;
                MoveOriginal(source, target, directory);
                if (Exists(source) || !Exists(target)) throw new IOException("系统移动未完整完成。");
                item.Path = target;
                item.OriginalPath = null;
                moveCompleted = true;
                AppendJournal(JournalDirectory, journal, "completed", null);
                return null;
            }
            catch (Exception ex)
            {
                if (journalStarted) TryAppendJournal(JournalDirectory, journal, "interrupted", ex.Message);
                if (moveCompleted) return "原件已成功还原，但完成日志无法写入；移动前记录仍保留。原因：" + ex.Message;
                if (!String.IsNullOrEmpty(target) && Exists(target))
                    return "还原未完整完成。请检查原位置“" + target + "”和收纳位置“" + source +
                        "”；已保留条目的原位置记录。请保留并核对两处现有内容。原因：" + ex.Message;
                return "还原失败：" + ex.Message;
            }
        }

        public static List<string> GetDesktopItems()
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] desktops = GetDesktopDirectories();
            foreach (string desktop in desktops)
            {
                if (String.IsNullOrWhiteSpace(desktop) || !Directory.Exists(desktop)) continue;
                try
                {
                    foreach (string entry in Directory.EnumerateFileSystemEntries(desktop))
                    {
                        try
                        {
                            if (String.Equals(System.IO.Path.GetFileName(entry), "desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                            FileAttributes attributes = File.GetAttributes(entry);
                            if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                            result.Add(CanonicalPath(entry));
                        }
                        catch { /* An inaccessible item must not prevent the rest of the desktop from loading. */ }
                    }
                }
                catch { /* An unavailable public or redirected desktop is optional. */ }
            }
            List<string> sorted = new List<string>(result);
            sorted.Sort(delegate(string a, string b)
            {
                int order = StringComparer.CurrentCultureIgnoreCase.Compare(System.IO.Path.GetFileName(a), System.IO.Path.GetFileName(b));
                return order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(a, b);
            });
            return sorted;
        }

        internal static string CanonicalPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("路径不能为空。");
            string full = System.IO.Path.GetFullPath(path);
            string root = System.IO.Path.GetPathRoot(full);
            while (full.Length > root.Length && (full[full.Length - 1] == System.IO.Path.DirectorySeparatorChar || full[full.Length - 1] == System.IO.Path.AltDirectorySeparatorChar))
                full = full.Substring(0, full.Length - 1);
            return full;
        }

        internal static bool IsSafeGroupId(string id)
        {
            return !String.IsNullOrWhiteSpace(id) && id != "." && id != ".." &&
                id.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0 &&
                id.IndexOf(System.IO.Path.DirectorySeparatorChar) < 0 && id.IndexOf(System.IO.Path.AltDirectorySeparatorChar) < 0;
        }

        private static bool Exists(string path)
        {
            return !String.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path));
        }

        private static bool IsInside(string path, string directory)
        {
            return String.Equals(path, directory, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(directory.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string DisplayPath(string path)
        {
            return String.IsNullOrWhiteSpace(path) ? "（空路径）" : path;
        }

        private static ItemEntry NewItem(string path, string originalPath)
        {
            return new ItemEntry { Id = Guid.NewGuid().ToString("N"), Path = path, OriginalPath = originalPath, AddedAt = DateTime.Now };
        }

        private static ItemEntry FindItem(FolderGroup group, string path)
        {
            foreach (ItemEntry item in group.Items)
            {
                if (item == null || String.IsNullOrWhiteSpace(item.Path)) continue;
                try { if (String.Equals(CanonicalPath(item.Path), path, StringComparison.OrdinalIgnoreCase)) return item; }
                catch { }
            }
            return null;
        }

        private static void TrackCollected(FolderGroup group, ItemEntry existing, string destination, string original)
        {
            if (existing == null)
                group.Items.Add(NewItem(destination, original));
            else
            {
                existing.Path = destination;
                existing.OriginalPath = original;
                if (!group.Items.Contains(existing)) group.Items.Add(existing);
            }
        }

        private static MoveJournalRecord NewJournalRecord(string operation, string groupId, string itemId,
            string source, string destination, string original)
        {
            return new MoveJournalRecord
            {
                OperationId = Guid.NewGuid().ToString("N"), Operation = operation,
                GroupId = groupId, ItemId = itemId, Source = source, Destination = destination,
                OriginalPath = original
            };
        }

        private static string JournalGroupId(string source)
        {
            if (String.IsNullOrWhiteSpace(JournalDirectory)) return null;
            try
            {
                string root = CanonicalPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(CanonicalPath(JournalDirectory)), "storage"));
                if (!IsInside(source, root) || String.Equals(source, root, StringComparison.OrdinalIgnoreCase)) return null;
                string relative = source.Substring(root.Length).TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
                int separator = relative.IndexOfAny(new char[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar });
                return separator < 0 ? relative : relative.Substring(0, separator);
            }
            catch { return null; }
        }

        private static void AppendJournal(string directory, MoveJournalRecord record, string phase, string message)
        {
            if (String.IsNullOrWhiteSpace(directory)) throw new IOException("未设置移动日志目录，已拒绝移动原件。");
            lock (JournalSync)
            {
                record.Time = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
                record.Phase = phase;
                record.Message = message;
                byte[] bytes;
                using (MemoryStream memory = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(MoveJournalRecord)).WriteObject(memory, record);
                    bytes = memory.ToArray();
                }
                string journalPath = System.IO.Path.Combine(directory, "move-journal.jsonl");
                try
                {
                    Directory.CreateDirectory(directory);
                    using (FileStream stream = new FileStream(journalPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
                    {
                        // An interrupted final append must not swallow the next valid record.
                        if (stream.Length > 0)
                        {
                            stream.Seek(-1, SeekOrigin.End);
                            int tail = stream.ReadByte();
                            if (tail != 10) stream.WriteByte(10);
                        }
                        else stream.Seek(0, SeekOrigin.End);
                        stream.Write(bytes, 0, bytes.Length);
                        stream.WriteByte(10);
                        stream.Flush(true);
                    }
                }
                catch (Exception ex)
                {
                    throw new IOException("无法持久保存移动记录：" + journalPath + "。为保留原路径记录，尚未开始的移动已取消。", ex);
                }
            }
        }

        private static void TryAppendJournal(string directory, MoveJournalRecord record, string phase, string message)
        {
            try { AppendJournal(directory, record, phase, message); }
            catch { /* The already-flushed started record still contains both recovery paths. */ }
        }

        private static string UniqueDestination(string directory, string name, bool isDirectory)
        {
            string destination = System.IO.Path.Combine(directory, name);
            string extension = isDirectory ? String.Empty : System.IO.Path.GetExtension(name);
            string stem = isDirectory ? name : System.IO.Path.GetFileNameWithoutExtension(name);
            int suffix = 2;
            while (Exists(destination))
            {
                destination = System.IO.Path.Combine(directory, stem + " (" + suffix.ToString(CultureInfo.InvariantCulture) + ")" + extension);
                suffix++;
            }
            return destination;
        }

        private static void MoveOriginal(string source, string destination, bool directory)
        {
            if (Exists(destination)) throw new IOException("目标位置已有同名文件或文件夹，未覆盖。 ");
            if (String.Equals(System.IO.Path.GetPathRoot(source), System.IO.Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase))
            {
                if (directory) Directory.Move(source, destination);
                else File.Move(source, destination);
                return;
            }
            // Windows Shell handles cross-volume operations. We never implement copy-then-delete ourselves.
            if (directory)
                FileSystem.MoveDirectory(source, destination, UIOption.OnlyErrorDialogs, UICancelOption.ThrowException);
            else
                FileSystem.MoveFile(source, destination, UIOption.OnlyErrorDialogs, UICancelOption.ThrowException);
        }
    }

    [DataContract]
    internal sealed class MoveJournalRecord
    {
        [DataMember(Order = 1)] public string Time { get; set; }
        [DataMember(Order = 2)] public string OperationId { get; set; }
        [DataMember(Order = 3)] public string Phase { get; set; }
        [DataMember(Order = 4)] public string Operation { get; set; }
        [DataMember(Order = 5, EmitDefaultValue = false)] public string GroupId { get; set; }
        [DataMember(Order = 6)] public string ItemId { get; set; }
        [DataMember(Order = 7)] public string Source { get; set; }
        [DataMember(Order = 8)] public string Destination { get; set; }
        [DataMember(Order = 9)] public string OriginalPath { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public string Message { get; set; }
    }
}
