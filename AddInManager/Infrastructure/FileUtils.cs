using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

using AddInManager.Wpf.Dialogs;

namespace AddInManager
{
    public static class FileUtils
    {
        public static DateTime GetModifyTime(string filePath)
        {
            return File.GetLastWriteTime(filePath);
        }

        public static string CreateTempFolder(string prefix)
        {
            var tempPath = Path.GetTempPath();
            var directoryInfo = new DirectoryInfo(Path.Combine(tempPath, "RevitAddins"));
            if (!directoryInfo.Exists)
            {
                directoryInfo.Create();
            }
            // 不在此处清理旧目录：同一 Revit 会话中首次加载的程序集会被 CLR 缓存并锁定
            // 其 DLL 无法删除，但资源文件会被删除，导致插件运行时找不到文件。
            // 清理工作已移至 App.OnStartup，在 Revit 启动时（无锁定文件）执行一次。
            var text = $"{DateTime.Now:yyyyMMdd_HHmmss_ffff}";
            var text2 = Path.Combine(directoryInfo.FullName, prefix + text);
            var directoryInfo3 = new DirectoryInfo(text2);
            directoryInfo3.Create();
            return directoryInfo3.FullName;
        }

        /// <summary>
        /// 清理 RevitAddins 临时目录下所有残留的子目录。
        /// 应在 Revit 启动时调用（此时没有 DLL 被 CLR 锁定），不应在命令执行期间调用。
        /// </summary>
        public static void CleanupTempFolders()
        {
            var revitAddinsPath = Path.Combine(Path.GetTempPath(), "RevitAddins");
            var directoryInfo = new DirectoryInfo(revitAddinsPath);
            if (!directoryInfo.Exists) return;

            foreach (var dir in directoryInfo.GetDirectories())
            {
                try
                {
                    Directory.Delete(dir.FullName, true);
                }
                catch (Exception)
                {
                }
            }
        }

        public static void SetWriteable(string fileName)
        {
            if (File.Exists(fileName))
            {
                var fileAttributes = File.GetAttributes(fileName) & ~FileAttributes.ReadOnly;
                File.SetAttributes(fileName, fileAttributes);
            }
        }

        public static bool SameFile(string file1, string file2)
        {
            return 0 == string.Compare(file1.Trim(), file2.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static bool CreateFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                return true;
            }
            try
            {
                var directoryName = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directoryName))
                {
                    Directory.CreateDirectory(directoryName);
                }
                using (new FileInfo(filePath).Create())
                {
                    SetWriteable(filePath);
                }
            }
            catch (Exception)
            {
                return false;
            }
            return File.Exists(filePath);
        }

        public static void DeleteFile(string fileName)
        {
            if (File.Exists(fileName))
            {
                var fileAttributes = File.GetAttributes(fileName) & ~FileAttributes.ReadOnly;
                File.SetAttributes(fileName, fileAttributes);
                try
                {
                    File.Delete(fileName);
                }
                catch (Exception)
                {
                }
            }
        }

        public static bool FileExistsInFolder(string filePath, string destFolder)
        {
            var text = Path.Combine(destFolder, Path.GetFileName(filePath));
            return File.Exists(text);
        }

        public static string CopyFileToFolder(string sourceFilePath, string destFolder, bool onlyCopyRelated, List<FileInfo> allCopiedFiles)
        {
            if (!File.Exists(sourceFilePath))
            {
                return null;
            }
            var directoryName = Path.GetDirectoryName(sourceFilePath);
            if (onlyCopyRelated)
            {
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourceFilePath);
                var text = $"{fileNameWithoutExtension}.*";
                var files = Directory.GetFiles(directoryName, text, SearchOption.TopDirectoryOnly);
                foreach (var text2 in files)
                {
                    var fileName = Path.GetFileName(text2);
                    var text3 = Path.Combine(destFolder, fileName);
                    var flag = CopyFile(text2, text3);
                    if (flag)
                    {
                        var fileInfo = new FileInfo(text3);
                        allCopiedFiles.Add(fileInfo);
                    }
                }
            }
            else
            {
                var folderSize = GetFolderSize(directoryName);
                if (folderSize > 50L)
                {
                    var result = FolderTooBigDialog.Show(directoryName, folderSize);
                    switch (result)
                    {
                        case MessageBoxResult.Yes:
                            CopyDirectory(directoryName, destFolder, allCopiedFiles);
                            break;
                        case MessageBoxResult.No:
                            CopyFileToFolder(sourceFilePath, destFolder, true, allCopiedFiles);
                            break;
                        default:
                            return null;
                    }
                }
                else
                {
                    CopyDirectory(directoryName, destFolder, allCopiedFiles);
                }
            }
            var text4 = Path.Combine(destFolder, Path.GetFileName(sourceFilePath));
            if (File.Exists(text4))
            {
                return text4;
            }
            return null;
        }

        public static bool CopyFile(string sourceFilename, string destinationFilename)
        {
            if (!File.Exists(sourceFilename))
            {
                return false;
            }
            var fileAttributes = File.GetAttributes(sourceFilename) & ~FileAttributes.ReadOnly;
            File.SetAttributes(sourceFilename, fileAttributes);
            if (File.Exists(destinationFilename))
            {
                var fileAttributes2 = File.GetAttributes(destinationFilename) & ~FileAttributes.ReadOnly;
                File.SetAttributes(destinationFilename, fileAttributes2);
                File.Delete(destinationFilename);
            }
            try
            {
                if (!Directory.Exists(Path.GetDirectoryName(destinationFilename)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationFilename));
                }
                File.Copy(sourceFilename, destinationFilename, true);
            }
            catch (Exception)
            {
                return false;
            }
            return File.Exists(destinationFilename);
        }

        public static void CopyDirectory(string sourceDir, string desDir, List<FileInfo> allCopiedFiles)
        {
            try
            {
                var directories = Directory.GetDirectories(sourceDir, "*.*", SearchOption.AllDirectories);
                foreach (var text in directories)
                {
                    var text2 = text.Replace(sourceDir, string.Empty);
                    var text3 = desDir + text2;
                    if (!Directory.Exists(text3))
                    {
                        Directory.CreateDirectory(text3);
                    }
                }
                var files = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);
                foreach (var text4 in files)
                {
                    var text5 = text4.Replace(sourceDir, string.Empty);
                    var text6 = desDir + text5;
                    if (!Directory.Exists(Path.GetDirectoryName(text6)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(text6));
                    }
                    if (CopyFile(text4, text6))
                    {
                        allCopiedFiles.Add(new FileInfo(text6));
                    }
                }
            }
            catch (Exception)
            {
                // ignored
            }
        }

        public static long GetFolderSize(string folderPath)
        {
            long num = 0L;
            try
            {
                foreach (var file in Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories))
                {
                    num += new FileInfo(file).Length;
                }
            }
            catch (Exception)
            {
            }
            return num / 1024L / 1024L;
        }
    }
}
