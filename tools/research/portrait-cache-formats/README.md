# 头像缓存格式离线探针

比较真实头像 PNG 与生产 `.texture` schema 1 / schema 2。链接当前 `AssetPipeline` 源码，使用生产容器写入、LZ4HC 压缩、元数据检查及像素 SHA-256 校验，不维护缓存实现的副本。

探针仅通过已有 `MonoCacheHost.cs` 加载游戏的 x86 Mono DLL，**不会启动游戏或 Unity 场景**。PNG 使用 Windows 原生 GDI+ 解码为直通 alpha 的 BGRA32，作为 CPU 解码代理。它不调用 Unity `LoadImage`，也不测 GPU 上传、首次模型渲染或游戏帧耗时。

## 运行

在仓库根目录执行，需要 Windows、.NET SDK、现有 `lib/lz4-win-x86.dll` 和游戏 Managed/Mono DLL：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/research/portrait-cache-formats/Run-PortraitCacheProbe.ps1
```

可指定游戏和 PNG 目录：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/research/portrait-cache-formats/Run-PortraitCacheProbe.ps1 -GameDir 'D:/Games/Steam Games/steamapps/common/Overcooked! 2' -SourceDirectory 'C:/path/to/portrait-pngs'
```

默认只读取游戏 `BepInEx/cache/DIYChefFastInit/portraits/*.png`。输入需为 128×128 或 512×512 PNG；不修改游戏目录，不安装 DLL，不删除原缓存。编译文件、PNG 副本、两种二进制容器和报告全部写到仓库 `artifacts/portrait-cache-formats/`。

## 输出和覆盖范围

- `samples.csv`：每个文件的尺寸、完整容器字节数、源 PNG SHA-256、解码 BGRA SHA-256。
- `timings.csv`：每组各阶段的九轮中位数，单位 ms。
- `probe.txt`：运行时位数、版本、生产压缩和像素验证结果、总尺寸及耗时。
- `png/`、`schema1/`、`schema2/`：同一磁盘上的源副本、未压缩容器和 LZ4HC 容器。

每个文件通过生产 `WriteTextureFile` 写入 schema 1，再复制并调用生产 `CompressTextureCache` 转成 schema 2。随后用 `ReadTextureFile` 的 managed/native 两条路径读取两种容器，核对所有元数据及像素逐字节相等；校验和也是生产读路径的一部分。退出前验证 native 像素缓冲计数归零。

计时阶段分别测量 PNG 文件读取、内存 PNG 解码、PNG 读取加解码，以及两种生产容器的完整读取/解码/校验。生产读取带 512 尺寸上限；以生成式头像缓存为例，不再读取或计算源 PNG 的哈希，但始终验证容器的元数据和像素哈希。

分组为前 50 张按文件名排序的 128 缩略图、全部 128 缩略图、全部 512 详情图和第一张 512 详情图。前 50 张代表一屏工作量，不表示它们在实际游戏中同时出现。每组操作先预热，再做九轮，轮前 GC；50 张组每轮重复三次并除以三。源 PNG 和生成容器已在预热时进入文件系统缓存，因此结果属于暖文件缓存测试。

## 如何解释

`.texture` 数字来自实际生产路径，包含完整文件头、尺寸/格式/mips 校验、读取、必要的 LZ4 解压和元数据/像素 SHA-256。PNG 数字来自 GDI+，包含 BGRA 行拷贝；它与 Unity 的 PNG 实现不同。两者可用于判断格式工作量及方案潜力，**不能据此宣称某个百分比的游戏菜单提速**。BGRA 样本也不代替游戏中透明度、颜色和上下方向的视觉检查。

原始像素读取通常 CPU 成本最低，但占用磁盘更多。LZ4 保留无损像素且减少磁盘读取；是否值得由生成的容器尺寸和完整读取计时共同决定。首次模型加载和 GPU 上传仍由游戏承担。

相关 Unity API：[`LoadImage`](https://docs.unity3d.com/2018.4/Documentation/ScriptReference/ImageConversion.LoadImage.html) 自动上传纹理并可能改变格式；[`GetRawTextureData`](https://docs.unity3d.com/2018.4/Documentation/ScriptReference/Texture2D.GetRawTextureData.html) 的 byte[] 重载可保存可读纹理的系统内存副本；[`LoadRawTextureData`](https://docs.unity3d.com/2018.4/Documentation/ScriptReference/Texture2D.LoadRawTextureData.html) 需匹配尺寸、格式和 mip 链，再通过 Apply 上传。

## 2026-11-14 实测

在当前游戏 x86 Mono（指针 4 字节，CLR 2.0.50727.1433）读取 186 个原缓存文件：109 个 128 缩略图和 77 个 512 详情图。全部文件通过 schema 1 / 2、managed / native 四条读取路径的元数据及像素逐字节校验。以下二进制数据来自完整生产容器，并包含生产校验开销；原始记录在 `artifacts/portrait-cache-formats/`。

| 文件组 | PNG 字节 | schema 1 字节 | schema 2 字节 |
|---|---:|---:|---:|
| 50 张缩略图 | 1,348,977 | 3,281,600 | 2,066,369 |
| 109 张缩略图 | 2,951,764 | 7,153,888 | 4,515,307 |
| 77 张详情图 | 15,070,844 | 80,747,744 | 21,854,382 |

| 读取方式，中位数 ms | 50 张缩略图 | 1 张详情图 | 77 张详情图 |
|---|---:|---:|---:|
| PNG 读取 + GDI+ 解码及行拷贝代理 | 96.6357 | 7.5939 | 563.1137 |
| schema 1，native 读取及校验 | 53.7035 | 3.5933 | 216.9806 |
| schema 2，native 读取、解码及校验 | 59.1767 | 3.7613 | 228.5338 |

结果支持采用 LZ4 二进制缓存：读取 CPU 成本接近 raw，512 图的磁盘占用明显降低；PNG 仍更小。PNG 的列使用代理解码器，不能把这个表的相对比例等同于实际游戏菜单提速。首次探索的裸 `.raw` / `.lz4` 计时保留在 `artifacts/portrait-cache-research/`，其格式没有生产容器头；本节和本工具使用完整 schema 1 / 2。
