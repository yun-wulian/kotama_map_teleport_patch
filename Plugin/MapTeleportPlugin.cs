using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace Kotama.MapTeleport;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class MapTeleportPlugin : BasePlugin
{
    public const string PluginGuid = "com.yunwulian.kotama.map-teleport";
    public const string PluginName = "Kotama Map Teleport";
    public const string PluginVersion = "0.2.0";

    internal static ManualLogSource LogSource;
    internal static MapTeleportPlugin Instance;
    internal static MapTeleportRunner Runner;

    internal static bool DebugLogsEnabled;
    internal static bool DebugCandidateModulesEnabled;
    internal static int DebugCandidateModulesLimit;

    private ConfigEntry<bool> _cfgDebugLogsEnabled;
    private ConfigEntry<bool> _cfgDebugCandidateModulesEnabled;
    private ConfigEntry<int> _cfgDebugCandidateModulesLimit;

    public override void Load()
    {
        LogSource = Log;
        Instance = this;

        _cfgDebugLogsEnabled = Config.Bind(
            "Debug",
            "EnableDebugLogs",
            false,
            "Enable extra debug logs (for investigating incorrect teleport destinations).");

        _cfgDebugCandidateModulesEnabled = Config.Bind(
            "Debug",
            "LogCandidateModules",
            false,
            "When enabled, logs all candidate MapMarkObjectModule entries for the selected map mark id on each teleport attempt.");

        _cfgDebugCandidateModulesLimit = Config.Bind(
            "Debug",
            "CandidateModulesLimit",
            16,
            "Max candidate modules to print per teleport attempt (sorted by distance).");

        DebugLogsEnabled = _cfgDebugLogsEnabled.Value;
        DebugCandidateModulesEnabled = _cfgDebugCandidateModulesEnabled.Value;
        DebugCandidateModulesLimit = Mathf.Clamp(_cfgDebugCandidateModulesLimit.Value, 0, 128);

        Runner = AddComponent<MapTeleportRunner>();

        Harmony harmony = new(PluginGuid);
        harmony.PatchAll(typeof(MapTeleportPlugin).Assembly);

        Log.LogInfo($"Loaded v{PluginVersion}. Map cursor teleport patch active.");
    }
}

public sealed class MapTeleportRunner : MonoBehaviour
{
    public MapTeleportRunner(System.IntPtr pointer) : base(pointer)
    {
    }
}
