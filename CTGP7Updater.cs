using System.Text;
using System.Net;
using File = System.IO.File;
using System;
using System.Net.Http;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using Path = System.IO.Path;

namespace CTGP7InstallerSharp
{

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

        public static HttpClient client = new();

        public static Func<bool>? IsStoppedCallback;
        public static Action<string, double>? FileProgressCallback;

        public bool IsInstaller { get; set; }
        public string BasePath { get; set; }
        public int DownloadCount { get; set; }
        public int CurrDownloadCount { get; set; }
        public long FileDownCurr { get; set; }
        public long FileDownSize { get; set; }
        public List<FileListEntry> FileList { get; set; }
        public string LatestVersion { get; set; }
        public Action<(char, object)>? LogFunction { get; set; }
        public bool IsStopped { get; set; }
        public long DownloadSize { get; set; }
        public int CurrentUpdateIndex { get; set; }
        public bool IsCitra { get; set; }
        public string? BaseURL { get; set; }

        public CTGP7Updater(Action<(char, object)> logFunction, bool isInstaller = true, bool isCitra = false)
        {
            IsInstaller = isInstaller;
            BasePath = string.Empty;
            DownloadCount = 0;
            CurrDownloadCount = 0;
            FileDownCurr = 0;
            FileDownSize = 0;
            FileList = new List<FileListEntry>();
            LatestVersion = string.Empty;
            SetLogFunction(logFunction);
            IsStopped = false;
            DownloadSize = 0;
            CurrentUpdateIndex = 0;
            IsCitra = isCitra;
            client.DefaultRequestHeaders.Connection.Add("keep-alive");

            IsStoppedCallback = IsStoppedCallbackFunc;
            FileProgressCallback = LogFileProgressCallbackFunc;
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

        public static void DeleteFile(string file)
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal); // Ensure the file is not read-only
                File.Delete(file);
            }
            catch { }
        }

        public static void MoveFile(string oldf, string newf)
        {
            DeleteFile(newf);
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
                BaseURL = GetDefaultCdnUrlAsString();
            }
            catch (Exception e)
            {
                throw new Exception("Failed to init updater: " + e.Message);
            }
        }

        public static void CreateDirectoriesRecursively(string fol)
        {
            string g = fol[..fol.LastIndexOf(Path.DirectorySeparatorChar)];
            Directory.CreateDirectory(g);
        }

        private string BuildFilePath(string path)
        {
            return Path.Combine(Path.Combine(BasePath, "CTGP-7"), path.Replace("/", Path.DirectorySeparatorChar.ToString())[1..]);
        }

        private string BuildFileURL(string path, bool isCitra)
        {
            return BaseURL + (isCitra ? _FILES_LOCATION_CITRA : _FILES_LOCATION) + path;
        }

        private List<FileListEntry> ParseAndSortDlList(List<(char, string)> downloadList)
        {
            List<string> allFilePaths = new();
            List<char> allFileModes = new();
            List<FileListEntry> ret = new();

            for (int i = 0; i < downloadList.Count; i++)
            {
                char mode = downloadList[i].Item1;
                string path = downloadList[i].Item2;

                if (mode == 'S')
                {
                    try
                    {
                        DownloadSize = int.Parse(path[1..]);
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
                using (HttpClient client = new())
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

        public bool IsStoppedCallbackFunc()
        {
            return IsStopped;
        }

        public void LogFileProgressCallbackFunc(string fileOnlyName, double progress)
        {
            Log($"Downloading file {CurrDownloadCount} of {DownloadCount}: \"{fileOnlyName}\" ({progress:F1}%)");
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
                string reinstFlag = Path.Combine(BasePath, "CTGP-7", _REINSTALLFLAG_PATH);
                CreateDirectoriesRecursively(reinstFlag);
                File.Create(reinstFlag).Close();
            }
            catch
            {
                // Ignore exceptions
            }
        }

        private static byte[] ReadUntilNulByte(BinaryReader reader)
        {
            if (reader == null)
                return Array.Empty<byte>();

            List<byte> bytes = new();

            while (true)
            {
                byte data = reader.ReadByte();
                if (data == 0)
                    break;

                bytes.Add(data);
            }

            return bytes.ToArray();
        }

        public void LoadUpdateInfo()
        {
            string LocalVersion;
            List<(char, string)> fileModeList = new();

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
                        fileModeList.Add((file[0], file[1..].Trim()));
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

                // TODO: Ensure functionality
                if (File.Exists(pendUpdName))
                {
                    int entriesLeft;

                    using (BinaryReader puf = new(File.OpenRead(pendUpdName)))
                    {
                        if (!BitConverter.IsLittleEndian)
                            entriesLeft = BitConverter.ToInt32(puf.ReadBytes(4).Reverse().ToArray());
                        else
                            entriesLeft = puf.ReadInt32();
                        LatestVersion = Encoding.UTF8.GetString(ReadUntilNulByte(puf));
                        foreach (int _ in Enumerable.Range(0, entriesLeft))
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

                    string fileListURL = DownloadString(BaseURL + _UPDATER_FILE_URL).Replace("\n", "").Replace("\r", "").Replace("%s", "{0}");
                    List<string> changelogData = DownloadString(BaseURL + _UPDATER_CHGLOG_FILE).Split(";").ToList();
                    for (int index = 0; index < changelogData.Count; index++)
                        changelogData[index] = changelogData[index].Split(":")[0];
                    
                    bool emptyDataRemover = true;

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
                        using (StreamReader vf = new(configPath))
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
                        // count is 63, indexof on 1.4.15 would be 62, indexof on 1.4.13 would be 60, adding 1 would be 63 and 61 respectively
                        int chglogIdx = changelogData.IndexOf(LocalVersion) + 1;

                        if (chglogIdx == -1)
                        {
                            MakeReinstallFlag();
                            throw new Exception("Current version not known. The version file might be corrupted, been modified, or an update has been revoked.");
                        }
                        else if (chglogIdx == changelogData.Count)
                        {
                            throw new Exception("There are no updates available. If this is not correct, please try again later.");
                        }

                        // progTotal = 63 (Count) - 61 (chglogIdx)  (2)
                        int progTotal = changelogData.Count - chglogIdx;

                        // index start = starts 62 inclusive (chglogIdx + 1), count is 63 (count) - chglogIdx (61)   (2)
                        // index would be 62 and 63
                        foreach (int index in Enumerable.Range(chglogIdx + 1, changelogData.Count - chglogIdx))
                        {
                            try
                            {
                                Log($"Preparing update (v{changelogData[index - 1]})...");
                                // index (62 or 63) - chglogIdx (61)  (1/2)
                                Prog(index - chglogIdx, progTotal);
                                string fileListString = DownloadString(string.Format(fileListURL, changelogData[index - 1]));
                                string[] fileList = fileListString.Split("\n");
                                foreach (string file in fileList)
                                {
                                    if (string.IsNullOrEmpty(file))
                                    {
                                        continue;
                                    }
                                    fileModeList.Add((file[0], file[1..].Trim()));
                                }
                            }
                            catch (Exception e)
                            {
                                throw new Exception($"Failed to get list of files: {e.Message}");
                            }
                        }
                        FileList = ParseAndSortDlList(fileModeList);
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
                throw new Exception($"Not enough free space on destination folder. Additional {neededSpace / 1048576:F1} MB needed to proceed with installation.");
            }
        }

        public static string? FindNintendo3DSRoot()
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
            // TODO: Ensure functionality of making update
            byte[] header = Encoding.ASCII.GetBytes(LatestVersion).AddNullByte();
            byte[] flist = Array.Empty<byte>();
            int pendingCount = 0;
            foreach (FileListEntry entry in FileList)
            {
                if (!entry.HavePerformed)
                {
                    flist = flist.Concat(entry.ExportToPend()).ToArray();
                    pendingCount++;
                }
            }
            if (!BitConverter.IsLittleEndian)
                header = BitConverter.GetBytes(pendingCount).Reverse().ToArray().Concat(header).ToArray();
            else
                header = BitConverter.GetBytes(pendingCount).ToArray().Concat(header).ToArray();

            string fileName = Path.Combine(BasePath, "CTGP-7", string.Join(Path.DirectorySeparatorChar.ToString(), _PENDINGUPDATE_PATH));
            CreateDirectoriesRecursively(fileName);
            DeleteFile(fileName);
            using (BinaryWriter puf = new(File.Open(fileName, FileMode.CreateNew)))
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
                CreateDirectoriesRecursively(configPath);
                File.WriteAllBytes(configPath, Encoding.ASCII.GetBytes("It's really a lemon, no?"));
            }
            string? prevReturnValue = null;
            foreach (var entry in FileList)
            {
                if (entry.FileMethod == 'M' || entry.FileMethod == 'C')
                {
                    Prog(CurrDownloadCount, DownloadCount);
                    CurrDownloadCount++;
                }

                try
                {
                    prevReturnValue = entry.Perform(prevReturnValue!);
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
                CreateDirectoriesRecursively(configPath);
                File.WriteAllBytes(configPath, Encoding.ASCII.GetBytes(LatestVersion));
            }
            catch (Exception e)
            {
                MakeReinstallFlag();
                throw new Exception($"Failed to write version info: {e.Message}");
            }

            try
            {
                DeleteFile(Path.Combine(BasePath, "CTGP-7", string.Join(Path.DirectorySeparatorChar.ToString(), _PENDINGUPDATE_PATH)));
                if (File.Exists(tooInstallHbrwFile))
                {
                    MoveFile(tooInstallHbrwFile, hbrwFile);
                    File.Copy(hbrwFile, hbrwFileFinal, true);
                }
                if (File.Exists(tooInstallCiaFile))
                {
                    MoveFile(tooInstallCiaFile, ciaFile);
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

        public static bool? IsCitraDirectory(string path)
        {
            string citraPath;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                citraPath = Path.Combine(Environment.GetEnvironmentVariable("APPDATA")!, "Citra", "sdmc");
            }
            else
            {
                citraPath = Path.Combine(Environment.GetEnvironmentVariable("HOME")!, ".local", "share", "citra-emu", "sdmc");
            }
            // TODO: android check

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
            return null;

        }
    }

    public class FileListEntry
    {
        readonly private string FilePath;
        readonly private int ForVersion; // Unused
        public char FileMethod;
        public bool HavePerformed;
        readonly private string URL;
        readonly private string FileOnlyName;
        readonly private string RemoteName;

        public FileListEntry(int ver, char method, string path, string url)
        {
            FilePath = path;
            ForVersion = ver; // Unused
            FileMethod = method;
            HavePerformed = false;

            URL = url;
            FileOnlyName = FilePath[(FilePath.LastIndexOf(Path.DirectorySeparatorChar) + 1)..];
            RemoteName = FilePath[(FilePath.LastIndexOf(Path.DirectorySeparatorChar + "CTGP-7" + Path.DirectorySeparatorChar) + 7)..]
                .Replace("\\", "/");
        }

        public override bool Equals(object? obj)
        {
            if (obj is FileListEntry other)
            {
                return FilePath == other.FilePath &&
                       URL == other.URL &&
                       FileMethod == other.FileMethod &&
                       ForVersion == other.ForVersion;
            }

            return false;
        }
        public override int GetHashCode()
        {
            return $"{FileMethod}/{FilePath}".GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("ver: \"{0}\" method: \"{1}\" path: \"{2}\" url: \"{3}\"",
                ForVersion, FileMethod, FilePath, URL);
        }

        // Export struct for pendingUpdate.bin
        public byte[] ExportToPend()
        {
            byte[] fileMethodByte = BitConverter.GetBytes(FileMethod);
            byte[] remotePathBytes = Encoding.ASCII.GetBytes(RemoteName);

            // Pack the data into a byte array
            byte[] result = new byte[1 + remotePathBytes.Length];
            Buffer.BlockCopy(fileMethodByte, 0, result, 0, 1);
            result = result.AddNullByte(4); // 4-byte padding, initialized to all zeroes (technically forVersion, but always is 0)
            Buffer.BlockCopy(remotePathBytes, 0, result, 5, remotePathBytes.Length);
            result = result.AddNullByte();

            return result;
        }


        private async Task DownloadFile()
        {
            const string _DOWN_PART_EXT = ".part"; // Better safe than sorry

            try
            {
                CancellationTokenSource cancellationTokenSource = new();

                CTGP7Updater.FileProgressCallback?.Invoke(Path.GetFileName(FileOnlyName), 0);

                CTGP7Updater.CreateDirectoriesRecursively(FilePath);



                char[] separators = { '\\', '/' };
                string trimmedPath = RemoteName.Replace("/", "\\").TrimStart(separators);

#if DEBUG_BENCH
                CustomTimer timer = new(trimmedPath);
                timer.Start("request+cs1+cs2+write");
                timer.Start("request+cs1+cs2");
                timer.Start("webrequest");
#endif
                using (HttpResponseMessage response = await CTGP7Updater.client.GetAsync(URL, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
#if DEBUG_BENCH
                    timer.Stop("webrequest");
#endif

                    // Get the total file size
                    long totalBytes = response.Content.Headers.ContentLength ?? -1;

#if DEBUG_BENCH
                    timer.Start("createstream1");
#endif
                    using (Stream contentStream = await response.Content.ReadAsStreamAsync())
                    {
#if DEBUG_BENCH
                        timer.Stop("createstream1");
                        timer.Start("createstream2");
#endif
                        using (FileStream fileStream = new(FilePath + _DOWN_PART_EXT, FileMode.Create, FileAccess.Write))
                        {
#if DEBUG_BENCH
                            timer.Stop("createstream2");
                            timer.Stop("request+cs1+cs2");
#endif
                            byte[] buffer = new byte[16384];
                            long downloadedBytes = 0;
                            int bytesRead;

#if DEBUG_BENCH
                            timer.Start("write");
#endif

                            int writeCount = 0;
                            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
                            {
                                if (CTGP7Updater.IsStoppedCallback!.Invoke()) throw new OperationCanceledException(); 

                                await fileStream.WriteAsync(buffer);

                                downloadedBytes += bytesRead;

                                // Calculate the progress percentage
                                double progressPercentage = (double)downloadedBytes / totalBytes * 100;

                                writeCount++;

                                // Invoke the progress callback with the percentage every "writeCount % Math.Round(8d / (buffer.Length / 8192)) == 0" writes (lol)
                                if (writeCount % Math.Round(8d / (buffer.Length / 8192)) == 0) CTGP7Updater.FileProgressCallback?.Invoke(Path.GetFileName(FileOnlyName), progressPercentage);
                            }
#if DEBUG_BENCH
                            timer.Stop("write");
                            timer.Stop("request+cs1+cs2+write");
#endif
                        }
                    }
                    CTGP7Updater.FileProgressCallback?.Invoke(Path.GetFileName(FileOnlyName), 100);

                    CTGP7Updater.MoveFile(FilePath + _DOWN_PART_EXT, FilePath);
                }
#if DEBUG_BENCH
                timer.Close();
#endif
            }
            catch (Exception e)
            {
                if (e is IOException || e is WebException || e is TimeoutException)
                {
                    CTGP7Updater.DeleteFile(FilePath + _DOWN_PART_EXT);
                    throw new Exception($"Failed to download file \"{FileOnlyName}\": {e.Message}");
                }
                else
                {
                    throw;
                }
            }
        }

        public string? Perform(string lastPerformValue)
        {
            if (FileMethod == 'M' || FileMethod == 'C') // Modify
            {
                DownloadFile().Wait();
                return null;
            }
            else if (FileMethod == 'D') // Delete
            {
                CTGP7Updater.DeleteFile(FilePath);
                return null;
            }
            else if (FileMethod == 'F') // (Rename) From
            {
                return FilePath;
            }
            else if (FileMethod == 'T') // (Rename) To
            {
                if (lastPerformValue != null)
                {
                    CTGP7Updater.MoveFile(lastPerformValue, FilePath);
                }
                else
                {
                    throw new Exception($"Rename to statement for \"{FileOnlyName}\" is missing rename from statement");
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

    public class CustomTimer
    {
        FileStream file;
        StreamWriter writer;
        Dictionary<string, Stopwatch> timers = new();

        string Name;

        public CustomTimer(string name)
        {
            Name = name;

            string newPath = $"{Path.Combine("TimeLogs", Name)}.txt";

            CTGP7Updater.CreateDirectoriesRecursively(newPath);
            file = File.OpenWrite(newPath);
            writer = new(file);
        }

        public void Add(string name)
        {
            timers.Add(name, new());
            
        }

        public void Start(string name)
        {
            if (!timers.ContainsKey(name))
                Add(name);
            timers[name].Start();
        }

        public void Stop(string name)
        {
            timers[name].Stop();
            writer.WriteLine($"{name} = {timers[name].ElapsedMilliseconds}ms");
        }

        public void Close()
        {
            writer.Flush();
            writer.Close(); // Close the StreamWriter
            file.Close(); // Close the FileStream
            writer.Dispose();
            file.Dispose(); 
        }
    }
}

