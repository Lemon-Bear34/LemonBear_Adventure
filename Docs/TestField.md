# TestField：Lua 玩法与 GitHub 资源更新

## 测试

打开 `Assets/Scenes/TestField.unity`，点击 Play。蓝色胶囊体是 Player。
WASD / 方向键移动，Shift 加速，空格跳跃；相机平滑跟随。
场地包含地面、边界墙和碰撞障碍物。此场景已加入 Build Settings 并排在第一位。

输入、相机相对移动、斜向速度归一化、转向、重力、跳跃、掉落重生、相机跟随
全部位于 `Assets/GameContent/Lua/*.lua.txt`。C# 只负责资源加载和 xLua 生命周期。
Lua 文件保留 `.txt` 后缀，供 Unity 作为 TextAsset 打入 AB 包。

每次进入场景，游戏请求 GitHub `ab-resources/<平台>/catalog.json`。
清单包含平台、Unity 版本、Hash128、CRC 和带哈希的包文件名。
新版本下载并校验 CRC，相同版本从游戏缓存加载；不会每次重复下载相同的大包。
缓存位于 `Application.persistentDataPath/AssetBundles/<平台>`，加载缓存时再次校验 CRC。
网络或清单失败时使用 StreamingAssets 中随客户端附带的完整版本。
左上角显示加载状态和资源来源。失败后可以点击 Retry 重试。

## 修改、构建、发布

1. 修改 Lua、`Assets/GameContent/Prefabs/Player.prefab` 或蓝色材质。
2. 在 Unity 的 Build Settings 选择目标平台（当前提供 Windows x86_64）。
3. 点击 `LemonBear > Build AssetBundles`。不要重复 Create TestField，除非希望重建测试场景。
4. 在项目根目录运行：

   ```powershell
   powershell -ExecutionPolicy Bypass -File Tools/Publish-AssetBundles.ps1
   ```

   其他平台示例：`-Platform Android`。先安装相应 Unity 平台模块，并构建该平台的包。
5. 再次启动场景/游戏，新清单将自动生效。发布脚本仅提交资源分支，不提交主项目改动。

发布脚本使用本机 Git 的 GitHub 登录凭据，需要具备仓库写入权限。
若出现 Invalid username or token，请先更新本机 Git 登录凭据，再重新运行。
脚本会沿用已启用的 Windows HTTP 代理，也可以传入 `-ProxyUrl http://127.0.0.1:7890`。
使用 `-PrepareOnly` 可以只生成资源提交、不上传。

构建输出位于 `Build/AssetBundles/<平台>`，离线副本位于
`Assets/StreamingAssets/AssetBundles/<平台>`。发布时保留历史哈希文件，
清单和新包在同一次提交中更新。构建后需要重新发布才会更新远端资源。
构建客户端时会自动构建并更新该平台的离线 AB 包。
如果只想验证本地修改，可以关闭 Bootstrap Inspector 中的 Check Remote。

## 范围与限制

- 已接入一个完整的 gamecontent AB 包，包含 Player prefab、材质和三个 Lua 模块。
  TestField 场景与场地本身随客户端发布。以后增加资源需扩展构建列表及加载流程。
- 客户端和包必须使用相同的 Unity 版本、目标平台。当前是 2022.3.61f1c1 / StandaloneWindows64。
- 仓库需公开才能匿名下载。客户端不包含 GitHub token。
- GitHub 在部分网络环境中不可访问；此时界面显示 Packaged (offline)。
- 该下载方式适用于当前 Windows 测试。移动平台需分别构建并验证；WebGL 尚未接入。
- 增加新的 C# API 类型调用时，请补充 `Assets/Scripts/Runtime/link.xml` 的保留配置。
  当前调用使用 xLua 反射桥接，不需要修改导入的 xLua 源码。

参考：[文件下载](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Networking.DownloadHandlerFile.html)、
[AB 加载与 CRC](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AssetBundle.LoadFromFileAsync.html)。
