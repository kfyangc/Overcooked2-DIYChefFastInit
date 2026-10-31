# 厨师按需加载

厨师和帽子资源按实际使用需求加载。版本的唯一来源为 Version.cs；项目版本、BepInEx 插件版本、程序集及文件版本共用该值。

## 构建与安装

安装 .NET SDK，然后执行：

```powershell
dotnet build -c Release "-p:GameDir=D:/Games/Steam Games/steamapps/common/Overcooked! 2"
```

也可以设置 OC2_GAME_DIR 环境变量。依赖直接取自目标游戏的 Managed 和 BepInEx/core，保持 net35 兼容，不把游戏程序集复制到产物中。输出为 bin/Release/OC2DIYChef.dll。

退出游戏，备份安装目录中旧的 OC2DIYChef.dll，然后将新 DLL 放到原 DLL 的位置，保留旁边的 Resources 和 prefer.txt。避免在 plugins 下留下第二个同 GUID 的插件。重启后，在 BepInEx/LogOutput.log 核对 Loading 行的版本及 Version 行的完整 DLL 路径。

## 行为与配置

成功读档后登记 INFO、BODY、选择顺序、帽子和资源路径，不在启动阶段导入厨师贴图、OBJ 或三种模型。重复读档不重复登记或订阅事件。

前端选择使用单个后台读取/解析任务；主线程以约 3 ms 调度预算分步解码、上传和构建，旧模型继续显示。每个显示对象只保留最新选择。纹理解码/上传等单次 Unity API 调用不能被预算抢占，仍可能形成长帧。InGame 创建链保留同步兼容入口，其等待时间单独写日志。

每个型号、渲染器持有独立绑定网格，纹理跨型号共享。缓存资源由实际实例引用保护；关闭或销毁显示对象后取消不再需要的工作。实例销毁时回收其材质副本。

BepInEx 配置文件 dev.gua.overcooked.diychef.cfg：

| [Loading] 项目 | x86 默认 | x64 默认 |
| --- | ---: | ---: |
| CacheBudgetMiB | 64 | 128 |
| MaxCachedChefs | 3 | 6 |
| Diagnostics | false | false |

既有配置值保留。超过任一缓存目标时按最近使用情况回收闲置资源；正在加载、等待应用和被实例引用的资源暂时保留。估算预算不等于整个游戏的内存上限。

Diagnostics=true 时记录资源准备、纹理解码上传、应用、首个相机绘制帧、帧 P95/最大值、Windows Private Bytes/Working Set、缓存估算和引用情况。相机绘制回调表示相机完成该帧绘制，不代表显示器扫描输出或 GPU 硬件计时。

## 1.3.0.1 验证记录

- 使用本机 Unity 2017.4/x86 的实际游戏与 BepInEx 程序集构建：0 警告，0 错误。
- 隔离游戏启动日志确认加载 1.3.0.1，所有 Harmony 补丁完成注册。
- 当前尚未完成前端/关卡的 GPU 画面验收、完整切换测量、两轮遍历内存曲线和联机验收，不能据此宣称全部验收通过。
- 首次隔离启动发现 Mono 的 Process 内存属性返回 0，已改用 Windows GetProcessMemoryInfo；真实数据在后续运行中复核。


## 1.3.0.2 蒙皮绑定调整

针对前端厨师模型出现拉长三角形的反馈，改为保留不可变的原始几何数组，为每个渲染器新建完整的蒙皮网格。刚性部件只绑定一个实际骨骼，身体保留原来的两骨骼高度权重，并按权重降序存储。绑定网格保留 CPU 可读数据至缓存回收，缓存估算相应包含 CPU 与 GPU 数据。

同时允许用于隐藏部件的空 OBJ，并为超过 65,535 个展开顶点的网格选择 32 位索引。完整身体替换使用按方法匹配的补丁查找 Body_Body，不再依赖固定 IL 下标。

离线回归执行：

```powershell
./tests/Run-ParserRegression.ps1
```

本机安装资源集的 349 个 OBJ、1,546,641 个展开顶点与原始导入器的坐标、法线、UV 和三角形顺序逐项一致；刚性和身体权重检查通过。该回归不创建 Unity 对象、不启动游戏。渲染效果和游戏体验由实际游戏测试确认，目前尚未确认截图中的爆面已经消除。

## 1.3.0.3 菜单取消

进入厨师选择菜单时，与原游戏的头像缓存同步记录本地玩家的额外厨师 ID。记录使用输入槽位和分屏状态作为标识；取消时先恢复这些 ID、取消待应用的预览请求，再让原游戏恢复原版头像，最后同步额外 ID 并刷新显示。确认退出保留当前选择。

离线回归命令：

```powershell
dotnet run --project tests/ChefSelectionRegression.csproj -c Release
```

使用托管游戏接口替身执行实际快照和恢复代码，29 项断言通过，覆盖原版/额外厨师互换、额外厨师互换、分屏区分、玩家重排、新玩家、远端玩家、重复取消、确认后重开和同步顺序。不加载 Unity、不启动游戏；实际 A/B 输入表现由游戏内测试确认。

## 1.3.0.4 持久化资源缓存

在 BepInEx/cache/DIYChefFastInit 保存解析后的 OBJ 几何和解码后的纹理像素、原始格式及 mipmap。内存中已有模型仍直接复用；资源被内存缓存回收或游戏重新启动后，可从磁盘缓存恢复，跳过 OBJ 文本解析和 PNG 解码，仍需创建 Unity 对象并上传 GPU。

新增 [Loading] DiskCacheEnabled，默认 true。原有 CacheBudgetMiB、MaxCachedChefs 保留用户配置，不因磁盘缓存而增加。解码贴图每次最多保留一个后台写入缓冲区，并纳入临时内存诊断。

原文件使用 SHA-256 内容指纹，缓存使用结构版本及负载校验和；原资源在下次导入时变化、缓存损坏或结构不兼容时，回读原资源并重建。缓存写入失败不阻止原资源加载。删除该缓存目录会让后续使用重新生成缓存，不影响厨师 ID、选择和资源原文件。

离线验证与 OBJ 预生成命令：

```powershell
dotnet run --project tests/DiskCacheRegression.csproj -c Release
dotnet run --project tests/DiskCacheRegression.csproj -c Release -- --warm "<插件 Resources 目录>" "<游戏目录>/BepInEx/cache/DIYChefFastInit"
```

回归覆盖内容变化（包含大小和时间戳均不变）、命中、损坏、截断、结构版本、空 OBJ、纹理格式/mipmap、取消、禁用缓存和写入失败。离线预生成只处理 OBJ，不启动游戏；PNG 像素由游戏首次使用时按原加载方式生成缓存。完整切换耗时及纹理缓存的实际渲染表现仍需游戏内确认。

本机离线检查：19 项缓存断言通过；已在指定目录预生成 349 个 OBJ 缓存，并逐项验证再次读取后的顶点、法线、UV、三角形一致。独立工具的磁盘再读合计约 0.91 秒，此数值不代表游戏切换耗时。
