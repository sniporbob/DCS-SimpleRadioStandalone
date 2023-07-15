﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using IWshRuntimeLibrary;
using MahApps.Metro.Controls;
using Microsoft.Win32;
using NLog;
using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using File = System.IO.File;
using ThreadState = System.Diagnostics.ThreadState;

namespace Installer
{
    /// <summary>
    ///     Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private const string REG_PATH = "HKEY_CURRENT_USER\\SOFTWARE\\DCS-SR-Standalone";
        private const string EXPORT_SRS_LUA = "pcall(function() local dcsSr=require('lfs');dofile(dcsSr.writedir()..[[Mods\\Services\\DCS-SRS\\Scripts\\DCS-SimpleRadioStandalone.lua]]); end,nil)";
        private readonly string _currentDirectory;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private ProgressBarDialog _progressBarDialog = null;

        public MainWindow()
        {
            SetupLogging();
            InitializeComponent();

            var assembly = Assembly.GetExecutingAssembly();
            var fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            var version = fvi.FileVersion;

            intro.Content = intro.Content + " v" + version;

            //allows click and drag anywhere on the window
            containerPanel.MouseLeftButtonDown += GridPanel_MouseLeftButtonDown;

            var srPathStr = ReadPath("SRPathStandalone");
            if (srPathStr != "")
            {
                srPath.Text = srPathStr;
            }

            var scriptsPath = ReadPath("ScriptsPath");
            if (scriptsPath != "")
            {
                dcsScriptsPath.Text = scriptsPath;
            }
            else
            {
                dcsScriptsPath.Text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) +
                                      "\\Saved Games\\";
            }

            //To get the location the assembly normally resides on disk or the install directory
            var currentPath = GetWorkingDirectory();

            if (currentPath.StartsWith("file:\\"))
            {
                currentPath = currentPath.Replace("file:\\", "");
            }

            _currentDirectory = currentPath;

            Logger.Info("Listing Files / Directories for: "+_currentDirectory);
            ListFiles(_currentDirectory);
            Logger.Info("Finished Listing Files / Directories");

            if (!CheckExtracted())
            {

                MessageBox.Show(
                    "Please extract the entire installation zip into a folder and then run the installer from the extracted folder.\n\nDo not run the installer from the zip as it wont work!",
                    "Please Extract Installation zip",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                Logger.Warn("Files missing from Installation Directory");

                Environment.Exit(0);

                return;
            }

            new Action(async () =>
            {
                await Task.Delay(1).ConfigureAwait(false);

                if (((App)Application.Current).Arguments.Length > 0)
                {
                    if(IsAutoUpdate() && !IsSilentServer())
                    {
                        Application.Current.Dispatcher?.Invoke(() =>
                            {
                                Logger.Info("Silent Installer Running");
                                var result = MessageBox.Show(
                                    "Do you want to make changes? \n\nYes - Pause install and make changes\n\nNo - Run with previous install path and install scripts as default. \n\nIf unsure - hit Yes!",
                                    "Change Installer Settings?",
                                    MessageBoxButton.YesNo, MessageBoxImage.Information);

                                if (result == MessageBoxResult.Yes)
                                {

                                }
                                else
                                {
                                    InstallScriptsCheckbox.IsChecked = true;
                                    InstallReleaseButton(null, null);
                                }

                                
                            }
                        ); //end-invoke
                    }
                    else if(IsAutoUpdate()&& IsSilentServer())
                    {
                        Application.Current.Dispatcher?.Invoke(() =>
                            {
                                var path = ServerPath();
                                Logger.Info("Silent Server Installer Running - "+path);

                                srPath.Text = path;
                                InstallScriptsCheckbox.IsChecked = false;
                                InstallReleaseButton(null, null);
                            }
                        ); //end-invoke
                    }
                }

            }).Invoke();
        }

        private bool IsAutoUpdate()
        {
            foreach (var commandLineArg in Environment.GetCommandLineArgs())
            {
                if (commandLineArg.Trim().Equals("-autoupdate"))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsSilentServer()
        {
            foreach (var commandLineArg in Environment.GetCommandLineArgs())
            {
                if (commandLineArg.Trim().Equals("-server"))
                {
                    return true;
                }
            }

            return false;
        }

        private string ServerPath()
        {
            foreach (var commandLineArg in Environment.GetCommandLineArgs())
            {
                if (commandLineArg.Trim().StartsWith("-path="))
                {
                    var line = commandLineArg.Trim();
                    line = line.Replace("-path=","");

                    return line;
                }
            }

            return "";
        }

        private bool ShouldRestart()
        {
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg.Trim().Equals("-restart"))
                {
                    return true;
                }

            }

            return false;
        }

        private bool CheckExtracted()
        {
            return File.Exists(_currentDirectory + "\\opus.dll") 
                   && File.Exists(_currentDirectory + "\\speexdsp.dll")
                   && File.Exists(_currentDirectory + "\\WebRtcVad.dll")
                   && File.Exists(_currentDirectory + "\\libmp3lame.64.dll")
                   && File.Exists(_currentDirectory + "\\libmp3lame.32.dll")
                   && File.Exists(_currentDirectory + "\\awacs-radios.json")
                   && File.Exists(_currentDirectory + "\\SR-ClientRadio.exe")
                   && File.Exists(_currentDirectory + "\\Scripts\\DCS-SRS\\Scripts\\DCS-SimpleRadioStandalone.lua");
        }


        private void SetupLogging()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string logFilePath = Path.Combine(baseDirectory, "installer-log.txt");
            string oldLogFilePath = Path.Combine(baseDirectory, "install-log.old.txt");

            FileInfo logFileInfo = new FileInfo(logFilePath);
            // Cleanup logfile if > 100MB, keep one old file copy
            if (logFileInfo.Exists && logFileInfo.Length >= 104857600)
            {
                if (File.Exists(oldLogFilePath))
                {
                    try
                    {
                        File.Delete(oldLogFilePath);
                    }
                    catch (Exception) { }
                }

                try
                {
                    File.Move(logFilePath, oldLogFilePath);
                }
                catch (Exception) { }
            }

            var config = new LoggingConfiguration();

            var fileTarget = new FileTarget();

            fileTarget.FileName = "${basedir}/installer-log.txt";
            fileTarget.Layout =
                @"${longdate} | ${logger} | ${message} ${exception:format=toString,Data:maxInnerExceptionLevel=2}";

            var wrapper = new AsyncTargetWrapper(fileTarget, 5000, AsyncTargetWrapperOverflowAction.Discard);
            config.AddTarget("file", wrapper);

#if DEBUG
            config.LoggingRules.Add( new LoggingRule("*", LogLevel.Debug, fileTarget));
#else
            config.LoggingRules.Add(new LoggingRule("*", LogLevel.Info, fileTarget));
#endif

            LogManager.Configuration = config;

        }

        private void ShowDCSWarning()
        {
           
                MessageBox.Show(
                    "DCS must now be closed before continuing the install or uninstall!\n\nClose DCS and please try again.",
                    "Please Close DCS",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            
        }

        private async void  InstallReleaseButton(object sender, RoutedEventArgs e)
        {

            var dcScriptsPath = dcsScriptsPath.Text;
            if ((bool)!InstallScriptsCheckbox.IsChecked)
            {
                dcScriptsPath = null;
            }
            else
            {
                var paths = FindValidDCSFolders(dcScriptsPath);

                if (paths.Count == 0)
                {
                    MessageBox.Show(
                        "Unable to find DCS Folder in Saved Games!\n\nPlease check the path to the \"Saved Games\" folder\n\nMake sure you are selecting the \"Saved Games\" folder - NOT the DCS folder inside \"Saved Games\" and NOT the DCS installation directory",
                        "SR Standalone Installer",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (IsDCSRunning())
                {
                    ShowDCSWarning();
                    return;
                }
            }

            InstallButton.IsEnabled = false;
            RemoveButton.IsEnabled = false;

            InstallButton.Content = "Installing...";

            _progressBarDialog = new ProgressBarDialog();
            _progressBarDialog.Owner = this;
            _progressBarDialog.Show();

            var srsPath = srPath.Text;
           
            var shortcut = CreateStartMenuShortcut.IsChecked ?? true;

            new Action(async () =>
            {
                int result = await Task.Run<int>(() => InstallRelease(srsPath,dcScriptsPath, shortcut));
                if (result == 0)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        {
                            InstallButton.IsEnabled = true;
                            RemoveButton.IsEnabled = true;
                            InstallButton.Content = "Install";
                        }
                    ); //end-invoke
                    _progressBarDialog.UpdateProgress(true, "Error");

                }
                else if (result == 1)
                {
                    _progressBarDialog.UpdateProgress(true, "Installed SRS Successfully!");

                    Logger.Info($"Installed SRS Successfully!");

                    //open to installation location
                    // Process.Start("explorer.exe", srPath.Text);
                    Environment.Exit(0);
                }
                else
                {
                    _progressBarDialog.UpdateProgress(true, "Error with Installation");

                    MessageBox.Show(
                        "Error with installation - please post your installer-log.txt on the SRS Discord for Support",
                        "Installation Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                
                    Process.Start("https://discord.gg/vqxAw7H");
                    Process.Start("explorer.exe", GetWorkingDirectory());
                    Environment.Exit(0);
                }
            }).Invoke();

        }

        private int InstallRelease(string srPath, string dcsScriptsPath, bool shortcut)
        {
            try
            {
                QuitSimpleRadio();

                var paths = new List<string>();
                if (dcsScriptsPath != null)
                {
                    paths = FindValidDCSFolders(dcsScriptsPath);

                    if (paths.Count == 0)
                    {

                        MessageBox.Show(
                            "Unable to find DCS Folder in Saved Games!\n\nPlease check the path to the \"Saved Games\" folder\n\nMake sure you are selecting the \"Saved Games\" folder - NOT the DCS folder inside \"Saved Games\" and NOT the DCS installation directory",
                            "SR Standalone Installer",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return 0;
                    }

                    if (IsDCSRunning())
                    {
                        MessageBox.Show(
                            "DCS must now be closed before continuing the installation!\n\nClose DCS and please try again.",
                            "Please Close DCS",
                            MessageBoxButton.OK, MessageBoxImage.Error);

                        Logger.Warn("DCS is Running - Installer stopped");

                        return 0;
                    }

                    Logger.Info($"Installing - Paths: \nProgram:{srPath} \nDCS:{dcsScriptsPath} ");

                    ClearVersionPreModsTechDCS(srPath, dcsScriptsPath);
                    ClearVersionPostModsTechDCS(srPath, dcsScriptsPath);
                    ClearVersionPostModsServicesDCS(srPath, dcsScriptsPath);

                    foreach (var path in paths)
                    {
                        InstallScripts(path);
                    }
                }
                else
                {
                    Logger.Info($"Installing - Paths: \nProgram:{srPath} DCS: NO PATH - NO SCRIPTS");
                }

                //install program
                InstallProgram(srPath);

                WritePath(srPath, "SRPathStandalone");

                if(dcsScriptsPath!=null)
                    WritePath(dcsScriptsPath, "ScriptsPath");

                if (shortcut)
                {
                    InstallShortcuts(srPath);
                }

                InstallVCRedist();

                if (dcsScriptsPath != null)
                {
                    string message = "Installation / Update Completed Successfully!\nInstalled DCS Scripts to: \n";

                    foreach (var path in paths)
                    {
                        message += ("\n" + path);
                    }

                    MessageBox.Show(message, "SR Standalone Installer",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    if (IsSilentServer())
                    {
                        if (ShouldRestart())
                        {
                            StartServer(srPath);
                            return 1;
                        }
                    }
                    else
                    {
                        string message = "Installation / Update Completed Successfully!";

                        MessageBox.Show(message, "SR Standalone Installer",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    
                }
                    

                return 1;

            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error Running Installer");

            
                return -1;
            }
        }

        private void StartServer(string path)
        {
            Logger.Info($"Starting SRS Server - Paths: \nProgram:{path} ");
            ProcessStartInfo procInfo = new ProcessStartInfo
            {
                WorkingDirectory = path, 
                FileName = (path + "\\" + "sr-server.exe"), 
                UseShellExecute = false
            };
            Process.Start(procInfo);
        }

        private string GetWorkingDirectory()
        {
            return new FileInfo(Assembly.GetEntryAssembly().Location).Directory.ToString();
        }

        private void InstallVCRedist()
        {
            _progressBarDialog.UpdateProgress(false, $"Installing VC Redist x64");
            Process.Start(GetWorkingDirectory() + "\\VC_redist.x64.exe", "/install /norestart /quiet /log \"vc_redist_2017_x64.log\"");
            _progressBarDialog.UpdateProgress(false, $"Finished installing VC Redist x64");

        }

        static void ListFiles(string sDir)
        {
            try
            {
                foreach (string d in Directory.GetDirectories(sDir))
                {
                    foreach (string f in Directory.GetFiles(d))
                    {
                        Logger.Info(f);
                    }
                    ListFiles(d);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex,"Error listing files");
            }
        }


        private void ClearVersionPreModsTechDCS(string programPath, string dcsPath)
        {
            Logger.Info($"Removed previous SRS Version at {programPath} and {dcsPath}");
           
            var paths = FindValidDCSFolders(dcsPath);

            foreach (var path in paths)
            {
                _progressBarDialog.UpdateProgress(false, $"Clearing Previous SRS at  {path}");
                RemoveScriptsPreModsTechDCS(path + "\\Scripts");
            }

            Logger.Info($"Removed SRS program files at {programPath}");
            _progressBarDialog.UpdateProgress(false, $"Clearing Previous SRS at  {programPath}");
            if (Directory.Exists(programPath) && File.Exists(programPath + "\\SR-ClientRadio.exe"))
            {
                DeleteFileIfExists(programPath + "\\SR-ClientRadio.exe");
                DeleteFileIfExists(programPath + "\\opus.dll"); 
                DeleteFileIfExists(programPath + "\\speexdsp.dll");
                DeleteFileIfExists(programPath + "\\WebRtcVad.dll");
                DeleteFileIfExists(programPath + "\\awacs-radios.json");
                DeleteFileIfExists(programPath + "\\SRS-AutoUpdater.exe");
                DeleteFileIfExists(programPath + "\\SR-Server.exe");
                DeleteFileIfExists(programPath + "\\DCS-SR-ExternalAudio.exe");
                DeleteFileIfExists(programPath + "\\grpc_csharp_ext.x64.dll");
                DeleteFileIfExists(programPath + "\\libmp3lame.32.dll");
                DeleteFileIfExists(programPath + "\\libmp3lame.64.dll");
                DeleteFileIfExists(programPath + "\\DCS-SimpleRadioStandalone.lua");
                DeleteFileIfExists(programPath + "\\DCS-SRSGameGUI.lua");
                DeleteFileIfExists(programPath + "\\DCS-SRS-AutoConnectGameGUI.lua");
                DeleteFileIfExists(programPath + "\\DCS-SRS-OverlayGameGUI.lua");
                DeleteFileIfExists(programPath + "\\DCS-SRS-Overlay.dlg");
                DeleteFileIfExists(programPath + "\\serverlog.txt");
                DeleteFileIfExists(programPath + "\\clientlog.txt");
                DeleteFileIfExists(programPath + "\\DCS-SRS-hook.lua");
            }
            Logger.Info($"Finished clearing scripts and program Pre Mods ");

        }

        private void ClearVersionPostModsTechDCS(string programPath, string dcsPath)
        {
            Logger.Info($"Removed SRS Version Post Mods at {programPath} and {dcsPath}");
            
            var paths = FindValidDCSFolders(dcsPath);

            foreach (var path in paths)
            {
                _progressBarDialog.UpdateProgress(false, $"Removing SRS at {path}");
                RemoveScriptsPostModsTechDCS(path);
            }

            Logger.Info($"Removed SRS program files at {programPath}");
            _progressBarDialog.UpdateProgress(false, $"Removing SRS at {programPath}");
            if (Directory.Exists(programPath) && File.Exists(programPath + "\\SR-ClientRadio.exe"))
            {
                DeleteFileIfExists(programPath + "\\SR-ClientRadio.exe");
                DeleteFileIfExists(programPath + "\\DCS-SR-ExternalAudio.exe");
                DeleteFileIfExists(programPath + "\\grpc_csharp_ext.x64.dll");
                DeleteFileIfExists(programPath + "\\WebRtcVad.dll");
                DeleteFileIfExists(programPath + "\\libmp3lame.32.dll");
                DeleteFileIfExists(programPath + "\\libmp3lame.64.dll");
                DeleteFileIfExists(programPath + "\\opus.dll");
                DeleteFileIfExists(programPath + "\\speexdsp.dll");
                DeleteFileIfExists(programPath + "\\awacs-radios.json");
                DeleteFileIfExists(programPath + "\\SRS-AutoUpdater.exe");
                DeleteFileIfExists(programPath + "\\SR-Server.exe");
                DeleteFileIfExists(programPath + "\\serverlog.txt");
                DeleteFileIfExists(programPath + "\\clientlog.txt");

               
                DeleteDirectory(programPath + "\\Scripts");
            }
            Logger.Info($"Finished clearing scripts and program Post Mods ");
        }

        private void RemoveScriptsPostModsTechDCS(string path)
        {
            Logger.Info($"Removing SRS Scripts at {path}");
            //SCRIPTS folder
            if (File.Exists(path + "\\Scripts\\Export.lua"))
            {
                var contents = File.ReadAllText(path + "\\Scripts\\Export.lua");

                if (contents.Contains("SimpleRadioStandalone.lua"))
                {
                    var lines = contents.Split('\n');

                    StringBuilder sb = new StringBuilder();

                    foreach (var line in lines)
                    {
                        if (!line.Contains("SimpleRadioStandalone.lua") && line.Trim().Length > 0)
                        {
                            sb.Append(line);
                            sb.Append("\n");
                        }
                        else
                        {
                            Logger.Info($"Removed SRS Scripts from Export.lua");
                        }
                    }
                    File.WriteAllText(path + "\\Scripts\\Export.lua", sb.ToString());
                }
            }

            Logger.Info($"Removed Hooks file");
            //Hooks Folder
            DeleteFileIfExists(path + "\\Scripts\\Hooks\\DCS-SRS-Hook.lua");

            //MODs folder
            if (Directory.Exists(path+"\\Mods\\Tech\\DCS-SRS"))
            {
                Logger.Info($"Removed Mods/Tech/DCS-SRS folder");
                Directory.Delete(path+"\\Mods\\Tech\\DCS-SRS",true);
            }

            Logger.Info($"Finished Removing Mods/Tech & Scripts for SRS");
        }

        private void ClearVersionPostModsServicesDCS(string programPath, string dcsPath)
        {
            Logger.Info($"Removed SRS Version Post Mods Services at {programPath} and {dcsPath}");

            var paths = FindValidDCSFolders(dcsPath);

            foreach (var path in paths)
            {
                _progressBarDialog.UpdateProgress(false, $"Removing SRS at {path}");
                RemoveScriptsPostModsServicesDCS(path);
            }

            Logger.Info($"Removed SRS program files at {programPath}");
            _progressBarDialog.UpdateProgress(false, $"Removing SRS at {programPath}");
            if (Directory.Exists(programPath) && File.Exists(programPath + "\\SR-ClientRadio.exe"))
            {
                DeleteFileIfExists(programPath + "\\SR-ClientRadio.exe");
                DeleteFileIfExists(programPath + "\\DCS-SR-ExternalAudio.exe");
                DeleteFileIfExists(programPath + "\\grpc_csharp_ext.x64.dll");
                DeleteFileIfExists(programPath + "\\WebRtcVad.dll");
                DeleteFileIfExists(programPath + "\\libmp3lame.32.dll");
                DeleteFileIfExists(programPath + "\\libmp3lame.64.dll");
                DeleteFileIfExists(programPath + "\\opus.dll");
                DeleteFileIfExists(programPath + "\\speexdsp.dll");
                DeleteFileIfExists(programPath + "\\awacs-radios.json");
                DeleteFileIfExists(programPath + "\\SRS-AutoUpdater.exe");
                DeleteFileIfExists(programPath + "\\SR-Server.exe");
                DeleteFileIfExists(programPath + "\\serverlog.txt");
                DeleteFileIfExists(programPath + "\\clientlog.txt");

          
                DeleteDirectory(programPath + "\\Scripts");
            }
            Logger.Info($"Finished clearing scripts and program Post Mods ");
        }

        private void RemoveScriptsPostModsServicesDCS(string path)
        {
            Logger.Info($"Removing SRS Scripts at {path}");
            //SCRIPTS folder
            if (File.Exists(path + "\\Scripts\\Export.lua"))
            {
                var contents = File.ReadAllText(path + "\\Scripts\\Export.lua");

                if (contents.Contains("SimpleRadioStandalone.lua"))
                {
                    var lines = contents.Split('\n');

                    StringBuilder sb = new StringBuilder();

                    foreach (var line in lines)
                    {
                        if (!line.Contains("SimpleRadioStandalone.lua") && line.Trim().Length > 0)
                        {
                            sb.Append(line);
                            sb.Append("\n");
                        }
                        else
                        {
                            Logger.Info($"Removed SRS Scripts from Export.lua");
                        }
                    }
                    File.WriteAllText(path + "\\Scripts\\Export.lua", sb.ToString());
                }
            }

            Logger.Info($"Removed Hooks file");
            //Hooks Folder
            DeleteFileIfExists(path + "\\Scripts\\Hooks\\DCS-SRS-Hook.lua");

            //MODs folder
            if (Directory.Exists(path + "\\Mods\\Services\\DCS-SRS"))
            {
                Logger.Info($"Removed Mods/Services/DCS-SRS folder");
                Directory.Delete(path + "\\Mods\\Services\\DCS-SRS", true);
            }

            Logger.Info($"Finished Removing Mods/Services & Scripts for SRS");
        }

        private static string ReadPath(string key)
        {
            var srPath = (string) Registry.GetValue(REG_PATH,
                key,
                "");

            return srPath ?? "";
        }

        private static void WritePath(string path, string key)
        {
            Registry.SetValue(REG_PATH,
                key,
                path);
        }


        private static void DeleteRegKeys()
        {
            try
            {
                Registry.SetValue(REG_PATH,
                    "SRPathStandalone",
                    "");
                Registry.SetValue(REG_PATH,
                    "ScriptsPath",
                    "");
            }
            catch (Exception)
            {
            }

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey("SOFTWARE", true))
                {
                    key.DeleteSubKeyTree("DCS-SimpleRadioStandalone", false);
                    key.DeleteSubKeyTree("DCS-SR-Standalone", false);
                }
            }
            catch (Exception)
            {
            }
        }

        private void QuitSimpleRadio()
        {
            Logger.Info($"Closing SRS Client & Server");
#if DEBUG
            return;
#endif
            foreach (var clsProcess in Process.GetProcesses())
            {
                if (clsProcess.ProcessName.ToLower().Trim().StartsWith("sr-server") || clsProcess.ProcessName.ToLower().Trim().StartsWith("sr-client"))
                {
                    Logger.Info($"Found & Terminating {clsProcess.ProcessName}");
                    clsProcess.Kill();
                    clsProcess.WaitForExit(5000);
                    clsProcess.Dispose();

                    
                }
            }
            Logger.Info($"Closed SRS Client & Server");
        }

        private bool IsDCSRunning()
        {
            foreach (var clsProcess in Process.GetProcesses())
            {
                if (clsProcess.ProcessName.ToLower().Trim().Equals("dcs"))
                {
                    return true;
                    // bool suspended = true;
                    // foreach (var thread in clsProcess.Threads)
                    // {
                    //     var t = (System.Diagnostics.ProcessThread)thread;
                    //
                    //     if (t.ThreadState == ThreadState.Wait && t.WaitReason == ThreadWaitReason.Suspended)
                    //     {
                    //         Logger.Info($"DCS thread is suspended");
                    //     }
                    //     else
                    //     {
                    //         Logger.Info($"DCS thread is not suspended");
                    //         suspended = false;
                    //     }
                    // }
                    //
                    // return !suspended;
                }
            }

            return false;
        }

        private void GridPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }


        private void Set_Install_Path(object sender, RoutedEventArgs e)
        {
            var dlg = new FolderBrowserDialog();
            var result = dlg.ShowDialog();
            if (result.ToString() == "OK")
            {
                // Open document
                var filename = dlg.SelectedPath;

                if (!filename.EndsWith("\\"))
                {
                    filename = filename + "\\";
                }
                filename = filename + "DCS-SimpleRadio-Standalone\\";

                srPath.Text = filename;
            }
        }

        private void Set_Scripts_Path(object sender, RoutedEventArgs e)
        {
            var dlg = new FolderBrowserDialog();
            var result = dlg.ShowDialog();
            if (result.ToString() == "OK")
            {
                // Open document
                var filename = dlg.SelectedPath;

                if (!filename.EndsWith("\\"))
                {
                    filename = filename + "\\";
                }

                dcsScriptsPath.Text = filename;
            }
        }


        private static List<string> FindValidDCSFolders(string path)
        {
            Logger.Info($"Finding DCS Saved Games Path");
            var paths = new List<string>();

            if(path == null || path.Length == 0)
            {
                return paths;
            }

            foreach (var directory in Directory.EnumerateDirectories(path))
            {
                if (directory.ToUpper().Contains("DCS.") || directory.ToUpper().EndsWith("DCS"))
                {
                    //check for config/network.vault and options.lua
                    var network = directory + "\\config\\network.vault";
                    var config = directory + "\\config\\options.lua";
                    if (File.Exists(network) || File.Exists(config))
                    {
                        var split = directory.Split(Path.DirectorySeparatorChar);
                        if (!split.Last().ToUpper().Contains("SERVER") && !split.Last().ToUpper().Contains("DEDICATED"))
                        {
                            Logger.Info($"Found DCS Saved Games Path {directory}");
                            paths.Add(directory);
                        }
                        else
                        {
                            Logger.Info($"Found DCS Saved Games Path {directory} - Ignoring as its a Server path");
                        }
                        
                    }
                }
               
            }

            Logger.Info($"Finished Finding DCS Saved Games Path");

            return paths;
        }

        private static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception)
                {
                }
            }
        }

        private void InstallProgram(string path)
        {
            Logger.Info($"Installing SRS Program to {path}");
            _progressBarDialog.UpdateProgress(false, $"Installing SRS at {path}");
            //sleep! WTF directory is lagging behind state here...
            Task.Delay(TimeSpan.FromMilliseconds(200)).Wait();

            _progressBarDialog.UpdateProgress(false, $"Creating Directories at {path}");

            Logger.Info($"Creating Directories");
            CreateDirectory(path);
            CreateDirectory(path + "\\AudioEffects");
            CreateDirectory(path + "\\Scripts");

            //sleep! WTF directory is lagging behind state here...
            Task.Delay(TimeSpan.FromMilliseconds(200)).Wait();
            _progressBarDialog.UpdateProgress(false, $"Copying Program Files at {path}");

            Logger.Info($"Copying binaries");
            File.Copy(_currentDirectory + "\\opus.dll", path + "\\opus.dll", true);
            File.Copy(_currentDirectory + "\\speexdsp.dll", path + "\\speexdsp.dll", true);
            File.Copy(_currentDirectory + "\\awacs-radios.json", path + "\\awacs-radios.json", true);
            
            File.Copy(_currentDirectory + "\\SR-ClientRadio.exe", path + "\\SR-ClientRadio.exe", true);
            File.Copy(_currentDirectory + "\\SR-Server.exe", path + "\\SR-Server.exe", true);
            File.Copy(_currentDirectory + "\\SRS-AutoUpdater.exe", path + "\\SRS-AutoUpdater.exe", true);
            File.Copy(_currentDirectory + "\\DCS-SR-ExternalAudio.exe", path + "\\DCS-SR-ExternalAudio.exe", true);

            File.Copy(_currentDirectory + "\\libmp3lame.32.dll", path + "\\libmp3lame.32.dll", true);
            File.Copy(_currentDirectory + "\\libmp3lame.64.dll", path + "\\libmp3lame.64.dll", true);

            File.Copy(_currentDirectory + "\\grpc_csharp_ext.x64.dll", path + "\\grpc_csharp_ext.x64.dll", true);
            

            File.Copy(_currentDirectory + "\\WebRtcVad.dll", path + "\\WebRtcVad.dll", true);

            Logger.Info($"Copying directories");
            DirectoryCopy(_currentDirectory+"\\AudioEffects", path+"\\AudioEffects");
            DirectoryCopy(_currentDirectory + "\\Scripts", path + "\\Scripts");

            Logger.Info($"Finished installing SRS Program to {path}");

        }

        private void InstallShortcuts(string path)
        {
            Logger.Info($"Adding SRS Shortcut");
            string executablePath = Path.Combine(path, "SR-ClientRadio.exe");
            string shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "DCS-SRS Client.lnk");

            WshShell shell = new WshShell();
            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutPath);

            shortcut.Description = "DCS-SimpleRadio Standalone Client";
            shortcut.TargetPath = executablePath;
            shortcut.WorkingDirectory = path;
            shortcut.Save();
        }

        private void InstallScripts(string path)
        {
            Logger.Info($"Installing Scripts to {path}");
            _progressBarDialog.UpdateProgress(false, $"Creating Script folders @ {path}");
            //Scripts Path
            CreateDirectory(path+"\\Scripts");
            CreateDirectory(path+"\\Scripts\\Hooks");
            
            //Make Tech Path
            CreateDirectory(path+"\\Mods"); 
            CreateDirectory(path+"\\Mods\\Services");
            CreateDirectory(path+ "\\Mods\\Services\\DCS-SRS");

            Task.Delay(TimeSpan.FromMilliseconds(100)).Wait();

            _progressBarDialog.UpdateProgress(false, $"Updating / Creating Export.lua @ {path}");
            Logger.Info($"Handling Export.lua");
            //does it contain an export.lua?
            if (File.Exists(path + "\\Scripts\\Export.lua"))
            {
                var contents = File.ReadAllText(path + "\\Scripts\\Export.lua");

                contents.Split('\n');

                if (contents.Contains("SimpleRadioStandalone.lua"))
                {
                    Logger.Info($"Updating existing Export.lua with existing SRS install");
                    var lines = contents.Split('\n');

                    StringBuilder sb = new StringBuilder();

                    foreach (var line in lines)
                    {
                        if (line.Contains("SimpleRadioStandalone.lua") )
                        {
                            sb.Append("\n");
                            sb.Append(EXPORT_SRS_LUA);
                            sb.Append("\n");
                        }
                        else if(line.Trim().Length>0)
                        {
                            sb.Append(line);
                            sb.Append("\n");
                        }
                        
                    }
                    File.WriteAllText(path + "\\Scripts\\Export.lua", sb.ToString());
                }
                else
                {
                    Logger.Info($"Appending to existing Export.lua");
                    var writer = File.AppendText(path + "\\Scripts\\Export.lua");

                    writer.WriteLine("\n" + EXPORT_SRS_LUA + "\n");
                    writer.Close();
                }
            }
            else
            {
                Logger.Info($"Creating new Export.lua");
                var writer = File.CreateText(path + "\\Scripts\\Export.lua");

                writer.WriteLine("\n"+EXPORT_SRS_LUA+"\n");
                writer.Close();
            }


            //Now sort out Scripts//Hooks folder contents
            Logger.Info($"Creating / installing Hooks & Mods / Services");
            _progressBarDialog.UpdateProgress(false, $"Creating / installing Hooks & Mods/Services @ {path}");
            try
            {
                File.Copy(_currentDirectory + "\\Scripts\\Hooks\\DCS-SRS-hook.lua", path + "\\Scripts\\Hooks\\DCS-SRS-hook.lua",
                    true);

                DirectoryCopy(_currentDirectory + "\\Scripts\\DCS-SRS",path+"\\Mods\\Services\\DCS-SRS");
            }
            catch (FileNotFoundException)
            {
                MessageBox.Show(
                    "Install files not found - Unable to install! \n\nMake sure you extract all the files in the zip then run the Installer",
                    "Not Unzipped", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(0);
            }
            Logger.Info($"Scripts installed to {path}");

            _progressBarDialog.UpdateProgress(false, $"Installed Hooks & Mods/Services @ {path}");
        }

        public static void DeleteDirectory(string target_dir)
        {
            if (Directory.Exists(target_dir))
            {
                Directory.Delete(target_dir, true);
            }
        }

        private void DirectoryCopy(string sourceDirName, string destDirName)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
            {
                CreateDirectory(destDirName);
            }

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirName, file.Name);
                file.CopyTo(temppath, true);
            }

            // If copying subdirectories, copy them and their contents to new location.
            foreach (DirectoryInfo subdir in dirs)
            {
                string temppath = Path.Combine(destDirName, subdir.Name);
                DirectoryCopy(subdir.FullName, temppath);
            }
            
        }

        private async Task<bool> UninstallSR(string srPath, string dcsScriptsPath)
        {
            try
            {
                QuitSimpleRadio();
                Application.Current.Dispatcher.Invoke(() =>
                    {
                        InstallButton.IsEnabled = false;
                        RemoveButton.IsEnabled = false;

                        RemoveButton.Content = "Removing...";
                    }
                ); //end-invoke

                _progressBarDialog.UpdateProgress(false, $"Removing SRS");
                Logger.Info($"Removing - Paths: \nProgram:{srPath} \nDCS:{dcsScriptsPath} ");
                ClearVersionPreModsTechDCS(srPath, dcsScriptsPath);
                ClearVersionPostModsTechDCS(srPath, dcsScriptsPath);
                ClearVersionPostModsServicesDCS(srPath, dcsScriptsPath);


                DeleteRegKeys();

                RemoveShortcuts();

                return true;

            }
            catch (Exception ex) 
            {
                Logger.Error(ex, "Error Running Uninstaller");
            }

            return false;

        }

        private void RemoveShortcuts()
        {
            Logger.Info($"Removed SRS Shortcut");
            string shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "DCS-SRS Client.lnk");

            DeleteFileIfExists(shortcutPath);
        }

        private void RemoveScriptsPreModsTechDCS(string path)
        {
            Logger.Info($"Removing SRS Pre Mods Scripts at {path}");
            //does it contain an export.lua?
            if (File.Exists(path + "\\Export.lua"))
            {
                var contents = File.ReadAllText(path + "\\Export.lua");

                if (contents.Contains("SimpleRadioStandalone.lua"))
                {
                    Logger.Info($"Removed SRS from Export.lua");
                    contents = contents.Replace("dofile(lfs.writedir()..[[Scripts\\DCS-SimpleRadioStandalone.lua]])",
                        "");
                    contents =
                        contents.Replace(
                            "local dcsSr=require('lfs');dofile(dcsSr.writedir()..[[Scripts\\DCS-SimpleRadioStandalone.lua]])",
                            "");
                    contents = contents.Trim();

                    File.WriteAllText(path + "\\Export.lua", contents);
                }
            }

            DeleteFileIfExists(path + "\\DCS-SimpleRadioStandalone.lua");
            DeleteFileIfExists(path + "\\DCS-SRSGameGUI.lua");
            DeleteFileIfExists(path + "\\DCS-SRS-AutoConnectGameGUI.lua");
            DeleteFileIfExists(path + "\\DCS-SRS-Overlay.dlg");
            DeleteFileIfExists(path + "\\DCS-SRS-OverlayGameGUI.lua");
            DeleteFileIfExists(path + "\\Hooks\\DCS-SRS-Hook.lua");

            Logger.Info($"Removed all SRS Scripts at {path}");
        }

        private void CreateDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                var dir = Directory.CreateDirectory(path);

                dir.Refresh();
                //sleep! WTF directory is lagging behind state here...
                Task.Delay(TimeSpan.FromMilliseconds(200)).Wait();
                try
                {
                    var dSecurity = dir.GetAccessControl();
                    if (WindowsIdentity.GetCurrent().Owner != null)
                    {
                        dSecurity.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().Owner, FileSystemRights.Modify,
                            InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit,
                            PropagationFlags.NoPropagateInherit, AccessControlType.Allow));
                    }
                    if (WindowsIdentity.GetCurrent().User != null)
                    {
                        dSecurity.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.Modify,
                            InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit,
                            PropagationFlags.NoPropagateInherit, AccessControlType.Allow));
                    }
                    dir.SetAccessControl(dSecurity);
                    dir.Refresh();
                }
                catch(Exception ex){
                    Logger.Warn($"Unable to set permissions on path ${path}",ex);
                }
                

            }

            //sometimes it says directory created and its not!
            do 
            { 
                Task.Delay(TimeSpan.FromMilliseconds(50)).Wait();
            } while(!Directory.Exists(path));
            Task.Delay(TimeSpan.FromMilliseconds(100)).Wait();
        }


        private async void Remove_Plugin(object sender, RoutedEventArgs e)
        {

            if (!Directory.Exists(dcsScriptsPath.Text))
            {
                dcsScriptsPath.Text = "";
                Logger.Info($"SRS Scripts path not valid - ignoring uninstall of scripts: {dcsScriptsPath.Text}");
            }
            else
            {
                if (IsDCSRunning())
                {
                    ShowDCSWarning();
                    return;
                }
            }

            _progressBarDialog = new ProgressBarDialog();
            _progressBarDialog.Owner = this;
            _progressBarDialog.Show();
            _progressBarDialog.UpdateProgress(false, "Uninstalling SRS");

            var result = await UninstallSR(srPath.Text,dcsScriptsPath.Text);
            if (result)
            {
                _progressBarDialog.UpdateProgress(true, "Removed SRS Successfully!");
                Logger.Info($"Removed SRS Successfully!");

                MessageBox.Show(
                    "SR Standalone Removed Successfully!\n\nContaining folder left just in case you want favourites or frequencies",
                    "SR Standalone Installer",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                _progressBarDialog.UpdateProgress(true, "Error with Uninstaller");
                MessageBox.Show(
                    "Error with uninstaller - please post your installer-log.txt on the SRS Discord for Support",
                    "Installation Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                Process.Start("https://discord.gg/vqxAw7H");

                Process.Start("explorer.exe", GetWorkingDirectory());

            }
            Environment.Exit(0);
        }

        private void InstallScriptsCheckbox_OnChecked(object sender, RoutedEventArgs e)
        {
            dcsScriptsPath.IsEnabled = true;

        }

        private void InstallScriptsCheckbox_OnUnchecked(object sender, RoutedEventArgs e)
        {
            dcsScriptsPath.IsEnabled = false;

        }
    }
}
