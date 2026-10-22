using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LargeFolder
{
    /// <summary>Shell icons shared by the folder previews and the expanded view.</summary>
    public static class ShellIcons
    {
        private const uint ShgfiIcon = 0x00000100;
        private const uint ShgfiSmallIcon = 0x00000001;
        private const uint ShgfiUseFileAttributes = 0x00000010;
        private const uint ShgfiAddOverlays = 0x00000020;
        private const uint FileAttributeDirectory = 0x00000010;
        private const uint FileAttributeNormal = 0x00000080;
        private const uint InvalidFileAttributes = 0xffffffff;
        private const uint DriveRemote = 4;

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<string, ImageSource> Cache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns a frozen, transparent Shell icon. Network locations use an
        /// extension-based icon without checking their existence or contacting them.
        /// </summary>
        public static ImageSource Get(string path, int size)
        {
            int requestedSize = size > 0 ? Math.Min(size, 256) : 32;
            string normalizedPath = NormalizePath(path);
            string key = requestedSize.ToString(CultureInfo.InvariantCulture) + "\0" + normalizedPath;
            ImageSource cached;
            lock (CacheLock)
            {
                if (Cache.TryGetValue(key, out cached))
                    return cached;
            }

            uint attributes = FileAttributeNormal;
            bool existsLocally = false;
            if (CanProbeLocally(normalizedPath))
            {
                uint foundAttributes = GetFileAttributes(normalizedPath);
                if (foundAttributes != InvalidFileAttributes)
                {
                    attributes = foundAttributes;
                    existsLocally = true;
                }
            }

            if (!existsLocally)
                attributes = GuessAttributes(normalizedPath);

            ImageSource result = existsLocally
                ? ReadIcon(normalizedPath, attributes, requestedSize, false)
                : null;

            if (result == null)
            {
                // Pass a synthetic name, never the original network path. Some
                // Shell providers can inspect a path even with USEFILEATTRIBUTES.
                string genericName = GetGenericName(normalizedPath, attributes);
                result = ReadIcon(genericName, attributes, requestedSize, true);
            }
            if (result == null)
                result = ReadIcon("item", FileAttributeNormal, requestedSize, true);
            if (result == null)
                result = CreateEmptyIcon(requestedSize);

            lock (CacheLock)
            {
                // Another caller may have filled this entry while the icon loaded.
                if (Cache.TryGetValue(key, out cached))
                    return cached;
                Cache.Add(key, result);
            }
            return result;
        }

        /// <summary>Opens an item using its Windows association. Errors are not swallowed.</summary>
        public static void Open(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
                throw new ArgumentException("请选择要打开的文件或文件夹。", "path");

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }

        /// <summary>Selects an item in Explorer, including a directory in its parent.</summary>
        public static void Reveal(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
                throw new ArgumentException("请选择要定位的文件或文件夹。", "path");

            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath);
            bool isRoot = String.Equals(
                fullPath.TrimEnd('\\', '/'),
                (root ?? String.Empty).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase);

            // Explorer's /select syntax accepts both files and folders. A drive
            // or share root has no parent in which it can be selected.
            string arguments = isRoot
                ? "\"" + fullPath + "\""
                : "/select,\"" + fullPath.TrimEnd('\\', '/') + "\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = arguments,
                UseShellExecute = true
            });
        }

        private static string NormalizePath(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
                return String.Empty;
            try
            {
                return Path.GetFullPath(path);
            }
            catch (ArgumentException) { return path; }
            catch (NotSupportedException) { return path; }
            catch (PathTooLongException) { return path; }
        }

        private static bool CanProbeLocally(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
                return false;

            string candidate = path.Replace('/', '\\');
            // Reject UNC, device namespaces and extended UNC before any file
            // attributes/Exists call. An unreachable share can block those calls.
            if (candidate.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                return false;
            if (candidate.StartsWith(@"\\?\", StringComparison.Ordinal))
                candidate = candidate.Substring(4);
            else if (candidate.StartsWith(@"\\", StringComparison.Ordinal))
                return false;

            if (candidate.Length < 3 || !Char.IsLetter(candidate[0]) ||
                candidate[1] != ':' || candidate[2] != '\\')
                return false;

            // A mapped drive also points at a network share. GetDriveType only
            // asks Windows about the root; no target file is opened or resolved.
            string root = candidate.Substring(0, 3);
            return GetDriveType(root) != DriveRemote;
        }

        private static uint GuessAttributes(string path)
        {
            if (String.IsNullOrEmpty(path))
                return FileAttributeNormal;
            if (path.EndsWith("\\", StringComparison.Ordinal) ||
                path.EndsWith("/", StringComparison.Ordinal))
                return FileAttributeDirectory;
            try
            {
                // An inaccessible/missing item has no inspectable metadata. An
                // extensionless path is treated as a folder, the usual case for
                // dropped folders and unavailable network shares.
                if (!Path.HasExtension(path))
                    return FileAttributeDirectory;
            }
            catch (ArgumentException) { }
            return FileAttributeNormal;
        }

        private static string GetGenericName(string path, uint attributes)
        {
            if ((attributes & FileAttributeDirectory) != 0)
                return "Folder";
            try
            {
                string extension = Path.GetExtension(path);
                if (!String.IsNullOrEmpty(extension))
                    return "item" + extension;
            }
            catch (ArgumentException) { }
            return "item";
        }

        private static ImageSource ReadIcon(string path, uint attributes, int size, bool generic)
        {
            ShellFileInfo info = new ShellFileInfo();
            uint flags = ShgfiIcon | (size <= 16 ? ShgfiSmallIcon : 0);
            if (generic)
                flags |= ShgfiUseFileAttributes;
            else
                flags |= ShgfiAddOverlays;

            try
            {
                IntPtr result = SHGetFileInfo(path, attributes, ref info,
                    (uint)Marshal.SizeOf(typeof(ShellFileInfo)), flags);
                if (result == IntPtr.Zero || info.Icon == IntPtr.Zero)
                    return null;

                BitmapSource bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    info.Icon, Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(size, size));
                bitmap.Freeze();
                return bitmap;
            }
            catch (ArgumentException) { return null; }
            catch (System.Runtime.InteropServices.ExternalException) { return null; }
            finally
            {
                if (info.Icon != IntPtr.Zero)
                    DestroyIcon(info.Icon);
            }
        }

        private static ImageSource CreateEmptyIcon(int size)
        {
            // A final, transparent fallback if the Windows Shell cannot supply
            // even a generic icon (for example during Explorer shutdown).
            BitmapSource bitmap = BitmapSource.Create(size, size, 96, 96,
                PixelFormats.Bgra32, null, new byte[size * size * 4], size * 4);
            bitmap.Freeze();
            return bitmap;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ShellFileInfo
        {
            public IntPtr Icon;
            public int IconIndex;
            public uint Attributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string TypeName;
        }

        [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes,
            ref ShellFileInfo info, uint infoSize, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr icon);

        [DllImport("kernel32.dll", EntryPoint = "GetFileAttributesW", CharSet = CharSet.Unicode,
            SetLastError = true)]
        private static extern uint GetFileAttributes(string path);

        [DllImport("kernel32.dll", EntryPoint = "GetDriveTypeW", CharSet = CharSet.Unicode)]
        private static extern uint GetDriveType(string rootPath);
    }
}
