﻿using System.Globalization;
using Advanced_Combat_Tracker;
using FFXIV_ACT_Plugin;
using FFXIV_ACT_Plugin.Common;
using FFXIV_ACT_Plugin.Config;
using FFXIV_ACT_Plugin.Logfile;
using FFXIV_ACT_Plugin.Memory;
using FFXIV_ACT_Plugin.Memory.MemoryReader;
using FFXIV_ACT_Plugin.Parse;
using FFXIV_ACT_Plugin.Resource;
using Machina.FFXIV;
using Machina.FFXIV.Headers.Opcodes;
using Microsoft.MinIoC;
using ACTWrapper = FFXIV_ACT_Plugin.Common.ACTWrapper;

namespace IINACT;

public class FfxivActPluginWrapper {
    public IDataSubscription Subscription;
    public IDataRepository Repository;
    public ParseSettings ParseSettings = null!;
    public DataCollectionSettingsEventArgs DataCollectionSettings = null!;
    public readonly ProcessManager ProcessManager;

    private readonly FFXIV_ACT_Plugin.FFXIV_ACT_Plugin _ffxivActPlugin;
    private readonly ParseMediator _parseMediator;
    private Configuration _configuration;

    public FfxivActPluginWrapper(int targetPid, Configuration configuration) {
        _configuration = configuration;
        _ffxivActPlugin = new FFXIV_ACT_Plugin.FFXIV_ACT_Plugin();
        _ffxivActPlugin.ConfigureIOC();
        OpcodeManager.Instance.SetRegion(GameRegion.Global);

        var iocContainer = _ffxivActPlugin._iocContainer;
        iocContainer.Resolve<ResourceManager>().LoadResources();

        Subscription = iocContainer.Resolve<DataSubscription>();
        _ffxivActPlugin.SetProperty("DataSubscription", Subscription);

        _parseMediator = iocContainer.Resolve<ParseMediator>();

        _ffxivActPlugin._dataCollection = iocContainer.Resolve<DataCollection>();

        var scanPackets = _ffxivActPlugin._dataCollection._scanPackets;
        ProcessManager = scanPackets.GetField<ProcessManager>("_processManager");

        SetupActWrapper();

        SetupDataSubscription();

        SetupSettingsMediator(targetPid);

        Repository = iocContainer.Resolve<IDataRepository>();
        _ffxivActPlugin.SetProperty("DataRepository", Repository);
        _ffxivActPlugin._dataCollection.StartMemory();

        ActGlobals.oFormActMain.BeforeLogLineRead += OFormActMain_BeforeLogLineRead;
    }


    private void SetupSettingsMediator(int targetPid)
    {
        var settingsMediator = _ffxivActPlugin._dataCollection._settingsMediator;

        DataCollectionSettings = new DataCollectionSettingsEventArgs {
            LogFileFolder = ActGlobals.oFormActMain.LogFilePath,
            UseSocketFilter = false,
            UseWinPCap = false,
            UseDeucalion= true,
            ProcessID = targetPid
        };
        settingsMediator.DataCollectionSettings = DataCollectionSettings;

        var readProcesses = ProcessManager.GetField<ReadProcesses>("_readProcesses");
        //wait for game
        while (!readProcesses.Read64(true).Exists(pid => targetPid == 0 || pid == targetPid))
            Thread.Sleep(500);

        ParseSettings = new ParseSettings() {
            DisableDamageShield = _configuration.DisableDamageShield,
            DisableCombinePets = _configuration.DisableCombinePets,
            LanguageID = (Language)_configuration.Language,
            ParseFilter = (ParseFilterMode)_configuration.ParseFilterMode,
            SimulateIndividualDoTCrits = _configuration.SimulateIndividualDoTCrits,
            ShowRealDoTTicks = _configuration.ShowRealDoTTicks,
            ShowDebug = _configuration.ShowDebug,
            EnableBenchmarks = false
        };
        settingsMediator.ParseSettings = ParseSettings;

        settingsMediator.ProcessException = OnProcessException;

        var logOutput = _ffxivActPlugin._dataCollection._logOutput;
        var logFormat = _ffxivActPlugin._dataCollection._logFormat;

        var line = logFormat.FormatParseSettings(ParseSettings.DisableDamageShield, ParseSettings.DisableCombinePets, ParseSettings.LanguageID, ParseSettings.ParseFilter, ParseSettings.SimulateIndividualDoTCrits, ParseSettings.ShowRealDoTTicks);
        logOutput.WriteLine(LogMessageType.Settings, DateTime.MinValue, line);

        var line2 = logFormat.FormatMemorySettings(DataCollectionSettings.ProcessID, DataCollectionSettings.LogFileFolder, DataCollectionSettings.LogAllNetworkData, DataCollectionSettings.DisableCombatLog, DataCollectionSettings.NetworkIP, DataCollectionSettings.UseWinPCap, DataCollectionSettings.UseSocketFilter, DataCollectionSettings.UseDeucalion);
        logOutput.WriteLine(LogMessageType.Settings, DateTime.MinValue, line2);

        logOutput.CallMethod("ConfigureLogFile", null);
        ActGlobals.oFormActMain.GetDateTimeFromLog = _parseMediator.ParseLogDateTime;

        ProcessManager.Verify();
    }

    private void SetupActWrapper() {
        var logOutput = _ffxivActPlugin._dataCollection._logOutput;
        var actWrapper = logOutput.GetField<ACTWrapper>("_actWrapper");

        actWrapper.TimeStampLen = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture).Length + 3;
        actWrapper.LogPathHasCharName = false;
        actWrapper.OverrideMainFormVisible = true;

        ACT_UIMods.UpdateACTTables(false);

        ActGlobals.oFormActMain.FfxivPlugin = _ffxivActPlugin;
    }

    private void OFormActMain_BeforeLogLineRead(bool isImport, LogLineEventArgs logInfo) {
        (logInfo.logLine, logInfo.detectedType) = _parseMediator.BeforeLogLineRead(isImport, logInfo.detectedTime, logInfo.logLine);
    }

    private void SetupDataSubscription() {
        _ffxivActPlugin.DataSubscription.ZoneChanged += OnZoneChanged;
    }

    private static void OnZoneChanged(uint ZoneID, string ZoneName) {
        ActGlobals.oFormActMain.ChangeZone(ZoneName);
    }

    private static void OnProcessException(DateTime timestamp, string text) {
    }

}
