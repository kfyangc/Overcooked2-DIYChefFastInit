# 厨师网格选择（2.2.0.2 测试版）

主菜单打开“厨师”时显示 8 列 × 3 行的分页网格，右侧显示当前光标对应的独立高清头像。条目数随实际可选厨师变化，移动到其他页会自动翻页。新增功能的生产代码全部放在 `ChefGrid/`，离线回归放在 `tests/ChefGrid/`。

根据用户的 `preview_2.2.0.1.png` 反馈，2.2.0.2 改为独立顶层 Canvas，消除原菜单继承缩放；所有文字及操作区纳入同一个不透明面板，背后增加全屏遮罩。卡片改为独立的方形图片和两行名称区域，字体换为清晰的系统字体。右侧头像从单独的 512 × 512 渲染或缓存读取，不再放大网格缩略图。模型按完整边界留白，取消底部裁切。

**本轮未启动游戏。** 已通过目标游戏程序集的 net35 编译和离线逻辑回归；2.2.0.2 的画面、输入与联机表现仍需游戏内复测。

## 原版能力调研

直接反编译本机游戏的 `Overcooked2_Data/Managed/Assembly-CSharp.dll`，没有启动游戏。调研时该文件 SHA-256 为 `9bb6a3791331201d32ca89c3509f019a9780309da7110002f04020e8491e1908`。同目录已有的 `Assembly-CSharp.dll.测1菜单` 备份，其 `FrontendChefMenu` 反编译结果与当前文件一致。结论限定于这套本机程序集，未比较其他平台和游戏版本。

| 类型 / 方法 | 已有能力 | 对实现的影响 |
| --- | --- | --- |
| `FrontendChefMenu.Show/Hide/Close` | 打开、确认、取消、镜头切换与菜单生命周期 | 沿用原菜单；确认调用 `Hide`，取消调用 `Close` |
| `ChefCustomiser.CacheCurrentAvatars/RevertAvatars` | 按输入槽位和分屏状态缓存、恢复官方头像 | 继续使用已有 DIY 快照补丁恢复额外厨师 |
| `FrontendChefCustomisation.Update/SetChefSelection` | 每位玩家的左右输入、循环选择、发送官方头像 | 没有多行厨师列表或多头像预览；网格打开时替换这部分输入处理 |
| `T17NavigableGrid` | 动态添加条目、二维导航 | 是通用 UI 基类，依赖边缘 Selectable、T17EventSystem，并只维护一组当前选中索引 |
| `T17ScrollView` | 基于上述控件滚动到条目 | 可以用于单光标列表；仍需自行提供厨师条目、头像和多玩家状态 |
| `ChefAvatarData` | 前端、关卡和 UI 三套 3D 预制体 | 没有头像纹理字段，不能直接填充二维头像网格 |
| `PlayerInputLookup.GetFixedButton/GetFixedValue` | 按 PlayerGameInput / PadSide 映射逻辑输入 | 复用设备映射；纵向用 MovementY，避免通用 UIUp/UIDown 为分屏键盘额外注入共享方向键 |

因此原版没有可直接启用的完整厨师表格。实现使用游戏已有的 Unity uGUI 组件构建独立覆盖层，自行管理分页和各本地玩家光标，同时复用原版模型、设备映射、菜单生命周期及原有 Mod 的加载/通信路径。没有修改游戏 DLL，也不需要额外 AssetBundle。

## 操作与扩展

- 手柄十字键 / 摇杆，或各玩家映射的移动键：上下左右移动，可长按连移。
- 方向键：控制当前活动的键盘玩家；点击上方 P1–P4 标签切换鼠标操作的玩家。
- 滚轮、PgUp / PgDn 或底部分页按钮：翻页查看，不立即改变选择。
- 点击头像：为活动玩家选择厨师。多个本地玩家可分别移动光标，头像上显示相应 P 编号。
- 原版确认键、空格 / Enter、确认按钮：保留选择并退出；原版取消键、Esc、返回按钮：恢复进入菜单时的选择。

启用 `prefer.txt` 时沿用登记、过滤后的顺序，只显示可用的官方厨师和已登记的额外厨师；没有启用偏好列表时显示已解锁的官方厨师。新厨师沿用现有资源目录、INFO 和 ID 规则登记后会自动增加页数，无需修改网格代码。额外厨师 ID 仍使用现有协议的 0–254。

选择时先安装额外 ID，再发送官方头像及额外 ID，最后刷新显示。额外厨师的官方替代外观沿用已有默认值，允许纯额外厨师列表。远端玩家不接受本地光标操作。玩家加入、离开或重排后，以输入槽位和分屏身份保留本地光标。

## 头像与配置

冷缓存时，先显示厨师名称，然后逐个补齐当前页头像。单独的离屏渲染对象沿用 `ChefLoading` 的异步读取和主线程预算，一次只准备一位厨师；实际玩家预览有待加载任务时，头像生成让出加载器。离屏对象不触发 ModelTest 粒子补挂。

左侧缩略图使用 128 × 128 PNG，右侧独立使用 512 × 512 PNG，放在 `BepInEx/cache/DIYChefFastInit/portraits/`；高清缓存使用 `.512.png` 后缀，下次打开或重启直接读取。内存最多保留 96 张缩略图、一张当前高清图、两个渲染目标和一个离屏模型，关闭菜单或卸载插件时释放。切换厨师时优先准备高清图，期间可暂时显示已缓存的小图。缓存键包含构图版本、厨师、帽子、颜色、朝向、程序集时间/大小和额外资源文件的路径、大小、修改时间，2.2.0.2 自动使用新的构图缓存，不需要手动清理旧缓存。该视觉缓存不做资源内容校验；同大小同时间戳替换素材后，可删除 portraits 目录重建，实际模型资源缓存仍使用原有完整校验。

额外厨师目录可提供 `preview.png`，优先作为头像。建议使用正方形、512 像素的图片；列表将其降采样为 128 像素。较小的图片仅用于列表，右侧仍自动渲染高清模型。可用此方式为不适合统一构图的厨师指定头像。

配置文件 `dev.gua.overcooked.diychef.cfg`：

```ini
[ChefSelection]
GridEnabled = true
PortraitYaw = 0
```

`GridEnabled=false` 使用原左右轮播；`PortraitYaw` 调整生成头像的模型旋转角度，修改后自动使用新的缓存键。界面或头像初始化异常会记录日志；界面异常回到原菜单，单个头像失败保留可选择的名称。

## 验证

Release/net35 构建使用本机实际游戏和 BepInEx 程序集：0 警告、0 错误。离线回归执行生产导航、列表和选择代码，覆盖 1–400 个条目、8 列导航、24 项分页/循环/末行不满、109 人厨师列表、偏好顺序、解锁过滤、纯额外厨师列表、官方替代 ID、消息顺序和远端玩家保护。320,836 项断言在 .NET 11 和游戏自带的 x86 Mono 下均通过；后者使用独立 Mono host，不运行 Overcooked2.exe 或 Unity 场景。

已有回归同时通过：取消恢复 29 项、默认厨师 49 项、ModelTest 兼容 45 项、材质所有权 16 项。

常规离线回归：

```powershell
dotnet run --project tests/ChefGridRegression.csproj -c Release
dotnet run --project tests/ChefSelectionRegression.csproj -c Release
```

仓库测试项目沿用 net10.0；本机只安装 .NET 11 时，可同时覆盖构建和还原目标，再运行生成的程序集：

```powershell
dotnet build tests/ChefGridRegression.csproj -c Release -p:TargetFrameworks=net11.0 -p:TargetFramework=net11.0
dotnet tests/bin/chef-grid/net11.0/ChefGridRegression.dll
```

待游戏内验收：冷/热缓存打开、头像朝向与特殊体型、不同分辨率、键盘/手柄/分屏、加入离开、确认及取消、关闭后重新打开，以及联机对端显示。上述离线结果不能替代画面验收。
