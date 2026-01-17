using System;
using System.Collections;
using EscapeGame;
using EscapeGame.UI;
using EscapeGame.UI.Controls;
using EscapeGame.UIGen;
using Game.Core;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Il2CppSystem.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kotama.MapTeleport.Patches;

internal static class MapTeleportCore
{
    private sealed class PendingTeleport
    {
        public InGameMenuMapPanelCtrl Menu;
        public string Source;
        public string ActiveSceneName;
        public string SceneName;
        public Vector3 TargetPos;
        public bool IsTemporarySavePoint;
        public MapMarkObjectModule Module;
        public float PreTeleportTimeScale;
    }

    private static void TryCloseMenu(InGameMenuMapPanelCtrl menu, string phase)
    {
        if (menu == null)
        {
            return;
        }

        // Prefer the same cancel flow as the game (it typically restores input/pause state),
        // but also call Hide() as a fallback so the map UI actually disappears.
        try
        {
            menu.OnBtnCancel();
        }
        catch (Exception ex)
        {
            MapTeleportPlugin.LogSource?.LogWarning($"[MapTeleport] menu close({phase}) via OnBtnCancel failed: {ex}");
        }

        try
        {
            menu.Hide();
        }
        catch (Exception ex)
        {
            MapTeleportPlugin.LogSource?.LogWarning($"[MapTeleport] menu close({phase}) via Hide failed: {ex}");
        }

        // The map page is part of the "InGameMenu" container UI. Closing only the map panel can leave the top bar
        // (tabs, Q/E hints) visible. Hide the related InGameMenu panels via the global UI manager.
        TryCloseInGameMenuPanels(phase);
    }

    private static void TryCloseInGameMenuPanels(string phase)
    {
        IUIManager ui = null;
        try
        {
            ui = UIMgrWrap.Ins;
        }
        catch (Exception ex)
        {
            MapTeleportPlugin.LogSource?.LogWarning($"[MapTeleport] UIMgrWrap.Ins not available ({phase}): {ex}");
            return;
        }

        if (ui == null)
        {
            return;
        }

        // IMPORTANT:
        // Only hide the InGameMenu container panels (topbar + map) here.
        // Hiding other panels can accidentally hide gameplay HUD and require an ESC toggle to recover.
        try { ui.HideUISync<InGameMenuTopBarPanel>(); } catch { }
        try { ui.HideUISync<InGameMenuMapPanel>(); } catch { }
    }

    private static void TryUnfreezeUI(string phase)
    {
        try
        {
            IUIManager ui = UIMgrWrap.Ins;
            if (ui == null)
            {
                return;
            }

            // When closing InGameMenu via manual panel hiding, the game's normal unfreeze path may be bypassed.
            // This can leave gameplay HUD hidden until the user toggles ESC.
            ui.SetFrozenAll(isFrozen: false, unexpectName: null);
        }
        catch (Exception ex)
        {
            MapTeleportPlugin.LogSource?.LogWarning($"[MapTeleport] TryUnfreezeUI({phase}) failed: {ex}");
        }
    }

    internal static UI_MarkWolrd TryGetNowAttackingMark(InGameMenuMapPanelCtrl menu)
    {
        try
        {
            if (menu == null)
            {
                return null;
            }

            // NOTE: In IL2CPP interop, instance fields are usually exposed as *properties*,
            // not C# fields. Using AccessTools.FieldRefAccess will fail.
            UI_Map map = menu.m_MinMapScript;
            if (map == null)
            {
                return null;
            }

            return map.m_NowAttackingMark;
        }
        catch (Exception ex)
        {
            MapTeleportPlugin.LogSource?.LogWarning($"[MapTeleport] TryGetNowAttackingMark: exception: {ex}");
            return null;
        }
    }

    internal static string Describe(UI_MarkWolrd mark)
    {
        if (mark == null)
        {
            return "<null>";
        }

        string tip = null;
        try
        {
            tip = mark.tips != null ? mark.tips.text : null;
        }
        catch
        {
            tip = "<tips-exception>";
        }

        string iconName = null;
        try
        {
            iconName = mark.markIcon != null && mark.markIcon.sprite != null ? mark.markIcon.sprite.name : null;
        }
        catch
        {
            iconName = "<icon-exception>";
        }

        return $"id={mark.id}, worldPos={mark.worldPos}, anchorPos={mark.anchorPos}, isUserDefine={mark.isUserDefine}, cfgPriority={mark.cfgPriority}, icon='{iconName}', tips='{tip}'";
    }

    internal static bool TryHandleTeleportFromMenu(InGameMenuMapPanelCtrl menu, string source)
    {
        try
        {
            UI_MarkWolrd mark = TryGetNowAttackingMark(menu);

            if (MapTeleportPlugin.DebugLogsEnabled)
            {
                MapTeleportPlugin.LogSource.LogInfo($"[MapTeleport] {source}: invoked. nowMapState={UIMapMgr.Instance?.nowMapState}, mark={Describe(mark)}");
            }

            if (mark == null)
            {
                return false;
            }

            MapMarkObjectModule module = MinimapTeleportPatch.TryFindBestModuleForMark(mark);
            if (!MinimapTeleportPatch.TryClassifyAsSavePoint(mark, module, out bool isTemporarySavePoint))
            {
                if (MapTeleportPlugin.DebugLogsEnabled)
                {
                    MapTeleportPlugin.LogSource.LogInfo($"[MapTeleport] {source}: not a savepoint, skipping. mark={Describe(mark)}");
                }
                return false;
            }

            string activeSceneName = SceneManager.GetActiveScene().name;
            string sceneName = activeSceneName;
            Vector3 targetPos = new(mark.worldPos.x, 0f, mark.worldPos.y);

            if (module != null)
            {
                targetPos = module.worldPos;

                string parsedScene = MinimapTeleportPatch.TryExtractSceneName(module.conditionMame);
                if (!string.IsNullOrWhiteSpace(parsedScene))
                {
                    // IMPORTANT:
                    // Kotama seems to use a wrapper scene like "GamePlay" and load actual area/room scenes separately.
                    // Those scenes may not be in Build Settings, so CanStreamedLevelBeLoaded can return false even if loadable.
                    // We therefore trust the condition prefix as the best guess for the real target "sceneName".
                    sceneName = parsedScene;
                }
            }

            if (RoomManager.Instance == null)
            {
                MapTeleportPlugin.LogSource.LogWarning("[MapTeleport] abort: RoomManager.Instance is null");
                return true;
            }

            MapTeleportPlugin.LogSource.LogInfo(
                $"[MapTeleport] teleport: {(isTemporarySavePoint ? "Temporary" : "Permanent")} savepoint -> activeScene='{activeSceneName}', scene='{sceneName}', pos={targetPos}, module={(module != null ? $"id={module.id}, cond='{module.conditionMame}', modulePos={module.worldPos}" : "<null>")}");

            if (MapTeleportPlugin.Runner == null)
            {
                MapTeleportPlugin.LogSource.LogWarning("[MapTeleport] abort: MapTeleportPlugin.Runner is null (cannot start coroutine)");
                return true;
            }

            float preTeleportTimeScale = Time.timeScale;

            // Close map menu first (use the same cancel path as the game), then teleport next frame.
            // This helps ensure the game is unpaused and input/menu state is consistent.
            try
            {
                menu.OnBtnCancel();
            }
            catch (Exception ex)
            {
                MapTeleportPlugin.LogSource?.LogWarning($"[MapTeleport] menu close via OnBtnCancel failed, fallback to Hide(): {ex}");
                try
                {
                    menu.Hide();
                }
                catch
                {
                    // ignore
                }
            }

            PendingTeleport pending = new()
            {
                Menu = menu,
                Source = source,
                ActiveSceneName = activeSceneName,
                SceneName = sceneName,
                TargetPos = targetPos,
                IsTemporarySavePoint = isTemporarySavePoint,
                Module = module,
                PreTeleportTimeScale = preTeleportTimeScale,
            };

            // Close immediately once, so user sees the map disappear right away.
            TryCloseMenu(menu, "immediate");

            MapTeleportPlugin.Runner.StartCoroutine(TeleportAfterMenuClose(pending).WrapToIl2Cpp());

            return true;
        }
        catch (Exception ex)
        {
            MapTeleportPlugin.LogSource.LogWarning($"[MapTeleport] {source}: exception: {ex}");
            return false;
        }
    }

    private static IEnumerator TeleportAfterMenuClose(PendingTeleport pending)
    {
        // Close again next frame to ensure menu state is fully cleared.
        TryCloseMenu(pending?.Menu, "pre");

        yield return null; // one frame
        yield return null; // another frame for menu/state transitions

        if (RoomManager.Instance == null)
        {
            MapTeleportPlugin.LogSource?.LogWarning("[MapTeleport] coroutine abort: RoomManager.Instance is null");
            yield break;
        }

        if (MapTeleportPlugin.DebugLogsEnabled)
        {
            SafeLogPlayerInfo("before");
        }
        SafeAdjustTargetHeight(pending);

        float oldTimeScale = Time.timeScale;
        SafeDoTeleport(pending, oldTimeScale);

        // Close once more after starting teleport, in case the UI was re-opened by internal state.
        TryCloseMenu(pending?.Menu, "post-start");
        TryUnfreezeUI("post-start");

        for (int i = 0; i < 20; i++)
        {
            yield return null;
        }

        if (MapTeleportPlugin.DebugLogsEnabled)
        {
            SafeLogPlayerInfo("after");
        }
        TryUnfreezeUI("after");

        if (oldTimeScale == 0f)
        {
            Time.timeScale = oldTimeScale;
        }
    }

    private static void SafeLogPlayerInfo(string phase)
    {
        try
        {
            if (UIMapMgr.Instance == null)
            {
                return;
            }

            var info = UIMapMgr.Instance.GetPlayerInfo();
            MapTeleportPlugin.LogSource?.LogInfo($"[MapTeleport] player({phase}): area={info.Item1}, pos={info.Item2}");
        }
        catch (Exception ex)
        {
            MapTeleportPlugin.LogSource?.LogWarning($"[MapTeleport] player({phase}) query failed: {ex}");
        }
    }

    private static void SafeAdjustTargetHeight(PendingTeleport pending)
    {
        try
        {
            if (pending == null || pending.Module != null || UIMapMgr.Instance == null)
            {
                return;
            }

            var info = UIMapMgr.Instance.GetPlayerInfo();
            pending.TargetPos = new Vector3(pending.TargetPos.x, info.Item2.y, pending.TargetPos.z);
        }
        catch
        {
            // Keep existing y
        }
    }

    private static void SafeDoTeleport(PendingTeleport pending, float oldTimeScale)
    {
        try
        {
            bool canTeleport = true;
            try
            {
                canTeleport = RoomManager.Instance != null && RoomManager.Instance.IsPlayerCanTeleport();
            }
            catch
            {
                canTeleport = true;
            }

            MapTeleportPlugin.LogSource?.LogInfo($"[MapTeleport] IsPlayerCanTeleport={canTeleport}");

            // If the game is paused (timescale 0) due to menu, the fade coroutine can get stuck.
            // Force timescale to 1 temporarily for the teleport process.
            if (oldTimeScale == 0f)
            {
                Time.timeScale = 1f;
            }

            // For teleports within the same scene, avoid auto fade to prevent black-screen lock,
            // but still load the target room prefab (otherwise you can end up with camera moved but player/room not loaded).
            bool sameScene = string.Equals(pending.SceneName, pending.ActiveSceneName, StringComparison.OrdinalIgnoreCase);
            bool isAutoFadeOut = !sameScene;
            bool isAutoFadeIn = !sameScene;
            bool isLoadScenePrefab = true;

            Action callback = () =>
            {
                MapTeleportPlugin.LogSource?.LogInfo("[MapTeleport] teleport callback invoked");
                TryCloseMenu(pending?.Menu, "callback");
                TryUnfreezeUI("callback");
            };

            MapTeleportPlugin.LogSource?.LogInfo(
                $"[MapTeleport] teleport(start): source={pending.Source}, sameScene={sameScene}, timeScale(old={oldTimeScale}, pre={pending.PreTeleportTimeScale}), targetPos={pending.TargetPos}");

            RoomManager.Instance.TeleportToTargetRoomProcess(
                pending.SceneName,
                pending.TargetPos,
                isAddEnteredRoom: true,
                isLoadScenePrefab: isLoadScenePrefab,
                isForcePlayerIdle: true,
                isAutoFadeOut: isAutoFadeOut,
                isAutoFadeIn: isAutoFadeIn,
                callback: callback
            );
        }
        catch (Exception ex)
        {
            MapTeleportPlugin.LogSource?.LogWarning($"[MapTeleport] teleport call failed: {ex}");
        }
    }
}

[HarmonyPatch(typeof(InGameMenuMapPanelCtrl), "OnMinMapX")]
internal static class MinimapTeleportPatch
{
    private static bool Prefix(InGameMenuMapPanelCtrl __instance)
    {
        if (__instance == null)
        {
            return true;
        }

        bool handled = MapTeleportCore.TryHandleTeleportFromMenu(__instance, "OnMinMapX");
        return !handled;
    }

    private struct ModuleCandidate
    {
        public MapMarkObjectModule Module;
        public float DistSqr;
    }

    internal static MapMarkObjectModule TryFindBestModuleForMark(UI_MarkWolrd mark)
    {
        if (mark == null || UIMapMgr.Instance == null || UIMapMgr.Instance.allMapMarkObjs == null)
        {
            return null;
        }

        int id = mark.id;
        Vector2 worldPos = mark.worldPos;

        MapMarkObjectModule best = null;
        float bestDistSqr = float.PositiveInfinity;
        int candidatesCount = 0;

        // Collect candidates for optional debug dump.
        // We avoid Linq here to keep IL2CPP happy.
        ModuleCandidate[] candidates = null;
        bool wantCandidates = MapTeleportPlugin.DebugCandidateModulesEnabled || MapTeleportPlugin.DebugLogsEnabled;
        int maxKeep = 0;
        if (wantCandidates)
        {
            maxKeep = Mathf.Clamp(MapTeleportPlugin.DebugCandidateModulesLimit, 0, 128);
            candidates = maxKeep > 0 ? new ModuleCandidate[maxKeep] : null;
        }

        foreach (MapMarkObjectModule module in UIMapMgr.Instance.allMapMarkObjs)
        {
            if (module == null || module.id != id)
            {
                continue;
            }

            float dx = module.worldPos.x - worldPos.x;
            float dy = module.worldPos.y - worldPos.y;
            float d2 = dx * dx + dy * dy;

            if (d2 < bestDistSqr)
            {
                best = module;
                bestDistSqr = d2;
            }

            if (candidates != null)
            {
                // Keep the closest N candidates by simple insertion (N is small).
                ModuleCandidate c = new() { Module = module, DistSqr = d2 };
                int keep = Mathf.Min(candidatesCount, candidates.Length);
                int insertAt = keep;
                for (int i = 0; i < keep; i++)
                {
                    if (d2 < candidates[i].DistSqr)
                    {
                        insertAt = i;
                        break;
                    }
                }

                if (insertAt < candidates.Length)
                {
                    int shiftStart = Mathf.Min(keep, candidates.Length - 1);
                    for (int j = shiftStart; j > insertAt; j--)
                    {
                        candidates[j] = candidates[j - 1];
                    }
                    candidates[insertAt] = c;
                }
            }

            candidatesCount++;
        }

        if (MapTeleportPlugin.DebugCandidateModulesEnabled)
        {
            try
            {
                MapTeleportPlugin.LogSource?.LogInfo(
                    $"[MapTeleport] candidates: id={id}, markWorldPos={worldPos}, markAnchorPos={mark.anchorPos}, tips='{mark?.tips?.text}', icon='{(mark?.markIcon?.sprite != null ? mark.markIcon.sprite.name : null)}', totalCandidates={candidatesCount}");

                if (candidates != null)
                {
                    int print = Mathf.Min(candidatesCount, candidates.Length);
                    for (int i = 0; i < print; i++)
                    {
                        MapMarkObjectModule m = candidates[i].Module;
                        string cond = m != null ? m.conditionMame : null;
                        string sceneGuess = TryExtractSceneName(cond);
                        Vector3 pos = m != null ? m.worldPos : default;
                        MapTeleportPlugin.LogSource?.LogInfo(
                            $"[MapTeleport] candidate[{i}]: d2={candidates[i].DistSqr:F4}, pos={pos}, sceneGuess='{sceneGuess}', cond='{cond}'");
                    }
                }

                if (best != null)
                {
                    MapTeleportPlugin.LogSource?.LogInfo(
                        $"[MapTeleport] candidate(best): d2={bestDistSqr:F4}, pos={best.worldPos}, cond='{best.conditionMame}'");
                }
                else
                {
                    MapTeleportPlugin.LogSource?.LogWarning("[MapTeleport] candidate(best): <null>");
                }
            }
            catch (Exception ex)
            {
                MapTeleportPlugin.LogSource?.LogWarning($"[MapTeleport] candidates dump failed: {ex}");
            }
        }

        return best;
    }

    internal static string TryExtractSceneName(string conditionName)
    {
        if (string.IsNullOrWhiteSpace(conditionName))
        {
            return null;
        }

        // conditionName convention (from in-game inspector header):
        // 区域+房间+类名+对象名+... e.g. "ZHO01_NPC_haha_01"
        int firstUnderscore = conditionName.IndexOf('_');
        if (firstUnderscore <= 0)
        {
            return null;
        }

        string prefix = conditionName.Substring(0, firstUnderscore).Trim();
        if (prefix.Length < 3 || prefix.Length > 32)
        {
            return null;
        }

        return prefix;
    }

    internal static bool TryClassifyAsSavePoint(UI_MarkWolrd mark, MapMarkObjectModule module, out bool isTemporary)
    {
        isTemporary = false;

        // Heuristic v1: Use the already-rendered tips text on the map mark.
        // This avoids needing to resolve cfg tables at runtime.
        string text = mark?.tips?.text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            // Common CN labels
            // (Kotama seems to label save/check points as "...点", e.g. "中继点")
            bool containsSave = text.Contains("\u5b58\u6863", StringComparison.OrdinalIgnoreCase) ||
                                text.Contains("\u5b58\u6863\u70b9", StringComparison.OrdinalIgnoreCase) ||
                                text.Contains("\u5927\u5b58\u6863\u70b9", StringComparison.OrdinalIgnoreCase) ||
                                text.Contains("\u5c0f\u5b58\u6863\u70b9", StringComparison.OrdinalIgnoreCase) ||
                                text.Contains("\u4e2d\u7ee7\u70b9", StringComparison.OrdinalIgnoreCase) ||
                                text.Contains("\u590d\u6d3b", StringComparison.OrdinalIgnoreCase) ||
                                text.Contains("\u91cd\u751f", StringComparison.OrdinalIgnoreCase);
            if (containsSave)
            {
                isTemporary = text.Contains("\u4e34\u65f6", StringComparison.OrdinalIgnoreCase);

                // If the UI explicitly says "小存档点"/"临时...", treat it as temporary.
                if (!isTemporary && (text.Contains("\u5c0f\u5b58\u6863", StringComparison.OrdinalIgnoreCase) ||
                                     text.Contains("\u4e34\u65f6\u5b58\u6863", StringComparison.OrdinalIgnoreCase)))
                {
                    isTemporary = true;
                }

                return true;
            }

            // Fallback EN labels (in case of localization)
            bool containsSaveEn = text.Contains("save", StringComparison.OrdinalIgnoreCase);
            if (containsSaveEn)
            {
                isTemporary = text.Contains("temp", StringComparison.OrdinalIgnoreCase) ||
                              text.Contains("temporary", StringComparison.OrdinalIgnoreCase);
                return true;
            }

            // Heuristic v1.5: Some marks may not include "存档" in tips, but still use "...点" naming.
            // Only accept when it looks like a system map mark (not user mark) and has higher priority.
            bool looksLikeSystemPoint = !mark.isUserDefine &&
                                        mark.cfgPriority >= 5 &&
                                        text.EndsWith("\u70b9", StringComparison.OrdinalIgnoreCase);
            if (looksLikeSystemPoint)
            {
                // If it is a checkpoint/relay point, allow teleport (treat as permanent by default).
                isTemporary = text.Contains("\u4e34\u65f6", StringComparison.OrdinalIgnoreCase) ||
                              text.Contains("\u5c0f\u5b58\u6863", StringComparison.OrdinalIgnoreCase);
                return true;
            }
        }

        // Heuristic v2: sprite name
        string spriteName = null;
        try
        {
            spriteName = mark?.markIcon != null && mark.markIcon.sprite != null ? mark.markIcon.sprite.name : null;
        }
        catch
        {
            spriteName = null;
        }

        if (!string.IsNullOrWhiteSpace(spriteName))
        {
            bool containsSave = spriteName.Contains("Storage", StringComparison.OrdinalIgnoreCase) ||
                                spriteName.Contains("Save", StringComparison.OrdinalIgnoreCase);
            if (containsSave)
            {
                isTemporary = spriteName.Contains("Temp", StringComparison.OrdinalIgnoreCase) ||
                              spriteName.Contains("Temporary", StringComparison.OrdinalIgnoreCase);
                return true;
            }
        }

        // Heuristic v2: Some projects include SavePoint keywords in condition names.
        string cond = module?.conditionMame;
        if (!string.IsNullOrWhiteSpace(cond))
        {
            bool containsSave = cond.Contains("\u5b58\u6863", StringComparison.OrdinalIgnoreCase) ||
                                cond.Contains("\u4e2d\u7ee7", StringComparison.OrdinalIgnoreCase) ||
                                cond.Contains("save", StringComparison.OrdinalIgnoreCase);
            if (containsSave)
            {
                isTemporary = cond.Contains("\u4e34\u65f6", StringComparison.OrdinalIgnoreCase) ||
                              cond.Contains("temp", StringComparison.OrdinalIgnoreCase) ||
                              cond.Contains("temporary", StringComparison.OrdinalIgnoreCase);
                return true;
            }
        }

        // Heuristic v3: known mark ids (based on observed tips)
        // 5010004: tips="中继点" in logs.
        if (mark != null && mark.id == 5010004)
        {
            isTemporary = false;
            return true;
        }

        return false;
    }
}

[HarmonyPatch(typeof(InGameMenuMapPanelCtrl), "OnBtnX")]
internal static class DebugOnBtnXPatch
{
    private static bool Prefix(InGameMenuMapPanelCtrl __instance)
    {
        if (__instance == null)
        {
            return true;
        }

        bool handled = MapTeleportCore.TryHandleTeleportFromMenu(__instance, "OnBtnX");
        return !handled;
    }
}

[HarmonyPatch(typeof(InGameMenuMapPanelCtrl), "OnBigMapX")]
internal static class DebugOnBigMapXPatch
{
    private static bool Prefix(InGameMenuMapPanelCtrl __instance)
    {
        if (__instance == null)
        {
            return true;
        }

        bool handled = MapTeleportCore.TryHandleTeleportFromMenu(__instance, "OnBigMapX");
        return !handled;
    }
}

[HarmonyPatch(typeof(InGameMenuMapPanelCtrl), "OnUpdate")]
internal static class DebugOnUpdateMarkChangePatch
{
    private static int _lastMarkId = int.MinValue;
    private static int _lastLogFrame = -999999;

    private static void Postfix(InGameMenuMapPanelCtrl __instance, float deltaTime)
    {
        try
        {
            if (!MapTeleportPlugin.DebugLogsEnabled)
            {
                return;
            }

            if (__instance == null || MapTeleportPlugin.LogSource == null)
            {
                return;
            }

            UI_MarkWolrd mark = MapTeleportCore.TryGetNowAttackingMark(__instance);
            int id = mark != null ? mark.id : int.MinValue;

            // Avoid spamming: only log on selection change or every ~5 seconds.
            bool selectionChanged = id != _lastMarkId;
            bool periodic = (Time.frameCount - _lastLogFrame) > 300;
            if (!selectionChanged && !periodic)
            {
                return;
            }

            _lastMarkId = id;
            _lastLogFrame = Time.frameCount;

            MapTeleportPlugin.LogSource.LogInfo(
                $"[MapTeleport] OnUpdate: nowMapState={UIMapMgr.Instance?.nowMapState}, mark={MapTeleportCore.Describe(mark)}");
        }
        catch (Exception ex)
        {
            MapTeleportPlugin.LogSource?.LogWarning($"[MapTeleport] OnUpdate log failed: {ex}");
        }
    }
}

[HarmonyPatch(typeof(InGameMenuMapPanelCtrl), "OnUserMarkConfirm")]
internal static class DebugOnUserMarkConfirmPatch
{
    private static bool Prefix(InGameMenuMapPanelCtrl __instance)
    {
        if (__instance == null)
        {
            return true;
        }

        bool handled = MapTeleportCore.TryHandleTeleportFromMenu(__instance, "OnUserMarkConfirm");
        return !handled;
    }
}

[HarmonyPatch(typeof(InGameMenuMapPanelCtrl), "OnMinMapConfirm")]
internal static class DebugOnMinMapConfirmPatch
{
    private static bool Prefix(InGameMenuMapPanelCtrl __instance)
    {
        if (__instance == null)
        {
            return true;
        }

        bool handled = MapTeleportCore.TryHandleTeleportFromMenu(__instance, "OnMinMapConfirm");
        return !handled;
    }
}

[HarmonyPatch(typeof(InGameMenuMapPanelCtrl), "OnBigMapConfirm")]
internal static class DebugOnBigMapConfirmPatch
{
    private static bool Prefix(InGameMenuMapPanelCtrl __instance)
    {
        if (__instance == null)
        {
            return true;
        }

        bool handled = MapTeleportCore.TryHandleTeleportFromMenu(__instance, "OnBigMapConfirm");
        return !handled;
    }
}

[HarmonyPatch(typeof(InGameMenuMapPanelCtrl), "OnBtnConfirm")]
internal static class DebugOnBtnConfirmPatch
{
    private static bool Prefix(InGameMenuMapPanelCtrl __instance)
    {
        if (__instance == null)
        {
            return true;
        }

        bool handled = MapTeleportCore.TryHandleTeleportFromMenu(__instance, "OnBtnConfirm");
        return !handled;
    }
}

[HarmonyPatch(typeof(UI_Map), "OperateUserDefineMark")]
internal static class DebugOperateUserDefineMarkPatch
{
    private static bool Prefix(UI_Map __instance, int _id)
    {
        try
        {
            UI_MarkWolrd mark = __instance != null ? __instance.m_NowAttackingMark : null;
            MapTeleportPlugin.LogSource.LogInfo($"[MapTeleport] UI_Map.OperateUserDefineMark({_id}) called. mark={MapTeleportCore.Describe(mark)}");
        }
        catch (Exception ex)
        {
            MapTeleportPlugin.LogSource.LogWarning($"[MapTeleport] UI_Map.OperateUserDefineMark log failed: {ex}");
        }

        return true;
    }
}
