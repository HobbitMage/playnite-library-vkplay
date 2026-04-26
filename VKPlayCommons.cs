using Microsoft.Win32;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace VKPlay.Commons
{

    // re-using source code from Playnite
    public class Program
    {
        public string Path { get; set; }
        public string Arguments { get; set; }
        public string Icon { get; set; }
        public int IconIndex { get; set; }
        public string WorkDir { get; set; }
        public string Name { get; set; }
        public string AppId { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }

    public class UninstallProgram
    {
        public string DisplayIcon { get; set; }
        public string DisplayName { get; set; }
        public string DisplayVersion { get; set; }
        public string InstallLocation { get; set; }
        public string Publisher { get; set; }
        public string UninstallString { get; set; }
        public string URLInfoAbout { get; set; }
        public string RegistryKeyName { get; set; }
        public string Path { get; set; }

        public override string ToString()
        {
            return DisplayName ?? RegistryKeyName;
        }
    }

    public partial class Programs
    {
        private static readonly string[] scanFileExclusionMasks = new string[]
        {
            "uninst",
            "setup",
            @"unins\d+",
            "Config",
            "DXSETUP",
            @"vc_redist\.x64",
            @"vc_redist\.x86",
            @"^UnityCrashHandler32\.exe$",
            @"^UnityCrashHandler64\.exe$",
            @"^notification_helper\.exe$",
            @"^python\.exe$",
            @"^pythonw\.exe$",
            @"^zsync\.exe$",
            @"^zsyncmake\.exe$"
        };

        private static ILogger logger = LogManager.GetLogger();

        public static bool IsFileScanExcluded(string path)
        {
            return scanFileExclusionMasks.Any(a => Regex.IsMatch(path, a, RegexOptions.IgnoreCase));
        }

        public static void CreateUrlShortcut(string url, string iconPath, string shortcutPath)
        {
            FileSystem.PrepareSaveFile(shortcutPath);
            var content = @"[InternetShortcut]
IconIndex=0";
            if (!iconPath.IsNullOrEmpty())
            {
                content += Environment.NewLine + $"IconFile={iconPath}";
            }

            content += Environment.NewLine + $"URL={url}";
            File.WriteAllText(shortcutPath, content);
        }

        private static List<UninstallProgram> GetUninstallProgsFromView(RegistryView view)
        {
            var rootString = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\";
            void SearchRoot(RegistryHive hive, List<UninstallProgram> programs)
            {
                using (var root = RegistryKey.OpenBaseKey(hive, view))
                {
                    var keyList = root.OpenSubKey(rootString);
                    if (keyList == null)
                    {
                        return;
                    }

                    foreach (var key in keyList.GetSubKeyNames())
                    {
                        try
                        {
                            using (var prog = root.OpenSubKey(rootString + key))
                            {
                                if (prog == null)
                                {
                                    continue;
                                }

                                var program = new UninstallProgram()
                                {
                                    DisplayIcon = prog.GetValue("DisplayIcon")?.ToString(),
                                    DisplayVersion = prog.GetValue("DisplayVersion")?.ToString(),
                                    DisplayName = prog.GetValue("DisplayName")?.ToString(),
                                    InstallLocation = prog.GetValue("InstallLocation")?.ToString(),
                                    Publisher = prog.GetValue("Publisher")?.ToString(),
                                    UninstallString = prog.GetValue("UninstallString")?.ToString(),
                                    URLInfoAbout = prog.GetValue("URLInfoAbout")?.ToString(),
                                    Path = prog.GetValue("Path")?.ToString(),
                                    RegistryKeyName = key
                                };

                                programs.Add(program);
                            }
                        }
                        catch (System.Security.SecurityException e)
                        {
                            logger.Warn(e, $"Failed to read registry key {rootString + key}");
                        }
                    }
                }
            }

            var progs = new List<UninstallProgram>();
            SearchRoot(RegistryHive.LocalMachine, progs);
            SearchRoot(RegistryHive.CurrentUser, progs);
            return progs;
        }

        public static List<UninstallProgram> GetUnistallProgramsList()
        {
            var progs = new List<UninstallProgram>();

            if (Environment.Is64BitOperatingSystem)
            {
                progs.AddRange(GetUninstallProgsFromView(RegistryView.Registry64));
            }

            progs.AddRange(GetUninstallProgsFromView(RegistryView.Registry32));
            return progs;
        }
    }

    public enum FileSystemItem
    {
        File,
        Directory
    }

    public static partial class FileSystem
    {
        private static ILogger logger = LogManager.GetLogger();

        public static void CreateDirectory(string path)
        {
            CreateDirectory(path, false);
        }

        public static void CreateDirectory(string path, bool clean)
        {
            var directory = Paths.FixPathLength(path);
            if (string.IsNullOrEmpty(directory))
            {
                return;
            }

            if (Directory.Exists(directory))
            {
                if (clean)
                {
                    DeleteDirectory(directory, true);
                }
                else
                {
                    return;
                }
            }

            Directory.CreateDirectory(directory);
        }

        public static void PrepareSaveFile(string path)
        {
            path = Paths.FixPathLength(path);
            CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public static bool IsDirectoryEmpty(string path)
        {
            path = Paths.FixPathLength(path);
            if (Directory.Exists(path))
            {
                return !Directory.EnumerateFileSystemEntries(path).Any();
            }
            else
            {
                return true;
            }
        }

        public static void DeleteFile(string path)
        {
            path = Paths.FixPathLength(path);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public static void CreateFile(string path)
        {
            path = Paths.FixPathLength(path);
            FileSystem.PrepareSaveFile(path);
            File.Create(path).Dispose();
        }

        public static void CopyFile(string sourcePath, string targetPath, bool overwrite = true)
        {
            sourcePath = Paths.FixPathLength(sourcePath);
            targetPath = Paths.FixPathLength(targetPath);
            logger.Debug($"Copying file {sourcePath} to {targetPath}");
            PrepareSaveFile(targetPath);
            File.Copy(sourcePath, targetPath, overwrite);
        }

        public static void DeleteDirectory(string path)
        {
            path = Paths.FixPathLength(path, true); // we need to force prefix because otherwise recursive delete will fail if some nested path is too long
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        public static void DeleteDirectory(string path, bool includeReadonly)
        {
            path = Paths.FixPathLength(path);
            if (!Directory.Exists(path))
            {
                return;
            }

            if (includeReadonly)
            {
                foreach (var s in Directory.GetDirectories(path))
                {
                    DeleteDirectory(s, true);
                }

                foreach (var f in Directory.GetFiles(path))
                {
                    var file = Paths.FixPathLength(f);
                    var attr = File.GetAttributes(file);
                    if ((attr & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        File.SetAttributes(file, attr ^ FileAttributes.ReadOnly);
                    }

                    File.Delete(file);
                }

                var dirAttr = File.GetAttributes(path);
                if ((dirAttr & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    File.SetAttributes(path, dirAttr ^ FileAttributes.ReadOnly);
                }

                Directory.Delete(path, false);
            }
            else
            {
                DeleteDirectory(path);
            }
        }

        public static bool CanWriteToFolder(string folder)
        {
            folder = Paths.FixPathLength(folder);
            try
            {
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                using (var stream = File.Create(Path.Combine(folder, Path.GetRandomFileName()), 1, FileOptions.DeleteOnClose))
                {
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string ReadFileAsStringSafe(string path, int retryAttempts = 5)
        {
            path = Paths.FixPathLength(path);
            IOException ioException = null;
            for (int i = 0; i < retryAttempts; i++)
            {
                try
                {
                    return File.ReadAllText(path);
                }
                catch (IOException exc)
                {
                    logger.Debug($"Can't read from file, trying again. {path}");
                    ioException = exc;
                    Task.Delay(500).Wait();
                }
            }

            throw new IOException($"Failed to read {path}", ioException);
        }

        public static byte[] ReadFileAsBytesSafe(string path, int retryAttempts = 5)
        {
            path = Paths.FixPathLength(path);
            IOException ioException = null;
            for (int i = 0; i < retryAttempts; i++)
            {
                try
                {
                    return File.ReadAllBytes(path);
                }
                catch (IOException exc)
                {
                    logger.Debug($"Can't read from file, trying again. {path}");
                    ioException = exc;
                    Task.Delay(500).Wait();
                }
            }

            throw new IOException($"Failed to read {path}", ioException);
        }

        public static Stream CreateWriteFileStreamSafe(string path, int retryAttempts = 5)
        {
            path = Paths.FixPathLength(path);
            IOException ioException = null;
            for (int i = 0; i < retryAttempts; i++)
            {
                try
                {
                    return new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
                }
                catch (IOException exc)
                {
                    logger.Debug($"Can't open write file stream, trying again. {path}");
                    ioException = exc;
                    Task.Delay(500).Wait();
                }
            }

            throw new IOException($"Failed to read {path}", ioException);
        }

        public static Stream OpenReadFileStreamSafe(string path, int retryAttempts = 5)
        {
            path = Paths.FixPathLength(path);
            IOException ioException = null;
            for (int i = 0; i < retryAttempts; i++)
            {
                try
                {
                    return new FileStream(path, FileMode.Open, FileAccess.Read);
                }
                catch (IOException exc)
                {
                    logger.Debug($"Can't open read file stream, trying again. {path}");
                    ioException = exc;
                    Task.Delay(500).Wait();
                }
            }

            throw new IOException($"Failed to read {path}", ioException);
        }

        public static void WriteStringToFile(string path, string content)
        {
            path = Paths.FixPathLength(path);
            PrepareSaveFile(path);
            File.WriteAllText(path, content);
        }

        public static string ReadStringFromFile(string path)
        {
            path = Paths.FixPathLength(path);
            return File.ReadAllText(path);
        }

        public static void WriteStringToFileSafe(string path, string content, int retryAttempts = 5)
        {
            path = Paths.FixPathLength(path);
            IOException ioException = null;
            for (int i = 0; i < retryAttempts; i++)
            {
                try
                {
                    PrepareSaveFile(path);
                    File.WriteAllText(path, content);
                    return;
                }
                catch (IOException exc)
                {
                    logger.Debug($"Can't write to a file, trying again. {path}");
                    ioException = exc;
                    Task.Delay(500).Wait();
                }
            }

            throw new IOException($"Failed to write to {path}", ioException);
        }

        public static void DeleteFileSafe(string path, int retryAttempts = 5)
        {
            if (!File.Exists(path))
            {
                return;
            }

            IOException ioException = null;
            for (int i = 0; i < retryAttempts; i++)
            {
                try
                {
                    File.Delete(path);
                    return;
                }
                catch (IOException exc)
                {
                    logger.Debug($"Can't detele file, trying again. {path}");
                    ioException = exc;
                    Task.Delay(500).Wait();
                }
                catch (UnauthorizedAccessException exc)
                {
                    logger.Error(exc, $"Can't detele file, UnauthorizedAccessException. {path}");
                    return;
                }
            }

            throw new IOException($"Failed to delete {path}", ioException);
        }

        public static long GetFreeSpace(string drivePath)
        {
            var root = Path.GetPathRoot(drivePath);
            var drive = DriveInfo.GetDrives().FirstOrDefault(a => a.RootDirectory.FullName.Equals(root, StringComparison.OrdinalIgnoreCase)); ;
            if (drive != null)
            {
                return drive.AvailableFreeSpace;
            }
            else
            {
                return 0;
            }
        }

        public static long GetFileSize(string path)
        {
            path = Paths.FixPathLength(path);
            return GetFileSize(new FileInfo(path));
        }

        public static long GetFileSize(FileInfo fi)
        {
            return fi.Length;
        }

        public static long GetDirectorySize(string path, bool getSizeOnDisk)
        {
            return GetDirectorySize(new DirectoryInfo(Paths.FixPathLength(path)), getSizeOnDisk);
        }

        private static long GetDirectorySize(DirectoryInfo dirInfo, bool getSizeOnDisk)
        {
            long size = 0;
            try
            {
                foreach (FileInfo fileInfo in dirInfo.GetFiles())
                {
                    size += getSizeOnDisk ? GetFileSizeOnDisk(fileInfo) : GetFileSize(fileInfo);
                }
            }
            catch (DirectoryNotFoundException)
            {
                // Directory not being found here means that directory is a symlink
                // with an invalid target path.
                // TODO Rework with proper symlinks handling with FileSystemInfo.ResolveLinkTarget
                // method after Net runtime upgrade
                return size;
            }

            foreach (DirectoryInfo subdirInfo in dirInfo.GetDirectories())
            {
                if (!IsDirectorySubdirSafeToRecurse(subdirInfo))
                {
                    continue;
                }

                size += GetDirectorySize(subdirInfo.FullName, getSizeOnDisk);
            }

            return size;
        }

        public static long GetFileSizeOnDisk(string path)
        {
            return GetFileSizeOnDisk(new FileInfo(Paths.FixPathLength(path)));
        }

        public static long GetFileSizeOnDisk(FileInfo fileInfo)
        {
            // Method will fail if file is a symlink that has a target
            // that does not exist. To avoid, we can check its lenght before continuing
            if (fileInfo.Length == 0)
            {
                return 0;
            }

            // Method will fail when checking a file that's not valid on Windows,
            // for example files used by Proton containing a colon (:).
            // 'Directory' will be null when encountering such a file.
            if (fileInfo.Directory is null)
            {
                return 0;
            }

            // From https://stackoverflow.com/a/3751135
            int result = Kernel32.GetDiskFreeSpaceW(fileInfo.Directory.Root.FullName, out uint sectorsPerCluster, out uint bytesPerSector, out _, out _);
            if (result == 0)
            {
                throw new System.ComponentModel.Win32Exception();
            }

            uint clusterSize = sectorsPerCluster * bytesPerSector;
            uint losize = Kernel32.GetCompressedFileSizeW(Paths.FixPathLength(fileInfo.FullName), out uint hosize);
            int error = Marshal.GetLastWin32Error();
            if (losize == 0xFFFFFFFF && error != 0)
            {
                throw new System.ComponentModel.Win32Exception(error);
            }

            var size = (long)hosize << 32 | losize;
            return ((size + clusterSize - 1) / clusterSize) * clusterSize;
        }

        private static bool IsDirectorySubdirSafeToRecurse(DirectoryInfo childDirectory)
        {
            // Whitespace characters can cause confusion in methods, causing them to process
            // the parent directory instead and causing an infinite loop
            if (childDirectory.Name.IsNullOrWhiteSpace())
            {
                return false;
            }

            return true;
        }

        public static void CopyDirectory(string sourceDirName, string destDirName, bool copySubDirs = true, bool overwrite = true)
        {
            sourceDirName = Paths.FixPathLength(sourceDirName);
            destDirName = Paths.FixPathLength(destDirName);
            var dir = new DirectoryInfo(sourceDirName);
            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);
            }

            var dirs = dir.GetDirectories();
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            var files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirName, file.Name);
                file.CopyTo(temppath, overwrite);
            }

            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string temppath = Path.Combine(destDirName, subdir.Name);
                    CopyDirectory(subdir.FullName, temppath, copySubDirs);
                }
            }
        }

        public static bool FileExistsOnAnyDrive(string filePath, out string existringPath)
        {
            return PathExistsOnAnyDrive(filePath, path => File.Exists(path), out existringPath);
        }

        public static bool DirectoryExistsOnAnyDrive(string directoryPath, out string existringPath)
        {
            return PathExistsOnAnyDrive(directoryPath, path => Directory.Exists(path), out existringPath);
        }

        private static bool PathExistsOnAnyDrive(string originalPath, Predicate<string> predicate, out string existringPath)
        {
            originalPath = Paths.FixPathLength(originalPath);
            existringPath = null;
            try
            {
                if (predicate(originalPath))
                {
                    existringPath = originalPath;
                    return true;
                }

                if (!Paths.IsFullPath(originalPath))
                {
                    return false;
                }

                var rootPath = Path.GetPathRoot(originalPath);
                var availableDrives = DriveInfo.GetDrives().Where(d => d.IsReady);
                foreach (var drive in availableDrives)
                {
                    var pathWithoutDrive = originalPath.Substring(drive.Name.Length);
                    var newPath = Path.Combine(drive.Name, pathWithoutDrive);
                    if (predicate(newPath))
                    {
                        existringPath = newPath;
                        return true;
                    }
                }
            }
            catch (Exception ex) when (!Debugger.IsAttached)
            {
                logger.Error(ex, $"Error checking if path exists on different drive \"{originalPath}\"");
            }

            return false;
        }

        public static bool DirectoryExists(string path)
        {
            return Directory.Exists(Paths.FixPathLength(path));
        }

        public static bool FileExists(string path)
        {
            return File.Exists(Paths.FixPathLength(path));
        }

        public static DateTime DirectoryGetLastWriteTime(string path)
        {
            return Directory.GetLastWriteTime(Paths.FixPathLength(path));
        }

        public static DateTime FileGetLastWriteTime(string path)
        {
            return File.GetLastWriteTime(Paths.FixPathLength(path));
        }

        public static void ReplaceStringInFile(string path, string oldValue, string newValue, Encoding encoding = null)
        {
            encoding = encoding ?? Encoding.UTF8;
            var fileContent = File.ReadAllText(path, encoding);
            File.WriteAllText(path, fileContent.Replace(oldValue, newValue), encoding);
        }
    }

    public class Paths
    {
        private const string longPathPrefix = @"\\?\";
        private const string longPathUncPrefix = @"\\?\UNC\";
        public static readonly char[] DirectorySeparators = new char[] { '\\', '/' };

        public static string GetFinalPathName(string path)
        {
            var h = Kernel32.CreateFile(path,
                0,
                FileShare.ReadWrite | FileShare.Delete,
                IntPtr.Zero,
                FileMode.Open,
                Fileapi.FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);

            if (path.StartsWith(@"\\"))
            {
                return path;
            }

            if (h == Winuser.INVALID_HANDLE_VALUE)
            {
                throw new Win32Exception();
            }

            try
            {
                var sb = new StringBuilder(1024);
                var res = Kernel32.GetFinalPathNameByHandle(h, sb, 1024, 0);
                if (res == 0)
                {
                    throw new Win32Exception();
                }

                var targetPath = sb.ToString();
                if (targetPath.StartsWith(longPathUncPrefix))
                {
                    return targetPath.Replace(longPathUncPrefix, @"\\");
                }
                else
                {
                    return targetPath.Replace(longPathPrefix, string.Empty);
                }
            }
            finally
            {
                Kernel32.CloseHandle(h);
            }
        }

        public static bool IsValidFilePath(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    return false;
                }

                if (string.IsNullOrEmpty(Path.GetExtension(path)))
                {
                    return false;
                }

                string drive = Path.GetPathRoot(path);
                if (!string.IsNullOrEmpty(drive) && !Directory.Exists(drive))
                {
                    return false;
                }

                return true;
            }
            catch
            {
                // Any of Path methods can throw exception in case that path is some weird string
                return false;
            }
        }

        public static string FixSeparators(string path)
        {
            if (path.IsNullOrWhiteSpace())
            {
                return path;
            }

            char prev = default;
            var sb = new StringBuilder(path.Length);
            for (int i = 0; i < path.Length; i++)
            {
                var current = path[i];
                if (current == Path.AltDirectorySeparatorChar)
                {
                    current = Path.DirectorySeparatorChar;
                }

                if (prev != current || current != Path.DirectorySeparatorChar ||
                    (current == Path.DirectorySeparatorChar && prev != Path.DirectorySeparatorChar))
                {
                    prev = current;
                    sb.Append(current);
                    continue;
                }
            }

            if (path.StartsWith(@"\\"))
            {
                sb.Insert(0, @"\");
            }

            return sb.ToString();
        }

        private static string Normalize(string path)
        {
            var formatted = path;
            try
            {
                formatted = new Uri(path).LocalPath;
            }
            catch
            {
                // this shound't happen
            }

            return Path.GetFullPath(formatted).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        }

        public static bool AreEqual(string path1, string path2)
        {
            if (string.IsNullOrEmpty(path1) && !string.IsNullOrEmpty(path2))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(path1) && string.IsNullOrEmpty(path2))
            {
                return false;
            }

            // Empty string is not valid path, return false even when both are null
            if (string.IsNullOrEmpty(path1) && string.IsNullOrEmpty(path2))
            {
                return false;
            }

            try
            {
                return Normalize(path1) == Normalize(path2);
            }
            catch
            {
                return false;
            }
        }

        public static string GetSafePathName(string filename)
        {
            var path = string.Join(" ", filename.Split(Path.GetInvalidFileNameChars()));
            return Regex.Replace(path, @"\s+", " ").Trim();
        }

        public static bool IsFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            // Don't use Path.IsPathRooted because it fails on paths starting with one backslash.
            return Regex.IsMatch(path, @"^([a-zA-Z]:\\|\\\\)");
        }

        public static string GetCommonDirectory(string[] paths)
        {
            int k = paths[0].Length;
            for (int i = 1; i < paths.Length; i++)
            {
                k = Math.Min(k, paths[i].Length);
                for (int j = 0; j < k; j++)
                {
                    if (paths[i][j] != paths[0][j])
                    {
                        k = j;
                        break;
                    }
                }
            }

            var common = paths[0].Substring(0, k);
            if (common.Length == 0)
            {
                return string.Empty;
            }

            if (common[common.Length - 1] == Path.DirectorySeparatorChar)
            {
                return common;
            }
            else
            {
                return common.Substring(0, common.LastIndexOf(Path.DirectorySeparatorChar) + 1);
            }
        }

        public static bool MathcesFilePattern(string filePath, string pattern)
        {
            if (filePath.IsNullOrEmpty() || pattern.IsNullOrEmpty())
            {
                return false;
            }

            if (pattern.Contains(';'))
            {
                return Shlwapi.PathMatchSpecExW(filePath, pattern, MatchPatternFlags.Multiple) == 0;
            }
            else
            {
                return Shlwapi.PathMatchSpecExW(filePath, pattern, MatchPatternFlags.Normal) == 0;
            }
        }

        public static string FixPathLength(string path, bool forcePrefix = false)
        {
            if (path.IsNullOrWhiteSpace())
            {
                return path;
            }

            // Relative paths don't support long paths
            // https://docs.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation?tabs=cmd
            if (!Paths.IsFullPath(path))
            {
                return path;
            }

            // While the MAX_PATH value is 260 characters, a lower value is used because
            // methods can append "\" and string terminator characters to paths and
            // make them surpass the limit
            if ((path.Length >= 258 || forcePrefix) && !path.StartsWith(longPathPrefix))
            {
                if (path.StartsWith(@"\\"))
                {
                    return longPathUncPrefix + path.Substring(2);
                }
                else
                {
                    return longPathPrefix + path;
                }
            }

            return path;
        }

        public static string TrimLongPathPrefix(string path)
        {
            if (path.IsNullOrWhiteSpace())
            {
                return path;
            }

            if (path.StartsWith(longPathUncPrefix))
            {
                return path.Replace(longPathUncPrefix, @"\\");
            }
            else
            {
                return path.Replace(longPathPrefix, string.Empty);
            }
        }
    }

    public class Kernel32
    {
        private const string dllName = "Kernel32.dll";

        [DllImport(dllName, SetLastError = true, CharSet = CharSet.Auto)]
        public extern static uint GetFinalPathNameByHandle(IntPtr hFile, [MarshalAs(UnmanagedType.LPTStr)] StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

        [DllImport(dllName, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public extern static bool CloseHandle(IntPtr hObject);

        [DllImport(dllName, CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CreateFile(
            [MarshalAs(UnmanagedType.LPTStr)] string filename,
            [MarshalAs(UnmanagedType.U4)] uint access,
            [MarshalAs(UnmanagedType.U4)] FileShare share,
            IntPtr securityAttributes, // optional SECURITY_ATTRIBUTES struct or IntPtr.Zero
            [MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
            [MarshalAs(UnmanagedType.U4)] uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport(dllName, SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

        [DllImport(dllName, SetLastError = true)]
        public static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

        [DllImport(dllName, SetLastError = true)]
        public static extern IntPtr FindResource(IntPtr hModule, string lpName, string lpType);

        [DllImport(dllName, SetLastError = true)]
        public static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

        [DllImport(dllName, SetLastError = true)]
        public static extern bool FreeLibrary(IntPtr hModule);

        [DllImport(dllName, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool EnumResourceNames(IntPtr hModule, IntPtr lpszType, ENUMRESNAMEPROC lpEnumFunc, IntPtr lParam);

        [DllImport(dllName, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, IntPtr lpType);

        [DllImport(dllName, SetLastError = true)]
        public static extern IntPtr LockResource(IntPtr hResData);

        [DllImport(dllName, SetLastError = true)]
        public static extern IntPtr GetCurrentProcess();

        [DllImport(dllName, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

        [DllImport(dllName, SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool CreateProcess(
           string lpApplicationName,
           string lpCommandLine,
           ref SECURITY_ATTRIBUTES lpProcessAttributes,
           ref SECURITY_ATTRIBUTES lpThreadAttributes,
           bool bInheritHandles,
           uint dwCreationFlags,
           IntPtr lpEnvironment,
           string lpCurrentDirectory,
           [In] ref STARTUPINFO lpStartupInfo,
           out PROCESS_INFORMATION lpProcessInformation);

        [DllImport(dllName, SetLastError = true)]
        public static extern IntPtr OpenProcess(ProcessAccessFlags processAccess, bool bInheritHandle, int processId);

        [DllImport(dllName, CharSet = CharSet.Auto)]
        public static extern bool QueryFullProcessImageName([In] IntPtr hProcess, [In] uint dwFlags, [Out] StringBuilder lpExeName, [In, Out] ref uint lpdwSize);

        [DllImport(dllName, SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.U4)]
        public static extern uint GetFileAttributesW(string lpFileName);

        [DllImport(dllName, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern uint GetCompressedFileSizeW(
            [In, MarshalAs(UnmanagedType.LPWStr)] string lpFileName,
            [Out, MarshalAs(UnmanagedType.U4)] out uint lpFileSizeHigh);

        [DllImport(dllName, SetLastError = true, PreserveSig = true, CharSet = CharSet.Unicode)]
        public static extern int GetDiskFreeSpaceW(
            [In, MarshalAs(UnmanagedType.LPWStr)] string lpRootPathName,
            out uint lpSectorsPerCluster,
            out uint lpBytesPerSector,
            out uint lpNumberOfFreeClusters,
            out uint lpTotalNumberOfClusters);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true, CharSet = CharSet.Unicode)]
    public delegate bool ENUMRESNAMEPROC(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct STARTUPINFO
    {
        public Int32 cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public Int32 dwX;
        public Int32 dwY;
        public Int32 dwXSize;
        public Int32 dwYSize;
        public Int32 dwXCountChars;
        public Int32 dwYCountChars;
        public Int32 dwFillAttribute;
        public Int32 dwFlags;
        public Int16 wShowWindow;
        public Int16 cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    public class Fileapi
    {
        public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x2000000;
    }


    public static class Winuser
    {
        public const int GWL_STYLE = -16;
        public const int WS_SYSMENU = 0x80000;
        public const int WM_HOTKEY = 0x0312;

        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public static readonly IntPtr HWND_TOP = new IntPtr(0);
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public const uint WM_QUERYENDSESSION = 0x11;
        public const uint WM_ENDSESSION = 0x16;
        public const uint ENDSESSION_CLOSEAPP = 0x1;

        // http://msdn.microsoft.com/en-us/library/windows/desktop/ms646244(v=vs.85).aspx
        public const uint WM_MOUSEACTIVATE = 0x0021;
        public const uint WM_MOUSEMOVE = 0x0200;
        public const uint WM_LBUTTONDOWN = 0x0201;
        public const uint WM_LBUTTONUP = 0x0202;
        public const uint WM_LBUTTONDBLCLK = 0x0203;
        public const uint WM_RBUTTONDOWN = 0x0204;
        public const uint WM_RBUTTONUP = 0x0205;
        public const uint WM_RBUTTONDBLCLK = 0x0206;
        public const uint WM_MBUTTONDOWN = 0x0207;
        public const uint WM_MBUTTONUP = 0x0208;
        public const uint WM_MBUTTONDBLCLK = 0x0209;

        public const uint MK_CONTROL = 0x0008;    // The CTRL key is down.
        public const uint MK_LBUTTON = 0x0001;    // The left mouse button is down.
        public const uint MK_MBUTTON = 0x0010;    // The middle mouse button is down.
        public const uint MK_RBUTTON = 0x0002;    // The right mouse button is down.
        public const uint MK_SHIFT = 0x0004;    // The SHIFT key is down.

        public const uint MOD_NONE = 0x0000;    //(none)
        public const uint MOD_ALT = 0x0001;     //ALT
        public const uint MOD_CONTROL = 0x0002; //CTRL
        public const uint MOD_SHIFT = 0x0004;   //SHIFT
        public const uint MOD_WIN = 0x0008;     //WINDOWS

        public const uint WM_KEYDOWN = 0x0100;
        public const uint WM_KEYUP = 0x0101;
        public const uint WM_SYSKEYDOWN = 0x0104;
        public const uint WM_SYSKEYUP = 0x0105;

        // http://msdn.microsoft.com/en-us/library/windows/desktop/dd375731(v=vs.85).aspx
        public const uint VK_LBUTTON = 0x01; // Left mouse button
        public const uint VK_RBUTTON = 0x02; // Right mouse button
        public const uint VK_CANCEL = 0x03; // Control-break processing
        public const uint VK_MBUTTON = 0x04; // Middle mouse button (three-button mouse)
        public const uint VK_XBUTTON1 = 0x05; // X1 mouse button
        public const uint VK_XBUTTON2 = 0x06; // X2 mouse button
        public const uint VK_0x07 = 0x07; // Undefined
        public const uint VK_BACK = 0x08; // BACKSPACE key
        public const uint VK_TAB = 0x09; // TAB key
        public const uint VK_0x0A = 0x0A; // Reserved
        public const uint VK_0x0B = 0x0B; // Reserved
        public const uint VK_CLEAR = 0x0C; // CLEAR key
        public const uint VK_RETURN = 0x0D; // ENTER key
        public const uint VK_0x0E = 0x0E; // Undefined
        public const uint VK_0x0F = 0x0F; // Undefined
        public const uint VK_SHIFT = 0x10; // SHIFT key
        public const uint VK_CONTROL = 0x11; // CTRL key
        public const uint VK_MENU = 0x12; // ALT key
        public const uint VK_PAUSE = 0x13; // PAUSE key
        public const uint VK_CAPITAL = 0x14; // CAPS LOCK key
        public const uint VK_KANA = 0x15; // IME Kana mode
        public const uint VK_HANGUEL = 0x15; // IME Hanguel mode (maintained for compatibility; use VK_HANGUL)
        public const uint VK_HANGUL = 0x15; // IME Hangul mode
        public const uint VK_0x16 = 0x16; // Undefined
        public const uint VK_JUNJA = 0x17; // IME Junja mode
        public const uint VK_FINAL = 0x18; // IME final mode
        public const uint VK_HANJA = 0x19; // IME Hanja mode
        public const uint VK_KANJI = 0x19; // IME Kanji mode
        public const uint VK_0x1A = 0x1A; // Undefined
        public const uint VK_ESCAPE = 0x1B; // ESC key
        public const uint VK_CONVERT = 0x1C; // IME convert
        public const uint VK_NONCONVERT = 0x1D; // IME nonconvert
        public const uint VK_ACCEPT = 0x1E; // IME accept
        public const uint VK_MODECHANGE = 0x1F; // IME mode change request
        public const uint VK_SPACE = 0x20; // SPACEBAR
        public const uint VK_PRIOR = 0x21; // PAGE UP key
        public const uint VK_NEXT = 0x22; // PAGE DOWN key
        public const uint VK_END = 0x23; // END key
        public const uint VK_HOME = 0x24; // HOME key
        public const uint VK_LEFT = 0x25; // LEFT ARROW key
        public const uint VK_UP = 0x26; // UP ARROW key
        public const uint VK_RIGHT = 0x27; // RIGHT ARROW key
        public const uint VK_DOWN = 0x28; // DOWN ARROW key
        public const uint VK_SELECT = 0x29; // SELECT key
        public const uint VK_PRINT = 0x2A; // PRINT key
        public const uint VK_EXECUTE = 0x2B; // EXECUTE key
        public const uint VK_SNAPSHOT = 0x2C; // PRINT SCREEN key
        public const uint VK_INSERT = 0x2D; // INS key
        public const uint VK_DELETE = 0x2E; // DEL key
        public const uint VK_HELP = 0x2F; // HELP key
        public const uint VK_0x30 = 0x30; // 0 key
        public const uint VK_0x31 = 0x31; // 1 key
        public const uint VK_0x32 = 0x32; // 2 key
        public const uint VK_0x33 = 0x33; // 3 key
        public const uint VK_0x34 = 0x34; // 4 key
        public const uint VK_0x35 = 0x35; // 5 key
        public const uint VK_0x36 = 0x36; // 6 key
        public const uint VK_0x37 = 0x37; // 7 key
        public const uint VK_0x38 = 0x38; // 8 key
        public const uint VK_0x39 = 0x39; // 9 key
        public const uint VK_0x3A = 0x3A; // Undefined
        public const uint VK_0x3B = 0x3B; // Undefined
        public const uint VK_0x3C = 0x3C; // Undefined
        public const uint VK_0x3D = 0x3D; // Undefined
        public const uint VK_0x3E = 0x3E; // Undefined
        public const uint VK_0x3F = 0x3F; // Undefined
        public const uint VK_0x40 = 0x40; // Undefined
        public const uint VK_0x41 = 0x41; // A key
        public const uint VK_0x42 = 0x42; // B key
        public const uint VK_0x43 = 0x43; // C key
        public const uint VK_0x44 = 0x44; // D key
        public const uint VK_0x45 = 0x45; // E key
        public const uint VK_0x46 = 0x46; // F key
        public const uint VK_0x47 = 0x47; // G key
        public const uint VK_0x48 = 0x48; // H key
        public const uint VK_0x49 = 0x49; // I key
        public const uint VK_0x4A = 0x4A; // J key
        public const uint VK_0x4B = 0x4B; // K key
        public const uint VK_0x4C = 0x4C; // L key
        public const uint VK_0x4D = 0x4D; // M key
        public const uint VK_0x4E = 0x4E; // N key
        public const uint VK_0x4F = 0x4F; // O key
        public const uint VK_0x50 = 0x50; // P key
        public const uint VK_0x51 = 0x51; // Q key
        public const uint VK_0x52 = 0x52; // R key
        public const uint VK_0x53 = 0x53; // S key
        public const uint VK_0x54 = 0x54; // T key
        public const uint VK_0x55 = 0x55; // U key
        public const uint VK_0x56 = 0x56; // V key
        public const uint VK_0x57 = 0x57; // W key
        public const uint VK_0x58 = 0x58; // X key
        public const uint VK_0x59 = 0x59; // Y key
        public const uint VK_0x5A = 0x5A; // Z key
        public const uint VK_LWIN = 0x5B; // Left Windows key (Natural keyboard)
        public const uint VK_RWIN = 0x5C; // Right Windows key (Natural keyboard)
        public const uint VK_APPS = 0x5D; // Applications key (Natural keyboard)
        public const uint VK_0x5E = 0x5E; // Reserved
        public const uint VK_SLEEP = 0x5F; // Computer Sleep key
        public const uint VK_NUMPAD0 = 0x60; // Numeric keypad 0 key
        public const uint VK_NUMPAD1 = 0x61; // Numeric keypad 1 key
        public const uint VK_NUMPAD2 = 0x62; // Numeric keypad 2 key
        public const uint VK_NUMPAD3 = 0x63; // Numeric keypad 3 key
        public const uint VK_NUMPAD4 = 0x64; // Numeric keypad 4 key
        public const uint VK_NUMPAD5 = 0x65; // Numeric keypad 5 key
        public const uint VK_NUMPAD6 = 0x66; // Numeric keypad 6 key
        public const uint VK_NUMPAD7 = 0x67; // Numeric keypad 7 key
        public const uint VK_NUMPAD8 = 0x68; // Numeric keypad 8 key
        public const uint VK_NUMPAD9 = 0x69; // Numeric keypad 9 key
        public const uint VK_MULTIPLY = 0x6A; // Multiply key
        public const uint VK_ADD = 0x6B; // Add key
        public const uint VK_SEPARATOR = 0x6C; // Separator key
        public const uint VK_SUBTRACT = 0x6D; // Subtract key
        public const uint VK_DECIMAL = 0x6E; // Decimal key
        public const uint VK_DIVIDE = 0x6F; // Divide key
        public const uint VK_F1 = 0x70; // F1 key
        public const uint VK_F2 = 0x71; // F2 key
        public const uint VK_F3 = 0x72; // F3 key
        public const uint VK_F4 = 0x73; // F4 key
        public const uint VK_F5 = 0x74; // F5 key
        public const uint VK_F6 = 0x75; // F6 key
        public const uint VK_F7 = 0x76; // F7 key
        public const uint VK_F8 = 0x77; // F8 key
        public const uint VK_F9 = 0x78; // F9 key
        public const uint VK_F10 = 0x79; // F10 key
        public const uint VK_F11 = 0x7A; // F11 key
        public const uint VK_F12 = 0x7B; // F12 key
        public const uint VK_F13 = 0x7C; // F13 key
        public const uint VK_F14 = 0x7D; // F14 key
        public const uint VK_F15 = 0x7E; // F15 key
        public const uint VK_F16 = 0x7F; // F16 key
        public const uint VK_F17 = 0x80; // F17 key
        public const uint VK_F18 = 0x81; // F18 key
        public const uint VK_F19 = 0x82; // F19 key
        public const uint VK_F20 = 0x83; // F20 key
        public const uint VK_F21 = 0x84; // F21 key
        public const uint VK_F22 = 0x85; // F22 key
        public const uint VK_F23 = 0x86; // F23 key
        public const uint VK_F24 = 0x87; // F24 key
        public const uint VK_0x88 = 0x88; // Unassigned
        public const uint VK_0x89 = 0x89; // Unassigned
        public const uint VK_0x8A = 0x8A; // Unassigned
        public const uint VK_0x8B = 0x8B; // Unassigned
        public const uint VK_0x8C = 0x8C; // Unassigned
        public const uint VK_0x8D = 0x8D; // Unassigned
        public const uint VK_0x8E = 0x8E; // Unassigned
        public const uint VK_0x8F = 0x8F; // Unassigned
        public const uint VK_NUMLOCK = 0x90; // NUM LOCK key
        public const uint VK_SCROLL = 0x91; // SCROLL LOCK key
        public const uint VK_0x92 = 0x92; // OEM specific
        public const uint VK_0x93 = 0x93; // OEM specific
        public const uint VK_0x94 = 0x94; // OEM specific
        public const uint VK_0x95 = 0x95; // OEM specific
        public const uint VK_0x96 = 0x96; // OEM specific
        public const uint VK_0x97 = 0x97; // Unassigned
        public const uint VK_0x98 = 0x98; // Unassigned
        public const uint VK_0x99 = 0x99; // Unassigned
        public const uint VK_0x9A = 0x9A; // Unassigned
        public const uint VK_0x9B = 0x9B; // Unassigned
        public const uint VK_0x9C = 0x9C; // Unassigned
        public const uint VK_0x9D = 0x9D; // Unassigned
        public const uint VK_0x9E = 0x9E; // Unassigned
        public const uint VK_0x9F = 0x9F; // Unassigned
        public const uint VK_LSHIFT = 0xA0; // Left SHIFT key
        public const uint VK_RSHIFT = 0xA1; // Right SHIFT key
        public const uint VK_LCONTROL = 0xA2; // Left CONTROL key
        public const uint VK_RCONTROL = 0xA3; // Right CONTROL key
        public const uint VK_LMENU = 0xA4; // Left MENU key
        public const uint VK_RMENU = 0xA5; // Right MENU key
        public const uint VK_BROWSER_BACK = 0xA6; // Browser Back key
        public const uint VK_BROWSER_FORWARD = 0xA7; // Browser Forward key
        public const uint VK_BROWSER_REFRESH = 0xA8; // Browser Refresh key
        public const uint VK_BROWSER_STOP = 0xA9; // Browser Stop key
        public const uint VK_BROWSER_SEARCH = 0xAA; // Browser Search key
        public const uint VK_BROWSER_FAVORITES = 0xAB; // Browser Favorites key
        public const uint VK_BROWSER_HOME = 0xAC; // Browser Start and Home key
        public const uint VK_VOLUME_MUTE = 0xAD; // Volume Mute key
        public const uint VK_VOLUME_DOWN = 0xAE; // Volume Down key
        public const uint VK_VOLUME_UP = 0xAF; // Volume Up key
        public const uint VK_MEDIA_NEXT_TRACK = 0xB0; // Next Track key
        public const uint VK_MEDIA_PREV_TRACK = 0xB1; // Previous Track key
        public const uint VK_MEDIA_STOP = 0xB2; // Stop Media key
        public const uint VK_MEDIA_PLAY_PAUSE = 0xB3; // Play/Pause Media key
        public const uint VK_LAUNCH_MAIL = 0xB4; // Start Mail key
        public const uint VK_LAUNCH_MEDIA_SELECT = 0xB5; // Select Media key
        public const uint VK_LAUNCH_APP1 = 0xB6; // Start Application 1 key
        public const uint VK_LAUNCH_APP2 = 0xB7; // Start Application 2 key
        public const uint VK_0xB8 = 0xB8; // Reserved
        public const uint VK_0xB9 = 0xB9; // Reserved
        public const uint VK_OEM_1 = 0xBA; // Used for miscellaneous characters; it can vary by keyboard. For the US standard keyboard, the ';:' key
        public const uint VK_OEM_PLUS = 0xBB; // For any country/region, the '+' key
        public const uint VK_OEM_COMMA = 0xBC; // For any country/region, the ',' key
        public const uint VK_OEM_MINUS = 0xBD; // For any country/region, the '-' key
        public const uint VK_OEM_PERIOD = 0xBE; // For any country/region, the '.' key
        public const uint VK_OEM_2 = 0xBF; // Used for miscellaneous characters; it can vary by keyboard.
        public const uint VK_OEM_3 = 0xC0; // Used for miscellaneous characters; it can vary by keyboard.
        public const uint VK_0xC1 = 0xC1; // Reserved
        public const uint VK_0xC2 = 0xC2; // Reserved
        public const uint VK_0xC3 = 0xC3; // Reserved
        public const uint VK_0xC4 = 0xC4; // Reserved
        public const uint VK_0xC5 = 0xC5; // Reserved
        public const uint VK_0xC6 = 0xC6; // Reserved
        public const uint VK_0xC7 = 0xC7; // Reserved
        public const uint VK_0xC8 = 0xC8; // Reserved
        public const uint VK_0xC9 = 0xC9; // Reserved
        public const uint VK_0xCA = 0xCA; // Reserved
        public const uint VK_0xCB = 0xCB; // Reserved
        public const uint VK_0xCC = 0xCC; // Reserved
        public const uint VK_0xCD = 0xCD; // Reserved
        public const uint VK_0xCE = 0xCE; // Reserved
        public const uint VK_0xCF = 0xCF; // Reserved
        public const uint VK_0xD0 = 0xD0; // Reserved
        public const uint VK_0xD1 = 0xD1; // Reserved
        public const uint VK_0xD2 = 0xD2; // Reserved
        public const uint VK_0xD3 = 0xD3; // Reserved
        public const uint VK_0xD4 = 0xD4; // Reserved
        public const uint VK_0xD5 = 0xD5; // Reserved
        public const uint VK_0xD6 = 0xD6; // Reserved
        public const uint VK_0xD7 = 0xD7; // Reserved
        public const uint VK_0xD8 = 0xD8; // Unassigned
        public const uint VK_0xD9 = 0xD9; // Unassigned
        public const uint VK_0xDA = 0xDA; // Unassigned
        public const uint VK_OEM_4 = 0xDB; // Used for miscellaneous characters; it can vary by keyboard.
        public const uint VK_OEM_5 = 0xDC; // Used for miscellaneous characters; it can vary by keyboard.
        public const uint VK_OEM_6 = 0xDD; // Used for miscellaneous characters; it can vary by keyboard.
        public const uint VK_OEM_7 = 0xDE; // Used for miscellaneous characters; it can vary by keyboard.
        public const uint VK_OEM_8 = 0xDF; // Used for miscellaneous characters; it can vary by keyboard.
        public const uint VK_0xE0 = 0xE0; // Reserved
        public const uint VK_0xE1 = 0xE1; // OEM specific
        public const uint VK_OEM_102 = 0xE2; // Either the angle bracket key or the backslash key on the RT 102-key keyboard
        public const uint VK_0xE3 = 0xE3; // OEM specific
        public const uint VK_0xE4 = 0xE4; // OEM specific
        public const uint VK_PROCESSKEY = 0xE5; // IME PROCESS key
        public const uint VK_0xE6 = 0xE6; // OEM specific
        public const uint VK_PACKET = 0xE7; // Used to pass Unicode characters as if they were keystrokes. The VK_PACKET key is the low word of a 32-bit Virtual Key value used for non-keyboard input methods. For more information, see Remark in KEYBDINPUT,SendInput, WM_KEYDOWN, and WM_KEYUP
        public const uint VK_0xE8 = 0xE8; // Unassigned
        public const uint VK_0xE9 = 0xE9; // OEM specific
        public const uint VK_0xEA = 0xEA; // OEM specific
        public const uint VK_0xEB = 0xEB; // OEM specific
        public const uint VK_0xEC = 0xEC; // OEM specific
        public const uint VK_0xED = 0xED; // OEM specific
        public const uint VK_0xEE = 0xEE; // OEM specific
        public const uint VK_0xEF = 0xEF; // OEM specific
        public const uint VK_0xF0 = 0xF0; // OEM specific
        public const uint VK_0xF1 = 0xF1; // OEM specific
        public const uint VK_0xF2 = 0xF2; // OEM specific
        public const uint VK_0xF3 = 0xF3; // OEM specific
        public const uint VK_0xF4 = 0xF4; // OEM specific
        public const uint VK_0xF5 = 0xF5; // OEM specific
        public const uint VK_ATTN = 0xF6; // Attn key
        public const uint VK_CRSEL = 0xF7; // CrSel key
        public const uint VK_EXSEL = 0xF8; // ExSel key
        public const uint VK_EREOF = 0xF9; // Erase EOF key
        public const uint VK_PLAY = 0xFA; // Play key
        public const uint VK_ZOOM = 0xFB; // Zoom key
        public const uint VK_NONAME = 0xFC; // Reserved
        public const uint VK_PA1 = 0xFD; // PA1 key
        public const uint VK_OEM_CLEAR = 0xFE; // Clear key
    }

    public enum QUERY_DEVICE_CONFIG_FLAGS : uint
    {
        QDC_ALL_PATHS = 0x00000001,
        QDC_ONLY_ACTIVE_PATHS = 0x00000002,
        QDC_DATABASE_CURRENT = 0x00000004
    }

    [Flags]
    public enum SWP
    {
        ASYNCWINDOWPOS = 0x4000,
        DEFERERASE = 0x2000,
        DRAWFRAME = 0x0020,
        FRAMECHANGED = 0x0020,
        HIDEWINDOW = 0x0080,
        NOACTIVATE = 0x0010,
        NOCOPYBITS = 0x0100,
        NOMOVE = 0x0002,
        NOOWNERZORDER = 0x0200,
        NOREDRAW = 0x0008,
        NOREPOSITION = 0x0200,
        NOSENDCHANGING = 0x0400,
        NOSIZE = 0x0001,
        NOZORDER = 0x0004,
        SHOWWINDOW = 0x0040,
        TOPMOST = NOACTIVATE | NOOWNERZORDER | NOSIZE | NOMOVE | NOREDRAW | NOSENDCHANGING
    }

    public enum MonitorOptions : uint
    {
        MONITOR_DEFAULTTONULL = 0x00000000,
        MONITOR_DEFAULTTOPRIMARY = 0x00000001,
        MONITOR_DEFAULTTONEAREST = 0x00000002
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class MONITORINFO
    {
        public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        public RECT rcMonitor = new RECT();
        public RECT rcWork = new RECT();
        public int dwFlags = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    public static class Windef
    {
        internal static int LOWORD(int i)
        {
            return (short)(i & 0xFFFF);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        private int _x;
        private int _y;

        public POINT(int x, int y)
        {
            _x = x;
            _y = y;
        }

        public int X
        {
            get { return _x; }
            set { _x = value; }
        }

        public int Y
        {
            get { return _y; }
            set { _y = value; }
        }

        public override bool Equals(object obj)
        {
            if (obj is POINT)
            {
                var point = (POINT)obj;

                return point._x == _x && point._y == _y;
            }
            return base.Equals(obj);
        }
        public override int GetHashCode()
        {
            return _x.GetHashCode() ^ _y.GetHashCode();
        }

        public static bool operator ==(POINT a, POINT b)
        {
            return a._x == b._x && a._y == b._y;
        }

        public static bool operator !=(POINT a, POINT b)
        {
            return !(a == b);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct RECT
    {
        private int _left;
        private int _top;
        private int _right;
        private int _bottom;

        public static readonly RECT Empty = new RECT();

        public RECT(int left, int top, int right, int bottom)
        {
            this._left = left;
            this._top = top;
            this._right = right;
            this._bottom = bottom;
        }

        public RECT(RECT rcSrc)
        {
            _left = rcSrc.Left;
            _top = rcSrc.Top;
            _right = rcSrc.Right;
            _bottom = rcSrc.Bottom;
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public void Offset(int dx, int dy)
        {
            _left += dx;
            _top += dy;
            _right += dx;
            _bottom += dy;
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public int Left
        {
            get { return _left; }
            set { _left = value; }
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public int Right
        {
            get { return _right; }
            set { _right = value; }
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public int Top
        {
            get { return _top; }
            set { _top = value; }
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public int Bottom
        {
            get { return _bottom; }
            set { _bottom = value; }
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public int Width
        {
            get { return _right - _left; }
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public int Height
        {
            get { return _bottom - _top; }
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public POINT Position
        {
            get { return new POINT { X = _left, Y = _top }; }
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public SIZE Size
        {
            get { return new SIZE { cx = Width, cy = Height }; }
        }

        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        public static RECT Union(RECT rect1, RECT rect2)
        {
            return new RECT
            {
                Left = Math.Min(rect1.Left, rect2.Left),
                Top = Math.Min(rect1.Top, rect2.Top),
                Right = Math.Max(rect1.Right, rect2.Right),
                Bottom = Math.Max(rect1.Bottom, rect2.Bottom),
            };
        }

        public override bool Equals(object obj)
        {
            try
            {
                var rc = (RECT)obj;
                return rc._bottom == _bottom
                    && rc._left == _left
                    && rc._right == _right
                    && rc._top == _top;
            }
            catch (InvalidCastException)
            {
                return false;
            }
        }

        public bool IsEmpty
        {
            get
            {
                // BUGBUG : On Bidi OS (hebrew arabic) left > right
                return Left >= Right || Top >= Bottom;
            }
        }

        public override string ToString()
        {
            if (this == Empty)
                return "RECT {Empty}";
            return "RECT { left : " + Left + " / top : " + Top + " / right : " + Right + " / bottom : " + Bottom + " }";
        }

        public override int GetHashCode()
        {
            return (_left << 16 | Windef.LOWORD(_right)) ^ (_top << 16 | Windef.LOWORD(_bottom));
        }

        public static bool operator ==(RECT rect1, RECT rect2)
        {
            return (rect1.Left == rect2.Left && rect1.Top == rect2.Top && rect1.Right == rect2.Right && rect1.Bottom == rect2.Bottom);
        }

        public static bool operator !=(RECT rect1, RECT rect2)
        {
            return !(rect1 == rect2);
        }
    }

    [Flags]
    public enum MatchPatternFlags : uint
    {
        Normal = 0x00000000,            // PMSF_NORMAL
        Multiple = 0x00000001,          // PMSF_MULTIPLE
        DontStripSpaces = 0x00010000    // PMSF_DONT_STRIP_SPACES
    }

    public class Shlwapi
    {
        private const string dllName = "Shlwapi.dll";

        [DllImport(dllName, BestFitMapping = false, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = false, ThrowOnUnmappableChar = true)]
        public static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

        [DllImport(dllName, SetLastError = false)]
        public static extern int PathMatchSpecExW([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.LPWStr)] string spec, MatchPatternFlags flags);
    }

    [Flags]
    public enum ProcessAccessFlags : uint
    {
        All = 0x001F0FFF,
        Terminate = 0x00000001,
        CreateThread = 0x00000002,
        VirtualMemoryOperation = 0x00000008,
        VirtualMemoryRead = 0x00000010,
        VirtualMemoryWrite = 0x00000020,
        DuplicateHandle = 0x00000040,
        CreateProcess = 0x000000080,
        SetQuota = 0x00000100,
        SetInformation = 0x00000200,
        QueryInformation = 0x00000400,
        QueryLimitedInformation = 0x00001000,
        Synchronize = 0x00100000
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public UIntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;

        public int Size
        {
            get { return (int)Marshal.SizeOf(typeof(PROCESS_BASIC_INFORMATION)); }
        }
    }

    public enum PROCESSINFOCLASS : int
    {
        ProcessBasicInformation = 0, // 0, q: PROCESS_BASIC_INFORMATION, PROCESS_EXTENDED_BASIC_INFORMATION
        ProcessQuotaLimits, // qs: QUOTA_LIMITS, QUOTA_LIMITS_EX
        ProcessIoCounters, // q: IO_COUNTERS
        ProcessVmCounters, // q: VM_COUNTERS, VM_COUNTERS_EX
        ProcessTimes, // q: KERNEL_USER_TIMES
        ProcessBasePriority, // s: KPRIORITY
        ProcessRaisePriority, // s: ULONG
        ProcessDebugPort, // q: HANDLE
        ProcessExceptionPort, // s: HANDLE
        ProcessAccessToken, // s: PROCESS_ACCESS_TOKEN
        ProcessLdtInformation, // 10
        ProcessLdtSize,
        ProcessDefaultHardErrorMode, // qs: ULONG
        ProcessIoPortHandlers, // (kernel-mode only)
        ProcessPooledUsageAndLimits, // q: POOLED_USAGE_AND_LIMITS
        ProcessWorkingSetWatch, // q: PROCESS_WS_WATCH_INFORMATION[]; s: void
        ProcessUserModeIOPL,
        ProcessEnableAlignmentFaultFixup, // s: BOOLEAN
        ProcessPriorityClass, // qs: PROCESS_PRIORITY_CLASS
        ProcessWx86Information,
        ProcessHandleCount, // 20, q: ULONG, PROCESS_HANDLE_INFORMATION
        ProcessAffinityMask, // s: KAFFINITY
        ProcessPriorityBoost, // qs: ULONG
        ProcessDeviceMap, // qs: PROCESS_DEVICEMAP_INFORMATION, PROCESS_DEVICEMAP_INFORMATION_EX
        ProcessSessionInformation, // q: PROCESS_SESSION_INFORMATION
        ProcessForegroundInformation, // s: PROCESS_FOREGROUND_BACKGROUND
        ProcessWow64Information, // q: ULONG_PTR
        ProcessImageFileName, // q: UNICODE_STRING
        ProcessLUIDDeviceMapsEnabled, // q: ULONG
        ProcessBreakOnTermination, // qs: ULONG
        ProcessDebugObjectHandle, // 30, q: HANDLE
        ProcessDebugFlags, // qs: ULONG
        ProcessHandleTracing, // q: PROCESS_HANDLE_TRACING_QUERY; s: size 0 disables, otherwise enables
        ProcessIoPriority, // qs: ULONG
        ProcessExecuteFlags, // qs: ULONG
        ProcessResourceManagement,
        ProcessCookie, // q: ULONG
        ProcessImageInformation, // q: SECTION_IMAGE_INFORMATION
        ProcessCycleTime, // q: PROCESS_CYCLE_TIME_INFORMATION
        ProcessPagePriority, // q: ULONG
        ProcessInstrumentationCallback, // 40
        ProcessThreadStackAllocation, // s: PROCESS_STACK_ALLOCATION_INFORMATION, PROCESS_STACK_ALLOCATION_INFORMATION_EX
        ProcessWorkingSetWatchEx, // q: PROCESS_WS_WATCH_INFORMATION_EX[]
        ProcessImageFileNameWin32, // q: UNICODE_STRING
        ProcessImageFileMapping, // q: HANDLE (input)
        ProcessAffinityUpdateMode, // qs: PROCESS_AFFINITY_UPDATE_MODE
        ProcessMemoryAllocationMode, // qs: PROCESS_MEMORY_ALLOCATION_MODE
        ProcessGroupInformation, // q: USHORT[]
        ProcessTokenVirtualizationEnabled, // s: ULONG
        ProcessConsoleHostProcess, // q: ULONG_PTR
        ProcessWindowInformation, // 50, q: PROCESS_WINDOW_INFORMATION
        ProcessHandleInformation, // q: PROCESS_HANDLE_SNAPSHOT_INFORMATION // since WIN8
        ProcessMitigationPolicy, // s: PROCESS_MITIGATION_POLICY_INFORMATION
        ProcessDynamicFunctionTableInformation,
        ProcessHandleCheckingMode,
        ProcessKeepAliveCount, // q: PROCESS_KEEPALIVE_COUNT_INFORMATION
        ProcessRevokeFileHandles, // s: PROCESS_REVOKE_FILE_HANDLES_INFORMATION
        MaxProcessInfoClass
    };

    public class Ntdll
    {
        private const string dllName = "Ntdll.dll";

        [DllImport(dllName, SetLastError = true)]
        public static extern int NtQueryInformationProcess(IntPtr hProcess, PROCESSINFOCLASS pic, ref PROCESS_BASIC_INFORMATION pbi, int cb, out int pSize);
    }

    public static class CmdLineTools
    {
        public const string TaskKill = "taskkill";
        public const string Cmd = "cmd";
        public const string IPConfig = "ipconfig";
    }

    public static class ProcessStarter
    {
        private static ILogger logger = LogManager.GetLogger();

        public static int ShellExecute(string cmdLine)
        {
            logger.Debug($"Executing shell command: {cmdLine}");
            var startInfo = new STARTUPINFO();
            var procInfo = new PROCESS_INFORMATION();
            var procAtt = new SECURITY_ATTRIBUTES();
            var threadAtt = new SECURITY_ATTRIBUTES();
            procAtt.nLength = Marshal.SizeOf(procAtt);
            threadAtt.nLength = Marshal.SizeOf(threadAtt);

            try
            {
                if (Kernel32.CreateProcess(
                    null,
                    cmdLine,
                    ref procAtt,
                    ref threadAtt,
                    false,
                    0x0020,
                    IntPtr.Zero,
                    null,
                    ref startInfo,
                    out procInfo))
                {
                    return procInfo.dwProcessId;
                }
                else
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                if (procInfo.hProcess != IntPtr.Zero)
                {
                    Kernel32.CloseHandle(procInfo.hProcess);
                }

                if (procInfo.hThread != IntPtr.Zero)
                {
                    Kernel32.CloseHandle(procInfo.hThread);
                }
            }
        }

        public static Process StartUrl(string url)
        {
            logger.Debug($"Opening URL: {url}");
            try
            {
                return Process.Start(url);
            }
            catch (Exception e)
            {
                // There are some crash report with 0x80004005 error when opening standard URL.
                logger.Error(e, "Failed to open URL.");
                return Process.Start(CmdLineTools.Cmd, $"/C start {url}");
            }
        }

        public static Process StartProcess(string path, bool asAdmin = false)
        {
            return StartProcess(path, string.Empty, string.Empty, asAdmin);
        }

        public static Process StartProcess(string path, string arguments, bool asAdmin = false)
        {
            return StartProcess(path, arguments, string.Empty, asAdmin);
        }

        public static Process StartProcess(string path, string arguments, string workDir, bool asAdmin = false)
        {
            logger.Debug($"Starting process: {path}, {arguments}, {workDir}, {asAdmin}");
            if (path.IsNullOrWhiteSpace())
            {
                throw new ArgumentNullException("Cannot start process, executable path is specified.");
            }

            var startupPath = path;
            if (path.Contains(".."))
            {
                startupPath = Path.GetFullPath(path);
            }

            var info = new ProcessStartInfo(startupPath)
            {
                Arguments = arguments,
                WorkingDirectory = string.IsNullOrEmpty(workDir) ? (new FileInfo(startupPath)).Directory.FullName : workDir
            };

            if (asAdmin)
            {
                info.Verb = "runas";
            }

            return Process.Start(info);
        }

        public static int StartProcessWait(string path, string arguments, string workDir, bool noWindow = false)
        {
            logger.Debug($"Starting process: {path}, {arguments}, {workDir}");
            if (path.IsNullOrWhiteSpace())
            {
                throw new ArgumentNullException("Cannot start process, executable path is specified.");
            }

            var startupPath = path;
            if (path.Contains(".."))
            {
                startupPath = Path.GetFullPath(path);
            }

            var info = new ProcessStartInfo(startupPath)
            {
                Arguments = arguments,
                WorkingDirectory = string.IsNullOrEmpty(workDir) ? (new FileInfo(startupPath)).Directory.FullName : workDir
            };

            if (noWindow)
            {
                info.CreateNoWindow = true;
                info.UseShellExecute = false;
            }

            using (var proc = Process.Start(info))
            {
                proc.WaitForExit();
                return proc.ExitCode;
            }
        }

        public static int StartProcessWait(
            string path,
            string arguments,
            string workDir,
            out string stdOutput,
            out string stdError)
        {
            logger.Debug($"Starting process: {path}, {arguments}, {workDir}");
            if (path.IsNullOrWhiteSpace())
            {
                throw new ArgumentNullException("Cannot start process, executable path is specified.");
            }

            var startupPath = path;
            if (path.Contains(".."))
            {
                startupPath = Path.GetFullPath(path);
            }

            var info = new ProcessStartInfo(startupPath)
            {
                Arguments = arguments,
                WorkingDirectory = string.IsNullOrEmpty(workDir) ? (new FileInfo(startupPath)).Directory.FullName : workDir,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                UseShellExecute = false
            };

            var stdout = string.Empty;
            var stderr = string.Empty;
            using (var proc = new Process())
            {
                proc.StartInfo = info;
                proc.OutputDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        stdout += e.Data + Environment.NewLine;
                    }
                };

                proc.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data != null)
                    {
                        stderr += e.Data + Environment.NewLine;
                    }
                };

                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                proc.WaitForExit();
                stdOutput = stdout;
                stdError = stderr;
                return proc.ExitCode;
            }
        }
    }
}
