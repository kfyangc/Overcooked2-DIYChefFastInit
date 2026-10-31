# 厨师切换路径与缓存开销分析

结论：游戏原版在选择阶段复用已导入、已加载的模型与材质；很多头像甚至共用同一预制体，只切换节点显隐。当前插件的磁盘缓存命中仍会扫描源文件、逐份校验完整负载，并重新构建 Unity 对象。现有日志与目标 Mono 离线探针均表明，1.3.0.4 的主要等待来自缓存校验和数据恢复，首先应优化这一段。

本文原始调查针对 1.3.0.4；1.3.0.5 的校验优化与复测结果见文末。

## 调研范围与证据

分析基线为提交 e28db67。读取本机实际 Assembly-CSharp.dll 的调用链、原始 StreamingAssets 资源和当前 BepInEx 日志。独立 x86 控制台进程加载游戏自带的 Mono/EmbedRuntime/mono.dll，仅执行文件、数组和校验代码；没有启动 Overcooked2.exe、加载 UnityPlayer、创建 Unity 对象或进行渲染测试。

原始数据快照和完整探针输出保存在本地 artifacts/chef-switch-research。可复现的 Mono 探针源码位于 tools/research/chef-cache-probe；不包含游戏程序集或资源二进制文件。

## 原版切换快的具体路径

### 共享预制体，跳过模型替换

ChefMeshReplacer.SetChefData 根据头像与颜色变化调用 ReplaceModel。ReplaceModel 按 FrontEnd/UI/InGame 取得目标预制体，并比较 m_lastModelPrefab：

- 预制体相同：直接返回，不实例化新模型，也不销毁当前模型。
- 预制体不同：实例化已经导入的预制体，配置动画与组件、颜色，销毁旧实例。
- 随后 SetHeadVisibility 关闭已缓存的头部节点，启用所选头部。
- 仅颜色变化时还有直接 AssignBodyColour 的路径。

已离线检查原版 StreamingAssets/Windows/bundle18 的基础头像定义：30 个 ChefAvatarData，在三种型号下各引用 4 个不同预制体，其中 27 个头像共用一套预制体。其 FrontendModelPrefab 指向 bundle29 的 CHR_FrontEnd_Chef_01；该预制体的 Mesh 下有 34 个直接子节点，29 个为 Chef_ 头部节点。

因此，对这 27 个头像进行连续切换时，原版命中的是已有模型内的节点显隐路径。这是已核实的基础资产组，不能推广为所有 DLC 或特殊 BODY 都共用同一个预制体。

### 原版资源已完成导入

原版 ChefAvatarData 直接引用序列化预制体，其中包含可直接使用的 Mesh、Material 和 Texture2D。选择代码中没有读取外部 OBJ/PNG、解析文本、重建蒙皮或重新计算切线的步骤。这些数据在资源制作与资源加载阶段已经准备。

bundle30、bundle31 中检查到的头部漫反射贴图使用 DXT1/DXT5。一个原版 2048×2048、12 级 mipmap 的 DXT1 示例，其 m_CompleteImageSize 为 2,796,216 字节，约 2.67 MiB。插件同尺寸 RGBA32 mip 链为约 21.33 MiB，约为该 DXT1 示例的 8 倍。该比较描述数据格式与体积，不能视为不同贴图的画质等价结论。

本次没有运行原版游戏，未测量其完整输入到首个实际显示帧的时长；上述结论来自实际调用链和原始资产引用。

## 当前缓存为何仍慢

### 游戏日志：命中后仍需约半秒

当前日志有 114 条完成应用的导入记录，其中 78 条读取了解码贴图缓存。各阶段独立中位数为：

| 指标 | 毫秒 |
| --- | ---: |
| 后台 readParseMs | 452.74 |
| 主线程贴图准备/提交 | 17.71 |
| prepareMs | 538.48 |
| inputToApplyMs | 547.85 |

这些是不同操作各指标的独立中位数，不应相加。

同一条 Anya 记录可拆解为：

| 阶段 | 毫秒 |
| --- | ---: |
| 后台缓存数据读取、源文件与负载校验 | 489.86 |
| 主线程创建/提交贴图 | 17.25 |
| 其余构建、调度与准备 | 约 48.96 |
| 输入到模型应用 | 563.97 |

该条记录 meshDiskHits=7、textureDiskHits=1，贴图 source=disk-pixels，确认磁盘缓存命中。inputToApplyMs 记录模型应用时刻；Diagnostics=false 时没有相机绘制回调记录，不能把它称为首个实际显示帧时间。贴图准备时间也是 CPU 调用时长，不是 GPU 硬件计时。

### 目标 Mono：完整 SHA-256 扫描占主要成本

1.3.0.4 的 AssetDiskCache.Open 每次命中仍会先用 SHA256Managed 扫描全部缓存负载，之后回到负载起点，再读取并恢复数据。ReadMesh 还会计算原始 OBJ 的完整内容指纹；PNG 也会完整读取并计算指纹。

使用实际游戏 x86 Mono（IntPtr.Size=4，CLR 2.0.50727.1433），对 Anya 的实际缓存测量。每项预热一次，再测 5 次并取中位数，GC 在计时区间外执行。一次可复现结果如下：

| 独立操作 | 毫秒 |
| --- | ---: |
| 读取原 PNG，1,294,192 字节 | 1.24 |
| PNG 的 SHA256Managed | 15.42 |
| 读取贴图缓存文件，22,369,716 字节 | 11.13 |
| 内存中的贴图负载 SHA256Managed | 262.96 |
| 同负载的 Windows 原生 SHA-256 | 19.78 |
| 从文件流扫描贴图负载 SHA256Managed | 313.42 |
| 实际 ReadTexture，包括完整验证 | 331.56 |
| 全部源 OBJ 的 SHA256Managed | 30.92 |
| 实际读取全部网格缓存，包括验证 | 98.25 |
| 主要贴图与网格导入循环 | 442.53 |

Windows 原生实现与托管实现的 SHA-256 结果一致。几轮独立探针中，主要导入循环约 443–502 ms，与游戏日志量级吻合。该循环未包含材质小文本读取、游戏线程竞争和 Unity 对象创建，不是完整游戏切换测量。

缓存完整性检查仍可保留：同一目标 Mono 内，原生 SHA-256 的探针约 20 ms，托管内存哈希约 263 ms。但原生实现尚未接入正式插件，不能把单项探针结果等同于完整切换加速。

之前现代 .NET 控制台的预生成/读取结果验证了缓存数据往返正确，没有验证旧 Mono 的运行性能。它不足以证明游戏内缓存读取已经足够快。

### 仍在重建模型，而且内存命中率低

当前 ResourceCache.Imported/GetPrefab 仅在对应 Unity 资源仍驻留时复用。被回收后，磁盘缓存只提供原始几何数组与贴图像素，仍要执行：

1. 创建 Texture2D，LoadRawTextureData 和 Apply。
2. 克隆 BODY 模板、裁剪其他头部、构造自制部件与材质。
3. 按渲染器重建权重、bindposes 和 Mesh，并 RecalculateTangents。
4. 构建完成后，由原版 ReplaceModel 再实例化一次显示模型。
5. 按 3 ms 主线程调度预算跨帧推进。

原版同预制体的头像切换跳过了这些步骤。

当前配置为 CacheBudgetMiB=128、MaxCachedChefs=6；这批高分辨率厨师每名估算约 31–36 MiB，字节预算通常只允许约 3–4 名同时驻留。日志存在大量 Evict，所以 MaxCachedChefs=6 并不表示实际保留了 6 名。遍历后回访常常重新进入上述导入与构建路径。

## 后续优化顺序

1. 先优化命中的读取路径。保留内容指纹和完整性能力，使用已验证的原生 SHA-256 或调整完整扫描的执行时机；减少一份负载扫描两遍和逐元素恢复的成本。目标首先是消除当前约半秒的后台等待。
2. 用接近原版的驻留资源与显示宿主复用。相同 BODY/型号保留宿主、骨架和动画状态，只切换准备好的部件、网格和材质；骨骼绑定与资源引用需要继续保持隔离。
3. 在预算内预取相邻厨师。当前请求优先，快速翻页后淘汰过期预取，保留即将使用的准备结果。
4. 降低贴图驻留与传输体积。评估适合透明度和画质要求的 GPU 压缩格式，提高同一预算下的驻留数量，再验证实际 shader 画面。
5. 继续分别测后台阶段、Unity 构建、应用和首个相机显示帧。完整效果需要实际游戏测试确认。

原始调研提交 46140af 没有修改正式插件；随后实施的校验优化与离线验证记录如下。

## 重现

在已有 Anya 网格与解码贴图缓存的本机执行：

```powershell
./tools/research/chef-cache-probe/Run-Probe.ps1
```

脚本可通过 GameDir、ResourceDir、CacheDir 参数指定其他安装与已缓存厨师。使用 .NET SDK 构建 net35 探针，通过 Windows 自带 .NET Framework C# 编译器生成 x86 宿主，再加载该安装的 Mono 运行时。输出为 artifacts/chef-switch-research/mono-benchmark.csv。

Unity 2017.4 API 文档同时说明：[LoadRawTextureData](https://docs.unity3d.com/2017.4/Documentation/ScriptReference/Texture2D.LoadRawTextureData.html) 之后仍需 Apply 提交纹理；[RecalculateTangents](https://docs.unity3d.com/2017.4/Documentation/ScriptReference/Mesh.RecalculateTangents.html) 会根据网格数据重新生成切线。这些成本在当前构建路径中仍然存在。

## 校验优化后的复测

1.3.0.5 将 Windows 原生 SHA-256 接入缓存内容指纹、读校验和写摘要计算，读取时校验同一份已读字节，写入时按缓冲块计算摘要。保留所有内容与负载校验，缓存格式不变。

同一探针、同一 Anya 缓存、同一游戏 x86 Mono 下，主要贴图与网格导入循环从 442.53 ms 降到 86.32 ms，约快 5.1 倍；贴图缓存读取与验证从 331.56 ms 降到 32.36 ms。网格恢复从 98.25 ms 降到 52.90 ms，剩余时间还包括内存数组恢复。

28 项离线缓存/摘要检查通过；349 个现有 OBJ 缓存全部命中并验证数据一致。没有启动游戏，没有以这些 CPU 探针数字替代完整输入到实际绘制的测量。模型构建和显示宿主复用不在本轮修改范围内。
