using System.IO;
using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia;
using MessageBox.Avalonia;
using static MessageBox.Avalonia.Enums.Icon;
using static MessageBox.Avalonia.Enums.ButtonResult;
using static MessageBox.Avalonia.Enums.ButtonEnum;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Color = Avalonia.Media.Color;
using Brushes = Avalonia.Media.Brushes;
using Avalonia.Threading;
using System.Linq;

namespace CTGP7InstallerSharp
{
    public partial class MainWindow : Window
    {

        private bool isInstaller;
        private bool? isCitraPath;
        private bool hasPending;
        private bool didSaveBackup;
        private int startButtonState;
        CTGP7InstallerWorker? worker;

        private ProgressBar progressBar;
        private Label progressInfoLabel;
        private TextBox sdRootText;
        private Label miscInfoLabel;
        private Button updateButton;
        private Button startStopButton;
        private Button sdBrowseButton;
        private Button helpButton;

        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);

            // Find the ProgressBar control by its resource key
            progressBar = this.FindControl<ProgressBar>("progressBar")!;
            progressInfoLabel = this.FindControl<Label>("progressInfoLabel")!;
            sdRootText = this.FindControl<TextBox>("sdRootText")!;
            miscInfoLabel = this.FindControl<Label>("miscInfoLabel")!;
            updateButton = this.FindControl<Button>("updateButton")!;
            startStopButton = this.FindControl<Button>("startStopButton")!;
            sdBrowseButton = this.FindControl<Button>("sdBrowseButton")!;
            helpButton = this.FindControl<Button>("helpButton")!;

            AdditionalInitializeComponent();
        }

        private void AdditionalInitializeComponent()
        {

            Title = "CTGP-7 Installer Sharp v" + CTGP7Updater.SHARP_VERSION_NUMBER;
            AttachSignals();
            progressBar.IsEnabled = false;
            progressInfoLabel.Content = "";
            isInstaller = true;
            isCitraPath = null;
            hasPending = false;
            didSaveBackup = false;
            SetStartButtonState(0);
            worker = null;

            CheckVersion();
            ScanForSD();
        }


        private void CheckVersion()
        {
            try
            {
                if (CTGP7Updater.CheckProgramVersion())
                {
                    IsEnabled = false;
                    MessageBoxManager
                        .GetMessageBoxStandardWindow(
                            title: "Update Check",
                            text: "There's a new update available for the PC installer.\n\n" +
                                     "It is recommended to visit the Gamebanana page " +
                                     "to download the latest version to ensure that the PC installer can work smoothly.",
                            icon: Warning
                        ).Show();
                    IsEnabled = true;

                }
            }
            catch (Exception e)
            {
                IsEnabled = false;
                MessageBoxManager
                    .GetMessageBoxStandardWindow(
                        title: "Update Check",
                        text: "An error has occurred while checking for updates.\n" +
                                 "Ensure your device is connected to the internet.\n\n" +
                                 "If this error keeps happening, ask for help in the " +
                                 "CTGP-7 Discord Server.\n\n" +
                                 $"{e}\n\n{GetVersionInfo()}",
                        icon: Warning
                    ).Show();
                IsEnabled = true;

            }
        }

        public static string GetVersionInfo()
        {
            return $"Installer version: {CTGP7Updater.SHARP_VERSION_NUMBER}" +
                   $"\nPython installer version equivalent: {CTGP7Updater.MAIN_VERSION_NUMBER}";
        }

        public void ReportProgress((char, object) data)
        {
            //if (data.Item1 == 'p') MessageBox.Show(data.Item1.ToString(), "progression");
            if (data.Item1 == 'm')
            {
                progressInfoLabel.SafeInvoke(() => progressInfoLabel.Content = data.Item2.ToString());
            }
            if (data.Item1 == 'p')
            {



                var progressData = ((int, int))data.Item2;
                int currentValue = progressData.Item1;
                int totalValue = progressData.Item2;

                double percentage = (double)currentValue / totalValue * 100;

                /*if (TaskbarManager.IsPlatformSupported)
                {
                    // Set the taskbar progress percentage
                    if (progressPercentage < 1)
                        this.SafeInvoke(() => TaskbarManager.Instance.SetProgressValue(progressPercentage, 100, Handle));
                    else
                        this.SafeInvoke(() => TaskbarManager.Instance.SetProgressValue(1, 100, this.Handle));
                }*/

                progressBar.SafeInvoke(() => progressBar.IsEnabled = true);
                progressBar.SafeInvoke(() => progressBar.Value = percentage);
            }
        }

        public void ResetMenu()
        {
            progressBar.SafeInvoke(() => progressBar.Value = 0);
            progressBar.SafeInvoke(() => progressBar.IsEnabled = false);
            sdBrowseButton.SafeInvoke(() => sdBrowseButton.IsEnabled = true);
            helpButton.SafeInvoke(() => helpButton.IsEnabled = true);
            sdRootText.SafeInvoke(() => sdRootText.IsEnabled = true);
            progressInfoLabel.SafeInvoke(() => progressInfoLabel.Content = "");
            UpdateSDFolder(sdRootText.Text);
        }

        private void OnInstallError(string err)
        {
            /*if (TaskbarManager.IsPlatformSupported)
            {
                // Set the taskbar progress state
                this.SafeInvoke(() => TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.NoProgress, Handle));
            }*/
            Extensions.SafeInvoke(() =>
                MessageBoxManager
                    .GetMessageBoxStandardWindow(
                text: "An error has occurred during the installation.\n" +
                "If this error keeps happening, ask for help in the " +
                "<a href='https://discord.com/invite/0uTPwYv3SPQww54l'>" +
                "CTGP-7 Discord Server</a>. \n\n" +
                $"{err}\n\n{GetVersionInfo()}",
                title: "Error",
                icon: Error
                ).Show(this));

            ResetMenu(); // close()
        }

        private async void OnInstallSuccess(string ver)
        {
             /*if (TaskbarManager.IsPlatformSupported)
            {
                // Set the taskbar progress state
                this.SafeInvoke(() => TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.NoProgress, Handle));
            }*/
            this.SafeInvoke(() => MessageBoxManager
                    .GetMessageBoxStandardWindow(
                text: $"Installation finished successfully! (v{ver})\n" +
                "Make sure to install the cia file in the CTGP-7 -> cia folder in the SD card.",
                title: "Installation finished",
                icon: Success).Show(this));

            if (didSaveBackup)
            {
                if (await MessageBoxManager
                    .GetMessageBoxStandardWindow(
                    text: "Would you like to restore the save backup done previously?",
                    title: "Save backup",
                    @enum: YesNo).Show(this) == Yes)
                {
                    var savefolder = Path.Combine(sdRootText.Text, "CTGP-7", "savefs");
                    var backupfolder = Path.Combine(sdRootText.Text, "CTGP-7savebak");
                    try
                    {
                        Directory.CreateDirectory(savefolder);
                        CopyDirectory(backupfolder, savefolder);
                    }
                    catch (Exception e)
                    {
                        OnInstallError($"Failed to restore save backup, please restore it manually: {e}");
                        return;
                    }
                }
            }

            ResetMenu(); // close()
        }

        // Helper method to recursively copy a directory
        private void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, dest);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(dir));
                CopyDirectory(dir, dest);
            }
        }
        private bool DoSaveBackup()
        {
            try
            {
                var savefolder = Path.Combine(sdRootText.Text, "CTGP-7", "savefs");
                var backupfolder = Path.Combine(sdRootText.Text, "CTGP-7savebak");

                if (Directory.Exists(savefolder))
                {
                    ReportProgress(('m', "Doing save backup..."));

                    if (Directory.Exists(backupfolder))
                    {
                        Directory.Delete(backupfolder, true);
                    }

                    Directory.Move(savefolder, backupfolder);
                    didSaveBackup = true;

                    MessageBoxManager
                    .GetMessageBoxStandardWindow(
                        text: $"Save data backup of the previous CTGP-7 installation has been made in {backupfolder}",
                        title: "Save backup",
                        icon: Info).Show(this);
                }

                return true;
            }
            catch (Exception e)
            {
                OnInstallError($"Failed to create save backup: {e}");
                return false;
            }
        }

        private void ScanForSD()
        {
            var folder = CTGP7Updater.FindNintendo3DSRoot();

            if (folder != null)
            {
                sdRootText.Text = folder;
            }
            else if (Directory.Exists(CTGP7Updater.GetCitraDir()))
            {
                sdRootText.Text = CTGP7Updater.GetCitraDir();

                IsEnabled = false;
                MessageBoxManager
                    .GetMessageBoxStandardWindow(
                    text: "Couldn't detect an SD Card but a Citra build was found.\n" +
                    "If you want to install/update CTGP-7 for a 3DS console, " +
                    "use the \"Browse\" button to navigate to the SD Card of your console.",
                    title: "Couldn't find SD Card",
                    icon: Info).Show();
                IsEnabled = true;
            }
        }

        private async void OnUpdateButtonPress()
        {
            if (hasPending && await
                MessageBoxManager
                    .GetMessageBoxStandardWindow(
                    text: "A pending update was detected. You must finish it first, before updating again. " +
                    "Do you want to continue this update?",
                    title: "Pending update",
                    @enum: YesNo).Show(this) == No)
            {
                return;
            }

            isInstaller = false;
            miscInfoLabel.SafeInvoke(() => miscInfoLabel.Content = "");

            worker = new CTGP7InstallerWorker(sdRootText.Text, isInstaller, (bool)isCitraPath!);
            worker.signals.Progress += ReportProgress;
            worker.signals.Success += OnInstallSuccess;
            worker.signals.Error += OnInstallError;

            SetStartButtonState(4);
            sdBrowseButton.IsEnabled = false;
            helpButton.IsEnabled = false;
            sdRootText.IsEnabled = false;

            Thread thread = new(start => worker.Run());
            thread.Start();
        }

        private async void MainButtonPress()
        {
            if (startButtonState > 0 && startButtonState < 4)
            {
                if (startButtonState == 2 && await
                    MessageBoxManager
                    .GetMessageBoxStandardWindow(
                        text: "You are about to re-install CTGP-7.\n" +
                        "Any modifications via MyStuff will be deleted.\n\n" +
                        "Do you want to continue?\n" +
                        "(Your save data will be backed up, if possible.)",
                        title: "Confirm re-installation",
                        @enum: YesNo).Show(this) == No)
                {
                    return;
                }

                if (startButtonState == 3 && await
                    MessageBoxManager
                    .GetMessageBoxStandardWindow(
                        text: "This installation is either corrupted or was flagged for removal. " +
                        "Proceeding will wipe this installation and create a new one.\n\n" +
                        "Do you want to proceed anyway?\n" +
                        "(Your save data will be backed up, if possible.)",
                        title: "Broken CTGP-7 installation",
                        @enum: YesNo).Show(this) == No)
                {
                    return;
                }

                if (isInstaller && !DoSaveBackup())
                {
                    return;
                }

                isInstaller = true;
                miscInfoLabel.SafeInvoke(() => miscInfoLabel.Content = "");
                worker = new CTGP7InstallerWorker(sdRootText.Text, isInstaller, (bool)isCitraPath!);
                worker.signals.Progress += ReportProgress;
                worker.signals.Success += OnInstallSuccess;
                worker.signals.Error += OnInstallError;
                SetStartButtonState(4);
                sdBrowseButton.IsEnabled = false;
                helpButton.IsEnabled = false;
                sdRootText.IsEnabled = false;
                Thread thread = new(start => worker.Run());
                thread.Start();
            }
            else if (startButtonState == 4)
            {
                worker?.signals.RaiseStopEvent();
                SetStartButtonState(0);
            }
        }

        private void SetStartButtonState(int state)
        {
            startButtonState = state;
            startStopButton.SafeInvoke(() => startStopButton.IsEnabled = state != 0);
            startStopButton.SafeInvoke(() => startStopButton.Width = 510);
            updateButton.SafeInvoke(() => updateButton.Content = hasPending ? "Continue update" : "Update");
            updateButton.SafeInvoke(() => updateButton.IsEnabled = true);
            updateButton.SafeInvoke(() => updateButton.IsVisible = false);

            if (state == 0)
            {
                startStopButton.SafeInvoke(() => startStopButton.Content = "");
                //startStopButton.ClearFocus();
                updateButton.SafeInvoke(() => updateButton.Content = "");
                //updateButton.ClearFocus();
            }
            else if (state == 1)
            {
                startStopButton.SafeInvoke(() => startStopButton.Content = "Install");
                updateButton.SafeInvoke(() => updateButton.IsEnabled = false);
            }
            else if (state == 2)
            {
                startStopButton.SafeInvoke(() => startStopButton.Width = 250);
                startStopButton.SafeInvoke(() => startStopButton.Content = "Re-install");
                updateButton.SafeInvoke(() => updateButton.IsVisible = true);
            }
            else if (state == 3)
            {
                startStopButton.SafeInvoke(() => startStopButton.Content = "Re-install");
                updateButton.SafeInvoke(() => updateButton.IsEnabled = false);
            }
            else if (state == 4)
            {
                startStopButton.SafeInvoke(() => startStopButton.Content = "Cancel");
                updateButton.SafeInvoke(() => updateButton.IsEnabled = false);
                updateButton.SafeInvoke(() => updateButton.IsVisible = false);
            }
        }

        private void UpdateSDFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || folder[^1] == ' ')
            {
                miscInfoLabel.SafeInvoke(() => miscInfoLabel.Content = "");
                SetStartButtonState(0);
                return;
            }

            isCitraPath = CTGP7Updater.IsCitraDirectory(folder);

            if (Directory.Exists(folder))
            {
                CTGP7Updater.InstallStatus bmsk = CTGP7Updater.CheckForInstallOfPath(folder);
                
                miscInfoLabel.SafeInvoke(() => miscInfoLabel.Content = "Ready to install CTGP-7.");
                miscInfoLabel.SafeInvoke(() => miscInfoLabel.Foreground = new SolidColorBrush(Color.Parse("#084")));
                if (!Directory.Exists(Path.Combine(folder, "Nintendo 3DS")))
                {
                    miscInfoLabel.SafeInvoke(() => miscInfoLabel.Content = "This path appears to not be of a 3DS SD Card.");
                    miscInfoLabel.SafeInvoke(() => miscInfoLabel.Foreground = new SolidColorBrush(Color.Parse("#c60")));
                }
                if (bmsk.VersionFileIssue && !bmsk.CTGPFolderNotPresent)
                {
                    miscInfoLabel.SafeInvoke(() => miscInfoLabel.Content = "Corrupted CTGP-7 installation detected.");
                    miscInfoLabel.SafeInvoke(() => miscInfoLabel.Foreground = new SolidColorBrush(Color.Parse("#f40")));
                    SetStartButtonState(3);
                }
                else if (bmsk.ReinstallFlagPresent)
                {
                    miscInfoLabel.SafeInvoke(() => miscInfoLabel.Content = "Broken CTGP-7 installation detected.");
                    miscInfoLabel.SafeInvoke(() => miscInfoLabel.Foreground = new SolidColorBrush(Color.Parse("#f24")));
                    SetStartButtonState(3);
                }
                else if (bmsk.CTGPFolderNotPresent)
                {
                    SetStartButtonState(1);
                }
                else
                {
                    hasPending = bmsk.PendingUpdateAvailable;
                    miscInfoLabel.SafeInvoke(() => miscInfoLabel.Content = "Valid CTGP-7 installation detected.");
                    miscInfoLabel.SafeInvoke(() => miscInfoLabel.Foreground = new SolidColorBrush(Color.Parse("#480")));
                    SetStartButtonState(2);
                }
            }
            else
            {
                miscInfoLabel.SafeInvoke(() => miscInfoLabel.Content = "Folder does not exist");
                miscInfoLabel.SafeInvoke(() => miscInfoLabel.Foreground = Brushes.Red);
                SetStartButtonState(0);
            }
        }

        private async void BrowseForSD()
        {
            OpenFolderDialog dialog = new()
            {
                Title = "Select 3DS SD Card",
                Directory = sdRootText.Text
            };

            var result = await dialog.ShowAsync(this);
            if (!string.IsNullOrEmpty(result))
            {
                sdRootText.Text = result;
            }
        }

        private void ShowHelpDialog()
        {
            MessageBoxManager
                    .GetMessageBoxStandardWindow(
                text: "CTGP-7 Installer v" + CTGP7Updater.SHARP_VERSION_NUMBER + "\n\n" +
                "Having issues? Ask for help in the CTGP-7 Discord Server\n\n" +
                "2021-2023 CyberYoshi64, PabloMK7",
                title: "About").Show(this);
        }

        private void AttachSignals()
        {
            sdBrowseButton.Click += (s, e) => BrowseForSD();
            sdRootText.GetObservable(TextBox.TextProperty).Subscribe(UpdateSDFolder);
            helpButton.Click += (s, e) => ShowHelpDialog();
            startStopButton.Click += (s, e) => MainButtonPress();
            updateButton.Click += (s, e) => OnUpdateButtonPress();
        }
    }



    public class WorkerSignals
    {
        public event Action<(char, object)>? Progress;
        public event Action<string>? Error;
        public event Action<string>? Success;
        public event Action? Stop;

        public void RaiseProgressEvent((char, object) data)
        {
            Progress?.Invoke(data);
        }

        public void RaiseErrorEvent(string errorMessage)
        {
            Error?.Invoke(errorMessage);
        }

        public void RaiseSuccessEvent(string successMessage)
        {
            Success?.Invoke(successMessage);
        }

        public void RaiseStopEvent()
        {
            Stop?.Invoke();
        }

    }

    public class CTGP7InstallerWorker
    {
        readonly string BaseDir;
        readonly bool IsInstall;
        readonly bool IsCitra;
        public WorkerSignals signals;
        CTGP7Updater? updater;

        public CTGP7InstallerWorker(string basedir, bool isInstall, bool isCitra)
        {
            signals = new WorkerSignals();
            BaseDir = basedir;
            IsInstall = isInstall;
            IsCitra = isCitra;
            signals.Stop += OnStop;
            updater = null;
        }

        void LogData((char, object) data) => signals.RaiseProgressEvent(data);

        void OnStop() => updater?.Stop();

        public void Run()
        {
            try
            {
                LogData(('m', "Starting CTGP-7 Installation..."));
                updater = new CTGP7Updater(LogData, IsInstall, IsCitra);
                updater.FetchDefaultCDNURL();
                updater.SetBaseDirectory(BaseDir);
                updater.CleanInstallFolder();
                updater.GetLatestVersion();
                updater.LoadUpdateInfo();
                updater.VerifySpaceAvailable();
                /*if (TaskbarManager.IsPlatformSupported)
                {
                    // Set the taskbar progress state
                    MainForm.ActiveForm.SafeInvoke(() => TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.NoProgress, MainForm.ActiveForm.Handle));
                    MainForm.ActiveForm.SafeInvoke(() => TaskbarManager.Instance.SetProgressValue(1, 100, MainForm.ActiveForm.Handle));
                }*/
                updater.StartUpdate();

                signals.RaiseSuccessEvent(updater.LatestVersion);
            }
            catch (Exception e)
            {
                signals.RaiseErrorEvent(e.ToString());
            }
        }
    }


    public static class Extensions
    {
        public static async void SafeInvoke(this AvaloniaObject _, Action method) => await Dispatcher.UIThread.InvokeAsync(method);

        public static async void SafeInvoke(Action method) => await Dispatcher.UIThread.InvokeAsync(method);

        public static byte[] AddNullByte(this byte[] byteArray, int count = 1)
        {
            Array.Resize(ref byteArray, byteArray.Length + count);
            for (int i = count - 1; i > 0;)
            {
                byteArray[byteArray.Length - 1 - i] = 0x00;
                i--;
            }
            return byteArray;
        }
    }
}