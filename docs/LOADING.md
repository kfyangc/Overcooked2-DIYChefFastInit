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

