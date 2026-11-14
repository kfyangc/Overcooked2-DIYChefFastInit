# 头像缓存迁移回归

直接编译生产 `ChefGridPortraitCache`、磁盘容器、SHA-256、原生像素缓冲、后台写队列及 LZ4。测试替换 Unity `Texture2D`、`Object` 和格式枚举，不加载 UnityPlayer 或启动游戏。

`TextureStubs.cs` 中的 `LoadImage` 读取确定性的解码结果夹具，文件扩展名沿用 `.png` 以测试迁移路径；它不是 PNG 解码器。测试验证分支、所有权、尺寸、格式、每个像素字节和 API 调用次数，不证明实际 PNG 解码速度、GPU 上传性能或画面效果。真实 PNG 的离线解码研究单独进行。

```powershell
dotnet run --project tests/PortraitCacheRegression.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tests/Run-PortraitCacheMonoRegression.ps1
```

覆盖旧图自动迁移、重复读取绕过 PNG、作者预览优先级、资源版本路径变化、RGBA/ARGB/BGRA/RGB24、透明像素、128 与 512 独立、缩略图下采样回调、尺寸与 mipmap 校验、损坏/截断回退修复、上传异常清理、生成图直接保存二进制、完整 50 张缩略图队列、禁用缓存和关闭后拒绝写入。

所有夹具及输出均在忽略提交的 `artifacts/portrait-cache-regression/`。64 位 .NET 运行原始容器路径；游戏自带 x86 Mono 额外加载嵌入的官方 LZ4 库，验证压缩后的缩略图和高清图仍逐字节一致。
