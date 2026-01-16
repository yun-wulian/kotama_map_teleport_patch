# KotamaAcademyCitadel 地图传送补丁（IL2CPP / BepInEx6）

[English README](README.en.md)

本项目是一个 **BepInEx IL2CPP 插件**，用于在《KotamaAcademyCitadel / Kotama Academy Citadel》中实现：

- 在 **地图界面**，当光标指向存档点相关标记时，按下 **SPACE**（游戏内原本可能用于地图标记/确认）即可 **传送**到该点位置。

## 功能

- 地图光标指向“小存档点 / 中继点”等标记时，按 `Space` 直接传送到该点。
- 传送成功后自动关闭 InGameMenu（若遇到残留 UI，可按一次 `ESC` 正常退出）。

## 说明（重要）

- 该 MOD 的触发按键依赖游戏在地图界面的输入映射；当前实现以“确认/放置标记”这一类输入作为触发入口。
- 该 MOD 仅对“存档点相关标记”生效，其他普通地图标记保持游戏原本行为。

## 依赖

- 游戏：KotamaAcademyCitadel（Unity 2022.3 / IL2CPP）
- **BepInEx 6（IL2CPP 版本）**
  - 官方 Releases（稳定版）：https://github.com/BepInEx/BepInEx/releases
  - BepInEx 构建站（推荐，适合 IL2CPP 最新 build）：https://builds.bepinex.dev/
  - 项目页（bepinex_be）：https://builds.bepinex.dev/projects/bepinex_be
  - 已验证可用（Windows x64 / IL2CPP metadata v31）：
    - `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.752+dd0655f.zip`
    - https://builds.bepinex.dev/projects/bepinex_be/752/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.752%2Bdd0655f.zip

## 安装

1. 安装 BepInEx 6（IL2CPP）。
2. 获取插件 DLL（自行编译或使用 Release）：`Kotama.MapTeleport.dll`
3. 放到游戏目录：`KotamaAcademyCitadel\\BepInEx\\plugins\\Kotama.MapTeleport.dll`
4. 启动游戏。

## 使用方法

1. 打开地图界面。
2. 用键盘/手柄移动光标，指向“中继点”等存档点相关标记。
3. 按 `Space` 进行传送。

## 编译（推荐）

本工程通过相对路径引用游戏目录下的 `BepInEx/core` 与 `BepInEx/interop` 程序集，最省事的方式是把仓库放在游戏目录的 `Modding` 下：

1. 放置/克隆到：
   - `...\\KotamaAcademyCitadel\\Modding\\kotama_map_teleport_patch\\`
2. 编译：
   - `dotnet build .\\kotama_map_teleport_patch\\KotamaMapTeleport.csproj -c Release`
3. 输出：
   - `.\\kotama_map_teleport_patch\\bin\\Release\\net6.0\\Kotama.MapTeleport.dll`

## 备注

- 本仓库不提交任何游戏文件 / BepInEx 文件 / 构建产物；已编译 DLL 请到 GitHub Releases 下载。

