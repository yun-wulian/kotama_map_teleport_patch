using BepInEx;
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
    public const string PluginVersion = "0.1.8";

    internal static ManualLogSource LogSource;
    internal static MapTeleportPlugin Instance;
    internal static MapTeleportRunner Runner;

    public override void Load()
    {
        LogSource = Log;
        Instance = this;

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
