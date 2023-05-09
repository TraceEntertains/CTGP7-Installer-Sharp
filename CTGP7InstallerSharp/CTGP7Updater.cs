﻿using System.Text;
using System.Net;
using File = System.IO.File;
using System;
using System.Net.Http;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace CTGP7InstallerSharp
{
    /*    import os
    import urllib3
    import shutil
    import psutil
    import struct
        from typing import List

    urlmgr = urllib3.PoolManager(headers ={"connection":"keep-alive"})
    def urlopen(url, ** kwarg):
        out = urlmgr.request("GET", url, chunked = True, preload_content = False, **kwarg)
        if out.status != 200: raise Exception("Received staus code {}".format(out.status))
        return out*/

    class CTGP7Updater
    {
        public static string MAIN_VERSION_NUMBER = "1.1.2";
        public static string SHARP_VERSION_NUMBER = "0.0.1";

        public static string _BASE_URL_DYN_LINK = "https://ctgp7.page.link/baseCDNURL";
        public static string _INSTALLER_VERSION = "installerver";
        public static string _INSTALLER_FILE_DIFF = "installinfo.txt";
        public static string _UPDATER_CHGLOG_FILE = "changeloglist";
        public static string _UPDATER_FILE_URL = "fileListPrefix.txt";
        public static string _FILES_LOCATION = "data";
        public static string _FILES_LOCATION_CITRA = "dataCitra";
        public static string _LATEST_VER_LOCATION = "latestver";
        public static int _DL_ATTEMPT_TOTALCNT = 30;
        public static string _VERSION_FILE_PATH = Path.Combine("config", "version.bin");
        public static string _PENDINGUPDATE_PATH = Path.Combine("config", "pendingUpdate.bin");
        public static string _ISCITRAFLAG_PATH = Path.Combine("config", "citra.flag");
        public static string _REINSTALLFLAG_PATH = Path.Combine("config", "forceInstall.flag");
        public static int _SLACK_FREE_SPACE = 20000000;

        public static HttpClient client = new HttpClient();

        public static Func<bool> isStoppedCallback;
        public static Action<string, double> fileProgressCallback;

        public bool IsInstaller { get; set; }
        public string BasePath { get; set; }
        public int DownloadCount { get; set; }
        public int CurrDownloadCount { get; set; }
        public long FileDownCurr { get; set; }
        public long FileDownSize { get; set; }
        public List<FileListEntry> FileList { get; set; }
        public string LatestVersion { get; set; }
        public Action<(char, object)> LogFunction { get; set; }
        public bool IsStopped { get; set; }
        public long DownloadSize { get; set; }
        public int CurrentUpdateIndex { get; set; }
        public bool IsCitra { get; set; }
        public string BaseURL { get; set; }

        public CTGP7Updater(bool isInstaller = true, bool isCitra = false)
        {
            IsInstaller = isInstaller;
            BasePath = string.Empty;
            DownloadCount = 0;
            CurrDownloadCount = 0;
            FileDownCurr = 0;
            FileDownSize = 0;
            FileList = new List<FileListEntry>();
            LatestVersion = string.Empty;
            LogFunction = null;
            IsStopped = false;
            DownloadSize = 0;
            CurrentUpdateIndex = 0;
            IsCitra = isCitra;

            isStoppedCallback = IsStoppedCallback;
            fileProgressCallback = LogFileProgressCallback;
        }


        public static string GetCitraDir()
        {
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Citra", "sdmc");
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), ".local", "share", "citra-emu", "sdmc");
            }
            else
            {
                return "./sdmc";
            }
        }

        public static void FileDelete(string file)
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal); // Ensure the file is not read-only
                File.Delete(file);
            }
            catch { }
        }

        public static void FileMove(string oldf, string newf)
        {
            FileDelete(newf);
            File.Move(oldf, newf);
        }

        public static string GetDefaultCdnUrlAsString()
        {
            return DownloadString(_BASE_URL_DYN_LINK).Trim();
        }


        public void FetchDefaultCDNURL()
        {
            try
            {
                BaseURL = DownloadString(_BASE_URL_DYN_LINK).Trim();
            }
            catch (Exception e)
            {
                throw new Exception("Failed to init updater: " + e.Message);
            }
        }

        public static void MkFoldersForFile(string fol)
        {
            string g = fol.Substring(0, fol.LastIndexOf(Path.DirectorySeparatorChar));
            Directory.CreateDirectory(g);
        }

        private string BuildFilePath(string path)
        {
            return Path.Combine(Path.Combine(BasePath, "CTGP-7"), path.Replace("/", Path.DirectorySeparatorChar.ToString()).Substring(1));
        }

        private string BuildFileURL(string path, bool isCitra)
        {
            return BaseURL + (isCitra ? _FILES_LOCATION_CITRA : _FILES_LOCATION) + path;
        }

        private List<FileListEntry> ParseAndSortDlList(List<(char, string)> downloadList)
        {
            List<string> allFilePaths = new List<string>();
            List<char> allFileModes = new List<char>();
            List<FileListEntry> ret = new List<FileListEntry>();

            for (int i = 0; i < downloadList.Count; i++)
            {
                char mode = downloadList[i].Item1;
                string path = downloadList[i].Item2;

                if (mode == 'S')
                {
                    try
                    {
                        DownloadSize = int.Parse(path.Substring(1));
                    }
                    catch (Exception e)
                    {
                        throw new Exception("Failed to parse needed download size: " + e.Message);
                    }
                }
                else
                {
                    int filePathIndex = 0;
                    if ((mode == 'C' && !IsCitra))
                    {
                        downloadList[i] = ('I', downloadList[i].Item2);
                        mode = 'I';
                    }
                    if (mode == 'C' || mode == 'M' || mode == 'D')
                    {
                        while (filePathIndex < allFilePaths.Count)
                        {
                            if (path == allFilePaths[filePathIndex] && (allFileModes[filePathIndex] == 'M' || allFileModes[filePathIndex] == 'D' || (mode == 'C' && allFileModes[filePathIndex] == 'C')))
                            {
                                allFileModes[filePathIndex] = 'I';
                            }
                            filePathIndex++;
                        }
                    }
                    allFilePaths.Add(path);
                    allFileModes.Add(mode);
                }
            }

            DownloadCount = 0;
            for (int i = 0; i < allFilePaths.Count; i++)
            {
                if (allFileModes[i] == 'M' || allFileModes[i] == 'C')
                {
                    DownloadCount++;
                }
                ret.Add(new FileListEntry(CurrentUpdateIndex, allFileModes[i], BuildFilePath(allFilePaths[i]), BuildFileURL(allFilePaths[i], allFileModes[i] == 'C')));
            }

            return ret;
        }


        private long CheckNeededExtraSpace(long diskSpace)
        {
            return DownloadSize == 0 ? 0 : Math.Max(0, DownloadSize + _SLACK_FREE_SPACE - diskSpace);
        }

        private static string DownloadString(string url)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    byte[] data = client.GetByteArrayAsync(url).Result;
                    return Encoding.UTF8.GetString(data);
                }
            }
            catch (Exception e)
            {
                throw new Exception($"Failed download string from URL '{url}': {e.Message}");
            }
        }

        public void Stop()
        {
            IsStopped = true;
        }

        public bool IsStoppedCallback()
        {
            return IsStopped;
        }

        public void LogFileProgressCallback(string fileOnlyName, double progress)
        {
            Log($"Downloading file {CurrDownloadCount} of {DownloadCount}: \"{fileOnlyName}\" ({progress:F1}%)");
        }

        public void SetBaseURL(string url)
        {
            BaseURL = url;
        }

        public void SetLogFunction(Action<(char, object)> func)
        {
            LogFunction = func;
        }

        private void Log(string msg)
        {
            LogFunction?.Invoke(('m', msg));
        }

        private void Prog(int curr, int tot)
        {
            LogFunction?.Invoke(('p', (curr, tot)));
        }

        private static bool IsValidNintendo3DSSDCard(string path)
        {
            return Directory.Exists(Path.Combine(path, "Nintendo 3DS"));
        }

        public class InstallStatus
        {
            public bool CTGPFolderNotPresent;
            public bool VersionFileIssue;
            public bool PendingUpdateAvailable;
            public bool ReinstallFlagPresent;
        }

        public static InstallStatus CheckForInstallOfPath(string path)
        {
            bool cond1 = !Directory.Exists(Path.Combine(path, "CTGP-7")); // bit0 (1)
            bool cond2 = !File.Exists(Path.Combine(path, "CTGP-7", _VERSION_FILE_PATH)); // bit1 (2)
            bool cond3 = File.Exists(Path.Combine(path, "CTGP-7", _PENDINGUPDATE_PATH)); // bit2 (4)
            bool cond4 = File.Exists(Path.Combine(path, "CTGP-7", _REINSTALLFLAG_PATH)); // bit3 (8)

            InstallStatus status = new()
            {
                CTGPFolderNotPresent = cond1,
                VersionFileIssue = cond2,
                PendingUpdateAvailable = cond3,
                ReinstallFlagPresent = cond4,
            };

            try
            {
                long vfSz = new FileInfo(Path.Combine(path, "CTGP-7", _VERSION_FILE_PATH)).Length;
                if (vfSz < 3 || vfSz > 8)
                    status.VersionFileIssue = true;
            }
            catch
            {
                status.VersionFileIssue = true;
            }

            return status;
        }

        public void SetBaseDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                throw new Exception("Installation path invalid.");
            }
            BasePath = path;
        }

        public void GetLatestVersion()
        {
            Log("Fetching latest version...");
            try
            {
                LatestVersion = DownloadString(BaseURL + _LATEST_VER_LOCATION).Replace("\n", "").Replace("\r", "");
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to get latest version: {e.Message}");
            }
        }

        public void MakeReinstallFlag()
        {
            try
            {
                string p = Path.Combine(BasePath, "CTGP-7", _REINSTALLFLAG_PATH);
                MkFoldersForFile(p);
                File.Create(p).Close();
            }
            catch
            {
                // Ignore exceptions
            }
        }

        private static byte[] ReadUntilNulByte(BinaryReader reader)
        {
            List<byte> bytes = new List<byte>();
            while (true)
            {
                byte data = reader.ReadByte();
                if (data == 0x00)
                {
                    break;
                }
                bytes.Add(data);
            }
            return bytes.ToArray();
        }

        public void LoadUpdateInfo()
        {
            string LocalVersion;
            List<(char, string)> fileModeList = new List<(char, string)>();

            if (IsInstaller)
            {
                Log("Downloading file list...");
                try
                {
                    string[] fileList = DownloadString(BaseURL + _INSTALLER_FILE_DIFF).Split("\n");
                    foreach (string file in fileList)
                    {
                        if (string.IsNullOrEmpty(file))
                            continue;
                        fileModeList.Add((file[0], file.Substring(1).Trim()));
                    }
                    FileList = ParseAndSortDlList(fileModeList);
                }
                catch (Exception e)
                {
                    throw new Exception($"Failed to get list of files: {e.Message}");
                }
            }
            else
            {
                Log("Preparing update...");
                string pendUpdName = Path.Combine(BasePath, "CTGP-7", _PENDINGUPDATE_PATH);

                if (File.Exists(pendUpdName))
                {
                    int entriesLeft = 0;
                    using (BinaryReader puf = new BinaryReader(File.OpenRead(pendUpdName)))
                    {
                        entriesLeft = puf.ReadInt32();
                        LatestVersion = Encoding.UTF8.GetString(ReadUntilNulByte(puf));
                        for (int i = 0; i < entriesLeft; i++)
                        {
                            char fileMethod = (char)puf.ReadByte();
                            puf.ReadInt32();
                            string fileName = Encoding.UTF8.GetString(ReadUntilNulByte(puf));
                            fileModeList.Add((fileMethod, fileName));
                        }
                    }
                    FileList = ParseAndSortDlList(fileModeList);
                }
                else
                {
                    bool emptyDataRemover = true;


                    string fileListURL = DownloadString(BaseURL + _UPDATER_FILE_URL).Replace("\n", "").Replace("\r", "");
                    List<string> changelogData = DownloadString(BaseURL + _UPDATER_CHGLOG_FILE).Split(";").ToList();
                    for (int index = 0; index < changelogData.Count; index++)
                        changelogData[index] = changelogData[index].Split(":")[0];

                    while (emptyDataRemover)
                    {
                        try
                        {
                            if (!changelogData.Remove(""))
                                throw new Exception();
                        }
                        catch
                        {
                            emptyDataRemover = false;
                        }
                    }

                    try
                    {
                        string configPath = Path.Combine(BasePath, "CTGP-7", _VERSION_FILE_PATH);
                        using (StreamReader vf = new StreamReader(configPath))
                        {
                            LocalVersion = vf.ReadToEnd().Trim();
                        }
                    }
                    catch (Exception e)
                    {
                        MakeReinstallFlag();
                        throw new Exception($"Could not read the version file: {e.Message}");
                    }


                    try
                    {
                        int chglogIdx = changelogData.IndexOf(LocalVersion);
                        if (chglogIdx == -1)
                        {
                            MakeReinstallFlag();
                            throw new Exception("Current version not known. The version file might be corrupted or has been modified or an update has been revoked.");
                        }
                        if (chglogIdx == changelogData.Count)
                        {
                            throw new Exception("There are no updates available. If this is not correct, please try again later.");
                        }

                        int progTotal = changelogData.Count - chglogIdx - 1;
                        for (int index = chglogIdx; index < changelogData.Count; index++)
                        {
                            try
                            {
                                Log($"Preparing update (v{changelogData[index]})...");
                                Prog(index - chglogIdx, progTotal);
                                string fileListString = DownloadString(string.Format(fileListURL, changelogData[index]));
                                string[] fileList = fileListString.Split("\n");
                                foreach (string file in fileList)
                                {
                                    if (string.IsNullOrEmpty(file))
                                    {
                                        continue;
                                    }
                                    fileModeList.Add((file[0], file.Substring(1).Trim()));
                                }
                                FileList = ParseAndSortDlList(fileModeList);
                            }
                            catch (Exception e)
                            {
                                throw new Exception($"Failed to get list of files: {e.Message}");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Failed to get list of files: {e.Message}");
                    }
                }
            }
        }
        public void VerifySpaceAvailable()
        {
            var driveInfo = new DriveInfo(BasePath);
            long availableSpace = driveInfo.AvailableFreeSpace;
            long neededSpace = CheckNeededExtraSpace(availableSpace);
            if (neededSpace > 0)
            {
                throw new Exception($"Not enough free space on destination folder. Additional {neededSpace / 1000000} MB needed to proceed with installation.");
            }
        }

        public static string FindNintendo3DSRoot()
        {
            try
            {
                var mountPoints = DriveInfo.GetDrives();
                var candidates = new List<string>();
                foreach (var mountPoint in mountPoints)
                {
                    try
                    {
                        if (IsValidNintendo3DSSDCard(mountPoint.RootDirectory.FullName))
                        {
                            candidates.Add(mountPoint.RootDirectory.FullName);
                        }
                    }
                    catch
                    {
                        // Ignore exceptions and continue searching
                    }
                }
                if (candidates.Count == 1)
                {
                    return candidates[0];
                }
            }
            catch
            {
                // Ignore exceptions and return null
            }
            return null;
        }

        public static bool CheckProgramVersion()
        {
            string baseURL = GetDefaultCdnUrlAsString();
            string ver = DownloadString(baseURL + _INSTALLER_VERSION).Trim();
            return ver != MAIN_VERSION_NUMBER;
        }

        public void MakePendingUpdate()
        {
            byte[] header = Encoding.ASCII.GetBytes(LatestVersion);
            byte[] flist = new byte[] { };
            int pendingCount = 0;
            foreach (var entry in FileList)
            {
                if (!entry.HavePerformed)
                {
                    flist = flist.Concat(entry.ExportToPend()).ToArray();
                    pendingCount++;
                }
            }
            header = BitConverter.GetBytes(pendingCount).Reverse().ToArray().Concat(header).ToArray();

            string fileName = Path.Combine(BasePath, "CTGP-7", string.Join(Path.DirectorySeparatorChar.ToString(), _PENDINGUPDATE_PATH));
            MkFoldersForFile(fileName);
            FileDelete(fileName);
            using (var puf = new BinaryWriter(File.Open(fileName, FileMode.CreateNew)))
            {
                puf.Write(header);
                puf.Write(flist);
            }
        }

        public void StartUpdate()
        {
            string mainfolder = Path.Combine(BasePath, "CTGP-7");
            string hbrwfolder = Path.Combine(BasePath, "3ds");

            try
            {
                Directory.CreateDirectory(mainfolder);
                Directory.CreateDirectory(hbrwfolder);
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to create CTGP-7 directory: {e.Message}");
            }

            if (IsCitra)
            {
                string configPath = Path.Combine(BasePath, "CTGP-7", string.Join(Path.DirectorySeparatorChar.ToString(), _ISCITRAFLAG_PATH));
                MkFoldersForFile(configPath);
                File.WriteAllBytes(configPath, Encoding.ASCII.GetBytes("It's really a lemon, no?"));
            }
            string prevReturnValue = null;
            foreach (var entry in FileList)
            {
                if (entry.FileMethod == 'M' || entry.FileMethod == 'C')
                {
                    Prog(CurrDownloadCount, DownloadCount);
                    CurrDownloadCount++;
                }

                try
                {
                    prevReturnValue = entry.Perform(prevReturnValue);
                }
                catch (Exception e)
                {
                    Log("Aborting installation...");
                    if (!IsInstaller)
                    {
                        Log("Marking update as pending");
                        MakePendingUpdate();
                    }
                    if (e.InnerException is OperationCanceledException)
                        throw new Exception("User cancelled installation");
                    else throw new Exception(e.Message);
                }
                finally
                {
                    entry.HavePerformed = true;
                }
            }

            Prog(CurrDownloadCount, DownloadCount);

            string ciaFile = Path.Combine(mainfolder, "cia", "CTGP-7.cia");
            string hbrwFile = Path.Combine(mainfolder, "cia", "CTGP-7.3dsx");
            string hbrwFileFinal = Path.Combine(hbrwfolder, "CTGP-7.3dsx");
            string tooInstallCiaFile = Path.Combine(mainfolder, "cia", "tooInstall.cia");
            string tooInstallHbrwFile = Path.Combine(mainfolder, "cia", "tooInstall.3dsx");

            Log("Completing installation...");

            try
            {
                string configPath = Path.Combine(mainfolder, string.Join(Path.DirectorySeparatorChar.ToString(), _VERSION_FILE_PATH));
                MkFoldersForFile(configPath);
                File.WriteAllBytes(configPath, Encoding.ASCII.GetBytes(LatestVersion));
            }
            catch (Exception e)
            {
                MakeReinstallFlag();
                throw new Exception($"Failed to write version info: {e.Message}");
            }

            try
            {
                FileDelete(Path.Combine(BasePath, "CTGP-7", string.Join(Path.DirectorySeparatorChar.ToString(), _PENDINGUPDATE_PATH)));
                if (File.Exists(tooInstallHbrwFile))
                {
                    FileMove(tooInstallHbrwFile, hbrwFile);
                    File.Copy(hbrwFile, hbrwFileFinal);
                }
                if (File.Exists(tooInstallCiaFile))
                {
                    FileMove(tooInstallCiaFile, ciaFile);
                }
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to finish cleanup: {e.Message}");
            }

            Log("Installation complete!");

        }

        public void CleanInstallFolder()
        {
            // Only wipe folder if not updating
            if (!IsInstaller)
            {
                return;
            }
            string mainfolder = Path.Combine(BasePath, "CTGP-7");
            if (Directory.Exists(mainfolder))
            {
                Log("Cleaning up previous CTGP-7 installation...");
                Directory.Delete(mainfolder, true);
            }
        }

        public static bool IsCitraDirectory(string path)
        {
            string citraPath;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                citraPath = Path.Combine(Environment.GetEnvironmentVariable("APPDATA"), "Citra", "sdmc");
            }
            else
            {
                citraPath = Path.Combine(Environment.GetEnvironmentVariable("HOME"), ".local", "share", "citra-emu", "sdmc");
            }

            try
            {
                if (string.Equals(path, citraPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                else
                {
                    if (File.Exists(Path.Combine(path, "boot.firm")))
                    {
                        return false;
                    }
                    if (File.Exists(Path.Combine(path, string.Join(Path.DirectorySeparatorChar.ToString(), _ISCITRAFLAG_PATH))))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore exceptions and return null
            }
            return false;

        }
    }

    public class FileListEntry
    {
        private string filePath;
        private int forVersion; // Unused
        public char FileMethod;
        public bool HavePerformed;
        private string URL;
        private string fileOnlyName;
        private string remoteName;

        public FileListEntry(int ver, char method, string path, string url)
        {
            filePath = path;
            forVersion = ver; // Unused
            FileMethod = method;
            HavePerformed = false;

            URL = url;
            fileOnlyName = filePath.Substring(filePath.LastIndexOf(Path.DirectorySeparatorChar) + 1);
            remoteName = filePath.Substring(filePath.LastIndexOf(Path.DirectorySeparatorChar + "CTGP-7" + Path.DirectorySeparatorChar) + 7)
                .Replace("\\", "/");
        }

        public override bool Equals(object? obj)
        {
            if (obj is FileListEntry other)
            {
                return filePath == other.filePath &&
                       URL == other.URL &&
                       FileMethod == other.FileMethod &&
                       forVersion == other.forVersion;
            }

            return false;
        }


        public override string ToString()
        {
            return string.Format("ver: \"{0}\" method: \"{1}\" path: \"{2}\" url: \"{3}\"",
                forVersion, FileMethod, filePath, URL);
        }

        // Export struct for pendingUpdate.bin
        public byte[] ExportToPend()
        {
            byte[] methodBytes = BitConverter.GetBytes((byte)FileMethod);
            byte[] versionBytes = BitConverter.GetBytes(forVersion);
            byte[] remoteNameBytes = Encoding.UTF8.GetBytes(remoteName);
            byte[] nullTerminator = new byte[] { 0 };

            byte[] result = new byte[1 + sizeof(int) + remoteNameBytes.Length + nullTerminator.Length];

            int offset = 0;
            Buffer.BlockCopy(methodBytes, 0, result, offset, methodBytes.Length);
            offset += methodBytes.Length;

            Buffer.BlockCopy(versionBytes, 0, result, offset, versionBytes.Length);
            offset += versionBytes.Length;

            Buffer.BlockCopy(remoteNameBytes, 0, result, offset, remoteNameBytes.Length);
            offset += remoteNameBytes.Length;

            Buffer.BlockCopy(nullTerminator, 0, result, offset, nullTerminator.Length);

            return result;
        }


        private async Task DownloadFile()
        {
            const string _DOWN_PART_EXT = ".part"; // Better safe than sorry

            try
            {
                CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
                
                CTGP7Updater.MkFoldersForFile(filePath);
                using (HttpResponseMessage response = await CTGP7Updater.client.GetAsync(URL, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    // Get the total file size
                    long totalBytes = response.Content.Headers.ContentLength ?? -1;

                    using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                    {
                        using (FileStream fileStream = new FileStream(filePath + _DOWN_PART_EXT, FileMode.Create, FileAccess.Write))
                        {
                            byte[] buffer = new byte[4096];
                            long downloadedBytes = 0;
                            int bytesRead;

                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                if (CTGP7Updater.isStoppedCallback.Invoke()) throw new OperationCanceledException(); 

                                await fileStream.WriteAsync(buffer, 0, bytesRead);

                                downloadedBytes += bytesRead;

                                // Calculate the progress percentage
                                double progressPercentage = (double)downloadedBytes / totalBytes * 100;

                                // Invoke the progress callback with the percentage
                                CTGP7Updater.fileProgressCallback?.Invoke(Path.GetFileName(filePath), progressPercentage);
                            }
                        }
                    }


                    CTGP7Updater.FileMove(filePath + _DOWN_PART_EXT, filePath);
                }
            }
            catch (Exception e)
            {
                if (e is IOException || e is WebException || e is TimeoutException)
                {
                    CTGP7Updater.FileDelete(filePath + _DOWN_PART_EXT);
                    throw new Exception($"Failed to download file \"{fileOnlyName}\": {e.Message}");
                }
                else
                {
                    throw;
                }
            }
        }

        public string Perform(string lastPerformValue)
        {
            if (FileMethod == 'M' || FileMethod == 'C') // Modify
            {
                DownloadFile().Wait();
                return null;
            }
            else if (FileMethod == 'D') // Delete
            {
                CTGP7Updater.FileDelete(filePath);
                return null;
            }
            else if (FileMethod == 'F') // (Rename) From
            {
                return filePath;
            }
            else if (FileMethod == 'T') // (Rename) To
            {
                if (lastPerformValue != null)
                {
                    CTGP7Updater.FileMove(lastPerformValue, filePath);
                }
                else
                {
                    throw new Exception($"Rename to statement for \"{fileOnlyName}\" is missing rename from statement");
                }
                return null;
            }
            else if (FileMethod == 'I') // Ignore file
            {
                return lastPerformValue;
            }
            else
            {
                throw new Exception($"Unknown file mode: {FileMethod}");
            }
        }
    }
}

