# 纹理缓存压缩实测与实现

本机 55 份纹理缓存原大小为 1041.34 MiB。推荐并已实现 LZ4HC level 9 写入、原生 x86 LZ4 解压：实际转换后为 89.44 MiB，节省 91.4%，解压像素逐份通过原有完整 SHA-256 验证。正式插件版本为 1.3.0.8，未启动游戏。

## 压缩率

对所有缓存先验证旧负载校验，再压缩全部像素和 mipmap。下表包括每文件原有 96 字节头部的估算，正式新格式每文件多 8 字节。所有方案均验证解压数据与输入逐字节一致。

| 方案 | 55 份总大小 | 节省 | 本机 64 位生成端总编码时间 |
| --- | ---: | ---: | ---: |
| 原始像素 | 1041.34 MiB | — | — |
| LZ4 默认 | 134.08 MiB | 87.12% | 1.17 s |
| LZ4HC 9 | 89.44 MiB | 91.41% | 41.96 s |
| Deflate 6 | 76.98 MiB | 92.61% | 21.34 s |
| Zstd 3 | 82.54 MiB | 92.07% | 3.02 s |
| Windows MSZIP | 77.50 MiB | 92.56% | 47.72 s |
| Windows XPRESS | 164.10 MiB | 84.24% | 2.67 s |
| Windows XPRESS_HUFF | 114.19 MiB | 89.03% | 3.52 s |

编码速度来自离线 Python/native 实验，不代表游戏内时间。实际独立 x86 Mono 转换 55 份并往返校验耗时 51.32 s。缓存格式 2 的实际总大小为 93,786,778 字节。

## 目标 Mono 读取速度

独立 x86 宿主只加载游戏的 Mono，不加载 UnityPlayer，不创建 Unity 对象。每项预热一次、测五次取中位数，GC 在计时外执行；文件已在 OS 页缓存中，不代表冷磁盘读取。输出在原生内存中，解压后 SHA-256 完整核对原始像素。GPU 上传、源 PNG 指纹、模型构建和游戏调度均不在计时内。

| 方案 | Anya，21.33 MiB | 熊，21.33 MiB | 企鹅，1.33 MiB |
| --- | ---: | ---: | ---: |
| 原始缓存读取与像素校验 | 37.60 ms | 35.54 ms | 4.06 ms |
| 原生 LZ4 默认 | 44.81 ms | 48.23 ms | 4.00 ms |
| 原生 LZ4HC 9 | 37.20 ms | 42.78 ms | 4.63 ms |
| C# LZ4HC 解码原型 | 45.73 ms | 68.94 ms | 7.49 ms |
| 原生 Zstd 3 | 67.05 ms | 82.63 ms | 7.23 ms |
| Windows XPRESS | 47.86 ms | 54.57 ms | 4.74 ms |
| Windows XPRESS_HUFF | 63.33 ms | 91.65 ms | 7.03 ms |
| Windows MSZIP | 102.87 ms | 148.15 ms | 12.07 ms |

DeflateStream 在该独立 Mono 宿主中因 MonoPosixHelper 缺失而无法测量，不能将其当作已验证的现成依赖方案。这不证明完整游戏进程无法提供相关实现。

LZ4HC 与默认 LZ4 使用同一块格式和解码器。当前三份样本里 HC 产物更小，原生解码约 0.8–11 ms；C# 原型在旧 Mono 上显著更慢。原生 LZ4HC 是本机数据中较好的空间和读取速度折中，但熊样本仍增加约 7 ms，不能宣称所有读取必然加速。

另测只校验压缩负载、再解压的路径：Anya 的原生 LZ4HC 为 20.19 ms。但正式实现仍校验解压后的像素，未用这一较小数字替代完整验证耗时。

## 文件压缩与 GPU 压缩

本机缓存均为 ARGB32，顺序与 [Unity 文档](https://docs.unity3d.com/2017.4/Documentation/ScriptReference/TextureFormat.ARGB32.html)一致。读取源 PNG 的 alpha 直方图独立核对后，48 份底层图像完全不透明，7 份含中间透明度。

本次 LZ4HC 是无损文件压缩，恢复出的格式和像素不变，显存、上传字节数及驻留预算不变。[Unity 的 Texture2D.Compress](https://docs.unity3d.com/2017.4/Documentation/ScriptReference/Texture2D.Compress.html) 属于 DXT 纹理格式转换，会涉及画质变化；本轮没有实现或以游戏画面验证 DXT，因此未将它作为默认方案。

## 实现

- Lz4Codec.cs 嵌入固定版本官方 x86 库，释放到缓存 native 目录，显式按绝对路径加载；库随进程存活，避免退出时后台任务使用已卸载句柄。
- 纹理格式 2 在原 76 字节外层头后保存原 20 字节像素元数据、codec、存储长度和负载。codec 1 为 LZ4 块，codec 0 为无压缩收益的原始字节。网格保持格式 1。
- 校验仍覆盖原元数据和完整解压像素；读取先验证尺寸、mipmap、存储长度和输出长度，失败回读 PNG。
- 快速 PNG 写入与低优先级 HC 转换分开，前台不会等待编码。单个转换任务使用一份输入与编码工作区，释放后再验证临时产物。
- 验证通过后，在发布锁内核对源指纹、旧校验和及格式，避免覆盖并发的新缓存，再原子替换。退出、取消或失败保留有效旧文件。
- 启动扫描与新 PNG 写入都会排队转换；已处理的格式 2 只检查头部，不重复编码。

81 项目标 Mono 回归通过，包含新旧格式、完整像素一致性、损坏和截断、取消清理、并发源更新、无收益回退、后台转换、启动扫描和退出。55 份实际缓存均已转换并完整核对。本轮未启动游戏，最终切换体验由用户测试。

## 重现

```powershell
./tools/Fetch-Lz4.ps1
./tests/Run-DiskCacheMonoRegression.ps1
./tools/Compress-TextureCache.ps1
uv run tools/research/texture-compression/prepare_samples.py --cache "<缓存目录>" --resources "<Resources目录>" --output artifacts/texture-compression
./tools/research/texture-compression/Run-Probe.ps1 -Lz4Dll lib/lz4-win-x86.dll
```

可选的 Zstd 对照使用其 [官方 v1.5.7 x86 发布包](https://github.com/facebook/zstd/releases/tag/v1.5.7)，通过 Run-Probe.ps1 的 ZstdDll 参数指定 DLL。实验数据与二进制在忽略提交的 artifacts 中；脚本、依赖锁和测试源码保留在仓库。算法与 API 来源：[LZ4 块格式](https://github.com/lz4/lz4/blob/dev/doc/lz4_Block_format.md)、[Windows Compression API](https://learn.microsoft.com/en-us/windows/win32/cmpapi/using-the-compression-api)。
