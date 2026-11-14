# 缓存格式调研与 2.2.0.21 方案

## `.obj` 和 `.mesh`

本机实际 Resources 下共 351 个 `.obj`，54,359,407 字节。全部严格 UTF-8 有效，没有 NUL 字节。最大文件 2,576,266 字节，开头为 Blender 4.0.2 注释、`mtllib`、`v` 等文本记录。它们是未压缩的 **OBJ 文本**，并非可直接上传 Unity 的二进制几何数组。

生产 `ObjImporter.ParseFile` 还要解析浮点文本、翻转 X 坐标、匹配位置/法线/UV 的独立索引、展开面顶点和三角化。`.mesh` 保存这些工作完成后的运行时数组：每个顶点 8 个 little-endian float，随后为 Int32 三角索引，外加源内容 SHA-256 和负载校验。它没有保存 Unity 对象或完整骨骼绑定。

在游戏自带 x86 Mono、Release 编译、操作系统暖文件缓存下，预热一次、7 次取中位；计时不包含 Unity 对象创建或 GPU 上传。缓存命中包含源哈希和负载校验：

| 工作量 | 直接解析 OBJ | `.mesh` 命中 |
| --- | ---: | ---: |
| 全部 351 个文件 | 5160.239 ms | 1099.029 ms |
| 最大 Head 文件 | 217.156 ms | 10.013 ms |
| ArcticFox 全部部件 | 90.225 ms | 21.864 ms |

所有 1,549,773 个展开顶点的位置、法线、UV，以及 1,890,579 个索引均逐项相同。缓存合计 57,184,536 字节，略大于源文本；其价值是减少解析与分配，并非压缩文件。因此 **保留 `.mesh`**。关闭磁盘缓存仍可直接解析 OBJ，但本机全库测量约慢 4.7 倍；这不是整次菜单打开耗时。

复现工具：[mesh probe](../tools/research/cache-formats/README.md)。本次原始记录：`artifacts/cache-formats/20261113-212209-304-d15b4d63d790438fbe323a67f8267771/mesh-format-results.txt`。初步非 Release 探针也得到相同方向，正式表采用可提交工具的 Release 结果；运行负载会带来波动。

## 资源 `.texture`

是从资源 `t_*.png` 解码而来的缓存。生产 `ResourceCache.Prepare` 首次调用 `LoadImage`，记录 **实际**纹理格式、宽高、mipmap 数和 `GetRawTextureData()` 字节；后续从缓存恢复像素，使用 `LoadRawTextureData` 与 `Apply` 上传。

schema 1 存未压缩像素；schema 2 存 LZ4HC level 9 无损压缩的像素，没压缩收益时保存 raw。解码后仍校验完整元数据和像素 SHA-256。LZ4 是磁盘存储压缩，不改变颜色、透明度、mipmap、显存或 GPU 上传量。原 PNG 仍用于内容指纹及缓存失败回退。

## 头像为什么改为二进制

旧头像缓存由独立模型 `ReadPixels → EncodeToPNG` 生成。PNG 节省空间、易于查看，但每次冷内存加载都要解码；新渲染时编码也在主线程执行。原始像素可以保存后直接恢复，Unity 2017.4 的 [`GetRawTextureData`](https://docs.unity3d.com/2017.4/Documentation/ScriptReference/Texture2D.GetRawTextureData.html) 和 [`LoadRawTextureData`](https://docs.unity3d.com/2017.4/Documentation/ScriptReference/Texture2D.LoadRawTextureData.html) 已支持这一流程。

实际读取 186 个旧头像：109 张 128×128、77 张 512×512。在独立游戏 x86 Mono 中，用 Windows GDI+ 解码真实 PNG 为 BGRA32，作为 PNG 的 CPU/I/O **代理**；再通过当前生产代码创建并读取完整 `.texture`。所有文件的 schema 1/2、managed/native 四条路径均核对元数据及像素逐字节一致；原生缓冲计数归零。

相同磁盘位置、暖文件缓存、9 轮中位数；生产读取包括头部检查、完整像素哈希与必要解压，批次共享哈希上下文。PNG 包含文件读取、GDI+ 解码与行拷贝：

| 方式 | 50 张缩略图 | 一张 512 大图 | 全部 77 张大图 |
| --- | ---: | ---: | ---: |
| PNG 代理 | 96.636 ms | 7.594 ms | 563.114 ms |
| 未压缩 `.texture` | 53.704 ms | 3.593 ms | 216.981 ms |
| LZ4 `.texture` | 59.177 ms | 3.761 ms | 228.534 ms |

| 文件组 | PNG | 未压缩容器 | LZ4 容器 |
| --- | ---: | ---: | ---: |
| 50 张缩略图 | 1,348,977 B | 3,281,600 B | 2,066,369 B |
| 全部 109 张缩略图 | 2,951,764 B | 7,153,888 B | 4,515,307 B |
| 全部 77 张大图 | 15,070,844 B | 80,747,744 B | 21,854,382 B |

**选择复用 LZ4 `.texture`**：其 CPU 开销接近 raw，体积比 PNG 约大 46%，远小于 raw；避免维护另一套容器、校验和解码器。热加载预期减少 PNG 解码工作，新渲染去掉主线程 PNG 编码。首次厨师模型加载、姿势计算、渲染和 GPU 上传仍需执行。

旧 PNG 保留用于回退与回滚，因此迁移后会同时占用空间；本机完整样本约为旧 PNG 18.0 MB 加新 LZ4 容器 26.4 MB。以后新生成的头像只创建二进制缓存。

表中不是 Unity `LoadImage` 或游戏帧耗时，不能把其比例等同于游戏提速。GDI+ 的颜色路径及行拷贝与 Unity 有差异；完整画面、颜色、透明度和菜单体验仍需游戏内复测。本轮没有启动游戏。复现工具、方法和完整数字见 [portrait probe](../tools/research/portrait-cache-formats/README.md)，原始记录在 `artifacts/portrait-cache-formats/`。

## 实现与兼容

- 先完成独立的代码结构整理提交，再修改缓存；目录说明见 [CODE_STRUCTURE.md](CODE_STRUCTURE.md)。
- 保留灰色衣服及 v8 视觉键；生成式头像的源版本已包含在文件名中，二进制读取无需重新读取源 PNG，但始终校验容器像素。资源 `.texture` 原有源内容校验仍保留。
- 读取顺序为同名 `.texture` → 作者 `preview.png` → 旧 v8 PNG → 原有模型渲染。128 与 512 单独缓存，右侧不使用裁剪缩略图。资源文件变化沿用原有键规则。
- 首次从 PNG 读取或下采样后转存实际格式及像素；不硬编码 PNG 的内存格式，不交换通道或翻转上下方向。生成头像直接取 raw 字节，不再 `EncodeToPNG`。
- 头像独立后台写队列按路径去重，活动与待写像素合计上限 8 MiB，模型资源写入优先；超限时保留显示和 PNG 回退，下次可重新迁移。关闭菜单不销毁全局写队列；插件退出停止队列并释放待写内存。
- 共用现有校验容器、临时文件和原子替换，再低优先级 LZ4 压缩；读取尺寸在分配像素前限额。启动扫描也覆盖 `portraits/`，上次退出留下的有效 raw 缓存可继续压缩。
- 保留旧 PNG、资源、配置与原有 `.mesh`/`.texture`，无需清缓存或重建全库。没有新增游戏运行时依赖。

## 验证

Release 插件 net35 构建零警告、零错误。缓存回归在 .NET 11 通过 185 项，游戏 x86 Mono 通过 206 项；头像迁移回归分别通过 115 / 125 项。包含 50 张头像完整迁移、队列限额和去重、模型写入优先、异常继续写、停止清理、PNG 优先级、缓存损坏恢复、实际纹理格式/透明像素保持、128/512 独立、尺寸分配前拒绝、递归扫描和原生内存释放。

头像回归直接编译生产缓存代码，Unity 图像解码及上传 API 使用明确替身，仅证明调用路径和数据约定。186 张真实 PNG 的 GDI+ 解码及完整二进制往返由性能探针独立覆盖，两者均不能替代 Unity 实际画面测试。[测试范围说明](../tests/PortraitCache/README.md)。

代码整理后原有默认选择、取消、网格、大厅/地图、名字菜单、材质所有权和外观兼容回归均通过；351 份真实 OBJ 也与原始导入器输出一致，绑定权重验证通过。网格的 669851 项、场景的 181 项和名字菜单的 397 项同时通过游戏自带 Mono。

运行缓存回归：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Run-DiskCacheMonoRegression.ps1
dotnet run --project tests/PortraitCacheRegression.csproj -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/Run-PortraitCacheMonoRegression.ps1
```

两个离线性能探针及运行方法见上文链接。所有测试与研究均未启动游戏，输出和隔离缓存只写工作区 `artifacts/`。
