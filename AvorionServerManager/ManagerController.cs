﻿using Microsoft.Owin.Hosting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using AvorionServerManager.Server;
using AvorionServerManager.Core;
using AvorionServerManager.Commands;
namespace AvorionServerManager
{
    public class ManagerController : ILoggable
    {
        #region private Variables
        private Process _process;
        private System.Timers.Timer _backupTimer;
        BackupController _backupController;
        #endregion

        #region public Variables
        public List<AvorionServerCommandDefinition> AvailableCommandDefinitions { get; private set;}
        public bool ServerProcessRunning { get; private set; }
        public DateTime LastUpdateTick { get; private set; }
        public AvorionServerSettings ServerSettings { get; private set; }
        public BackupSettings BackupSettings { get; set;}
        public bool ServerSettingLoadedFromFile { get; set; }
        public bool BackupSettingsLoadedFromFile { get; set; }
        public bool ManagerSettingsLoadedFromFile { get; set; }
        public ManagerSettings ManagerSettings;
        public AvorionServer Server { get; private set; }//This will change preferably so that multiple server/profiles are possible
        #endregion

        #region Public Eventing
        //TODO get rid of this
        public delegate void ServerStopped(object sender, ServerStoppedEventArgs e);
        public event ServerStopped ServerStoppedEvent;

        public event LogEvent LogEvent;
        #endregion
        public ManagerController()
        {
            ManagerSettingsLoadedFromFile = false;
            ManagerSettings = new ManagerSettings();
            _backupController = new BackupController();
            _LoadCommandDefinitions();
            _LoadSettings();
            ServerSettings = new AvorionServerSettings();
            ServerSettingLoadedFromFile = false;
            LoadServerSettings();
            BackupSettings = new BackupSettings();
            BackupSettingsLoadedFromFile = false;
            LoadBackupSettings();
            
        }
        private void LoadBackupSettings()
        {
            if (!Directory.Exists(Constants.SettingsFolderName))
            {
                Directory.CreateDirectory(Constants.SettingsFolderName);
            }
            else
            {
                if (File.Exists(Path.Combine(Constants.SettingsFolderName, Constants.BackupSettingsFileName)))
                {
                    using (StreamReader file = File.OpenText(Path.Combine(Constants.SettingsFolderName, Constants.BackupSettingsFileName)))
                    {
                        JsonSerializer serializer = new JsonSerializer();
                        BackupSettings = (BackupSettings)serializer.Deserialize(file, typeof(BackupSettings));
                        BackupSettingsLoadedFromFile = true;
                    }
                }
            }
        }
        private void LoadServerSettings()
        {
            if (!Directory.Exists(Constants.SettingsFolderName))
            {
                Directory.CreateDirectory(Constants.SettingsFolderName);
            }
            else
            {
                if (File.Exists(Path.Combine(Constants.SettingsFolderName, Constants.AvorionServerSettingsFileName)))
                {
                    using (StreamReader file = File.OpenText(Path.Combine(Constants.SettingsFolderName, Constants.AvorionServerSettingsFileName)))
                    {
                        JsonSerializer serializer = new JsonSerializer();
                        ServerSettings = (AvorionServerSettings)serializer.Deserialize(file, typeof(AvorionServerSettings));
                        ServerSettingLoadedFromFile = true;
                    }
                }
            }
        }
        private void _LoadCommandDefinitions()
        {
            AvailableCommandDefinitions = new List<AvorionServerCommandDefinition>();
            foreach (AvorionServerCommandDefinition currentCommand in CommandLoader.GetCommands())
            {
                AvailableCommandDefinitions.Add(currentCommand);
            }
        }
        private void _LoadSettings()
        {
            if (!Directory.Exists(Constants.SettingsFolderName))
            {
                Directory.CreateDirectory(Constants.SettingsFolderName);
            }
            else
            {
                if (File.Exists(Path.Combine(Constants.SettingsFolderName, Constants.ManagerSettingsFileName)))
                {
                    using (StreamReader file = File.OpenText(Path.Combine(Constants.SettingsFolderName, Constants.ManagerSettingsFileName)))
                    {
                        JsonSerializer serializer = new JsonSerializer();
                        ManagerSettings = (ManagerSettings)serializer.Deserialize(file, typeof(ManagerSettings));
                        ManagerSettingsLoadedFromFile = true;
                    }
                }
            }
        }
        public string GetAvorionFolder()
        {
            return ManagerSettings.AvorionFolder;
        }
        public int GetHttpServerPort()
        {
            return ManagerSettings.HttpServerPort;
        }
        public void SaveManagerSettings()
        {
            File.WriteAllText(Path.Combine(Constants.SettingsFolderName, Constants.ManagerSettingsFileName), JsonConvert.SerializeObject(ManagerSettings, Formatting.Indented));
            if(!string.IsNullOrWhiteSpace(ManagerSettings.AvorionFolder))
            CreateSteamCmdScriptFiles();
        }
        private void CreateSteamCmdScriptFiles()
        {
            ManagerSettings.SteamCmdScritpsFolder = Path.Combine(ManagerSettings.AvorionFolder, Constants.SteamCmdScriptsFolderName);
            if (!Directory.Exists(ManagerSettings.SteamCmdScritpsFolder))
            {
                Directory.CreateDirectory(ManagerSettings.SteamCmdScritpsFolder);
            }
            List<string> steamCmdScriptLines = new List<string>();
            steamCmdScriptLines.Add("login anonymous");
            steamCmdScriptLines.Add("force_install_dir \"" + ManagerSettings.AvorionFolder + "\"");
            steamCmdScriptLines.Add("app_update 565060");
            steamCmdScriptLines.Add("quit");

            List<string> steamCmdUpdateBetaScript = new List<string>();
            steamCmdUpdateBetaScript.Add("login anonymous");
            steamCmdUpdateBetaScript.Add("force_install_dir \"" + ManagerSettings.AvorionFolder + "\"");
            steamCmdUpdateBetaScript.Add("app_update 565060 -beta beta validate");
            steamCmdUpdateBetaScript.Add("quit");
            File.WriteAllLines(Path.Combine(ManagerSettings.SteamCmdScritpsFolder, Constants.SteamCmdInstallScriptFileName), steamCmdScriptLines);
            File.WriteAllLines(Path.Combine(ManagerSettings.SteamCmdScritpsFolder, Constants.SteamCmdBetaUpdateScriptFileName), steamCmdUpdateBetaScript);
        }
        public void SetAvorionFolder(string path)
        {
            ManagerSettings.AvorionFolder = path;
        }
        public void SetHttpServerPort(int port)
        {
            ManagerSettings.HttpServerPort = port;
        }
        public void StartWebApi()
        {
            WebApp.Start<Startup>(url: "https://*:" + ManagerSettings.HttpServerPort + "/AvorionServerManager/");
            
        }
        public void AddCommand(AvorionServerCommand command)
        {
            // CommandsApiData.AddCommand(command);
            Server.SendCommand(command);
        }
        private bool CheckConfigs()
        {
            if (string.IsNullOrWhiteSpace(ManagerSettings.AvorionFolder))
            {
                MessageBox.Show("Avorion Folder in Manager Settings not set");
                return false;
            }
            if (string.IsNullOrWhiteSpace(ServerSettings.DataPath))
            {
                MessageBox.Show("Data path in Server Settings not set");
                return false;
            }
            return true;
        }
        public void CreateBackup()
        {
            _backupController.CreateBackup(ServerSettings.DataPath, BackupSettings);
        }
        public void StartAvorionServer()
        {
            //TODO move all logic to Server.Start()
            string tmpAvorionServerExe = Path.Combine(ManagerSettings.AvorionFolder, "bin", "AvorionServer.exe");
            if (Server == null)
            {
                Server = new AvorionServer(ServerSettings, ManagerSettings.AvorionFolder, tmpAvorionServerExe);
            }
            if (!ServerProcessRunning&& CheckConfigs())
            {
                if (BackupSettings.SaveOnStartup)
                {
                        _backupController.CreateBackup(ServerSettings.DataPath, BackupSettings);
                  
                }
                if (BackupSettings.BackupInterval > 0)
                {
                    _backupTimer = new System.Timers.Timer(BackupSettings.BackupInterval * 60 * 1000);//Backupinterval in ms
                    _backupTimer.AutoReset = false;
                    _backupTimer.Enabled = true;
                    _backupTimer.Elapsed += _backupTimer_Elapsed;
                    _backupTimer.Start();
                }
               
                if (File.Exists(tmpAvorionServerExe))
                {
                    ServerProcessRunning = true;
                    Server.Start();
                    _process = Server.Process;
                     Thread listenThread = new Thread(ProcessOutputListenerLoop);
                    listenThread.IsBackground = true;
                    listenThread.Start();
                }
                else
                {
                    MessageBox.Show("Can not find File: "+tmpAvorionServerExe+Environment.NewLine+ "Please make sure the server is installed correctly and the Avorion Folder in the Manager Settings is correct. Use the Help in the Tools section for Instructions.");
                    ServerStoppedEvent(this, new ServerStoppedEventArgs());
                }
            }else
            {
                ServerStoppedEvent(this, new ServerStoppedEventArgs());
            }
        }

        private void _backupTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            _backupController.CreateBackup(ServerSettings.DataPath, BackupSettings);
            _backupTimer.Start();
        }

        private void Log(string entry)
        {
            LogEvent(this, new LogEventEventArgs(LogLevel.Undefined,LogHelper.StandardLogMessage(DateTime.Now,"Server stopped")));
          
        }
        private void ProcessCommandRequest()
        {
            AvorionServerCommand tmpCommand = CommandsApiData.DequeueCommand();
            if (tmpCommand != null)
            {
                Server.SendCommand(tmpCommand);
            }
            else
            {
                _process.StandardInput.WriteLine(-1);
            }
        }
        private void ProcessOutputListenerLoop()
        {
            while (IsRunning(_process))
            {
                string message = _process.StandardOutput.ReadLine();
                if (message != null)
                {
                    if (message.Contains(Constants.CommandRequest))
                    {
                        ProcessCommandRequest();
                    }
                    else if (message.Contains(Constants.UpdateTickIdentifier))
                    {
                        LastUpdateTick = DateTime.Now;
                    }
                    else
                    {
                        LogEvent(this, new LogEventEventArgs(LogLevel.Undefined, LogHelper.StandardLogMessage(DateTime.Now, message)));
                    }
                }
            }
            ServerStoppedEvent(this, new ServerStoppedEventArgs());
            if (_backupTimer != null)
            {
                _backupTimer.Stop();
                _backupTimer.Elapsed -= _backupTimer_Elapsed;
                _backupTimer.Dispose();
            }
            if (BackupSettings.SaveOnStop)
            {
                _backupController.CreateBackup(ServerSettings.DataPath, BackupSettings);
            }
            ServerProcessRunning = false;
            Server.IsRunning = false;
        }
        private void ProcessErrorListenerLoop()
        {
            while (IsRunning(_process))
            {
                string message = _process.StandardError.ReadLine();
                if (message != null)
                {
                    LogEvent(this, new LogEventEventArgs(LogLevel.Undefined, LogHelper.StandardLogMessage(DateTime.Now, message)));
                }
            }
        }
       
        public static bool IsRunning(Process process)
        {
            try { Process.GetProcessById(process.Id); }
            catch (InvalidOperationException) { return false; }
            catch (ArgumentException) { return false; }
            return true;
        }
        public void PatchServerLua()
        {
            LuaFilePatcher tmpPatcher = new LuaFilePatcher(AvailableCommandDefinitions);
            List<string> patchedLines = tmpPatcher.PatchServerLua(File.ReadAllLines(Path.Combine(ManagerSettings.AvorionFolder, "data", "scripts", "server", Constants.ServerLuaFileName)).ToList());
            File.WriteAllLines(Path.Combine(ManagerSettings.AvorionFolder, "data", "scripts", "server", Constants.ServerLuaFileName), patchedLines);
        }
        public void CleanServerLua()
        {
                List<string> cleanedLines = LuaFilePatcher.RemoveAsmPatches(File.ReadAllLines(Path.Combine(ManagerSettings.AvorionFolder, "data", "scripts", "server", Constants.ServerLuaFileName)).ToList());
                File.WriteAllLines(Path.Combine(ManagerSettings.AvorionFolder, "data", "scripts", "server", Constants.ServerLuaFileName), cleanedLines);
        }
        public void SaveServerSettings()
        {
            
            File.WriteAllText(Path.Combine(Constants.SettingsFolderName, Constants.AvorionServerSettingsFileName), JsonConvert.SerializeObject(ServerSettings, Formatting.Indented));
        }
        public void SaveBackupSettings()
        {
            File.WriteAllText(Path.Combine(Constants.SettingsFolderName, Constants.BackupSettingsFileName), JsonConvert.SerializeObject(BackupSettings, Formatting.Indented));
        }
        public string GenerateSeed(int length)
        {
            string charset = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            StringBuilder seedBuilder = new StringBuilder(length);
            Random random = new Random();
            for(int i =0; i < length; i++)
            {
                int tmpCharIndex = random.Next(0, charset.Length);
                seedBuilder.Append(charset[tmpCharIndex]);
            }
            return seedBuilder.ToString();
        }
    }
}
