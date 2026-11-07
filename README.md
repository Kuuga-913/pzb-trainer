# 影之刃零 修改器（Phantom Blade Zero Trainer）

单人模式的《影之刃零 / Phantom Blade Zero》外部修改器，C# / WPF 实现，界面取自水墨暗色 + 朱砂红的视觉风格。

- **76 项功能**，按 10 个分类浏览（战斗与生存 / 杀气与绝技 / 速度与冷却 / 破防与处决 / 异常与持续伤害 / 格挡与鬼步 / 资源与消耗 / 飞行与穿墙 / 六十六天模式 / 地图与收集）
- **目录类工具**：添加物品（881 项）、背包管理（改数量 / 删除）、生成怪物（177 种）、探索与地图（179 个地点传送）、一键全收集
- **游戏内悬浮层**：小地图（雷达式，中心玩家 + 铃铛 / 收集点 / 敌人）、透视（目标方框 + 名称距离标签，逐帧插值平滑跟随）
- 全局热键、状态自动保存、单文件免安装（自带 .NET 运行时）

![截图](assets/screenshot.png)

## 下载

到 [Releases](https://github.com/Kuuga-913/pzb-trainer/releases/latest) 下载成品（Windows x64，免安装）：

| 文件 | 说明 |
|---|---|
| **PBZTrainer-v1.0.1-win-x64.exe**（最省事） | 单文件，**组件已内置**，下载双击即用 |
| PBZTrainer-v1.0.0-win-x64.zip | 不含组件，解压后需自备一次 `PBZBridge.dll` |
| PBZTrainer-v1.0.0.exe | 同上，单文件版 |

内置组件版首次运行会把组件释放到 `%LOCALAPPDATA%\影之刃零修改器\components\<哈希>\`，之后复用；
不内置的版本第一次启动会弹窗让你选一次 `PBZBridge.dll`，程序自动复制到自己旁边（详见下文）。

## 快速开始

1. 先启动游戏，进入存档、能自由行动。
2. 双击 `PBZTrainer.exe`。它会自动找到 `PBZ.exe`、注入组件、完成握手，右上角变成「● 游戏已就绪」。
3. 左侧选分类，右侧拨开关即可。工具页（物品 / 怪物 / 地图 / 全收集 / 小地图 / 透视）在下面几组。

> 详细用法见 [docs/USAGE.txt](docs/USAGE.txt)，第一次用先看 [docs/FIRST-RUN.txt](docs/FIRST-RUN.txt)。

## 关于 `PBZBridge.dll`

这个程序需要一份 **`PBZBridge.dll`** 作为游戏内组件（注入到 `PBZ.exe` 里负责实际读写游戏状态）。

**该组件是第三方文件，不属于本项目，仓库里不包含它**，请自行准备并放到 `PBZTrainer.exe` 同目录。程序按以下顺序查找：

1. 环境变量 `PBZ_BRIDGE`（完整路径）
2. `PBZTrainer.exe` 所在目录
3. 游戏安装目录（`PBZ.exe` 旁边）
4. `D:\XiaowuTrainer\data\modules\**`（小伍盒子模块目录）

组件与游戏版本绑定，版本不一致会提示「游戏中的组件版本不同，请重启游戏后再连接」。

## 构建

需要 .NET 8 SDK（Windows）：

```bat
dotnet publish src\PBZTrainer\PBZTrainer.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o out
```

产物 `out\PBZTrainer.exe` 约 66 MB，单文件、免安装。

想把组件一起打进 exe（得到一个开箱即用的单文件）：

```bat
dotnet publish src\PBZTrainer\PBZTrainer.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -p:DebugType=none ^
  -p:EmbedBridge=true -p:BridgeDll="路径\PBZBridge.dll" -o out
```

这样运行时会把内嵌组件释放到本地缓存再注入。不带 `PBZCatalog.json` 也能编译，只是「一键全收集」的结果弹窗里显示 id 而不是中文名（可选：把该文件放进 `src/PBZTrainer/Data/` 再编译）。

## 目录结构

```
src/PBZTrainer/
  Services/
    Injector.cs        定位 PBZ.exe、注入组件、解析组件路径
    BridgeClient.cs    命名管道客户端：帧收发、握手、能力轮询
    BridgeCommands.cs  完整指令集（目录 / 物品 / 生成 / 传送 / 全收集 / 小地图 / 透视）
    BridgeGate.cs      解除「高级功能」限制（仅改游戏进程内存）
    HotkeyService.cs   全局热键（低级键盘钩子）
    StateStore.cs      状态持久化（兼容原版 trainer_state.json 字段）
  Views/
    MainWindow.xaml    主界面：左侧分组导航 + 右侧功能卡片
    CatalogView.cs     通用目录浏览器（物品 / 背包 / 怪物 / 地点）
    CompletionView.cs  一键全收集（进度 + 结果弹窗）
    OverlayService.cs  小地图 / 透视的驱动与轮询
    OverlayWindows.cs  贴在游戏画面上的透明悬浮层
    EspView.cs / MinimapView.cs / UiKit.cs
  Models/              功能清单解析、状态模型、id→中文名映射
  Data/PBZFeatures.json 功能表（id / 标题 / 分类 / 类型 / 默认值）
```

## 技术说明

### 通信协议

命名管道 `\\.\pipe\ChuwuPBZ_v1_<游戏PID>`，每帧 = 4 字节小端长度 + UTF-8 JSON；每个请求自动附加 `requestId` / `session` / `clientPid`，响应带同一个 `session`。

常用指令：

| 指令 | 说明 |
|---|---|
| `hello` / `capabilities` | 握手与能力（76 项状态、角色、地图、坐标） |
| `feature{id,enabled,value}` | 开关某一项功能，`value` 为数值型功能的参数 |
| `collection_step{job}` | 收集类功能的分步执行 |
| `catalog{type}` | 目录：`items` / `inventory` / `enemies` / `locations` / `bells` / `markers` / `dead_enemies` |
| `grant_items{ids,count}` / `inventory_change{id,snapshot,delete,value}` | 添加物品 / 改数量或删除 |
| `spawn{id,count,distance,friendly}` + `spawn_status{batch}` | 生成怪物 |
| `position` / `teleport{world,position}` / `collection_locate{id,travel}` | 位置读取与传送 |
| `completion.plan/begin/step/cancel` | 一键全收集（`job` 是数字，必须原样回传） |
| `minimap.configure/frame/travel_target` | 小地图（帧里含玩家 uv、标记 uv、朝向、层数） |
| `esp.configure/frame/status` | 透视（帧里含 `left/top/right/bottom/label` 屏幕坐标） |

### 注入与门禁

注入走标准 `OpenProcess` + `VirtualAllocEx` + `WriteProcessMemory` + `CreateRemoteThread(LoadLibraryW)`。

原版组件对「添加物品 / 生成怪物 / 一键全收集」等指令有一道高级功能校验。本程序在连接时把该判定入口（组件内的一个跳转表项）在**游戏进程内存**里改成恒真，只改内存、不动磁盘文件，游戏重启后重新打一次即可。若组件版本不同，会先校验字节特征再决定是否修改，不匹配就跳过并在日志里说明。

### 界面实现要点

- 无边框窗口用 `WindowStyle=None` + `AllowsTransparency`，拖动区域必须是 `Background="Transparent"` 的容器（`null` 背景不参与命中测试）。
- 卡片开关用 `IsChecked` 双向绑定 + `PropertyChanged`，而不是 `Click`（`Click` 不会被键盘 / UIA 触发）。
- 悬浮层是 `Topmost` + `WS_EX_TRANSPARENT` 的透明窗，按游戏客户区物理像素定位、内容做 `1/dpi` 缩放，使 1 个布局单位等于 1 物理像素；修改器窗口在前台时自动隐藏。
- 透视用「150ms 拉帧 + 每帧向目标位置插值 + 元素按 id 复用」，避免位置跳变。

## 免责声明

- 仅用于**单人离线**模式，请勿用于联机、排行榜或任何涉及其他玩家的场景。
- 会写存档的功能（添加物品、改数量、删除、一键全收集）关闭程序后不会撤销，**使用前请自行备份存档**。
- 本项目与游戏开发商、发行商无任何关联；游戏名称、游戏内文本与第三方组件的权利归其各自权利人所有。
- 使用本工具产生的一切后果由使用者自行承担。

## License

代码以 MIT 协议开源，详见 [LICENSE](LICENSE)。第三方组件与游戏内容不在授权范围内，见 [NOTICE.md](NOTICE.md)。
