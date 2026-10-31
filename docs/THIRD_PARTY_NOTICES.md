# 第三方组件

OC2DIYChef 嵌入 LZ4 1.9.4 官方 Windows x86 动态库，用于纹理缓存的无损 LZ4HC 编码与 LZ4 解码。库的许可证完整保存在 [third_party/lz4/LICENSE](../third_party/lz4/LICENSE)，同时嵌入发布 DLL，并在释放原生库时写入缓存 native 目录的 LZ4-LICENSE.txt。

- 官方源码：https://github.com/lz4/lz4/tree/v1.9.4
- 官方发布包：https://github.com/lz4/lz4/releases/download/v1.9.4/lz4_win32_v1_9_4.zip
- 发布包 SHA-256：C0321A555F522DF4FAD8D24FA6C53B4A8B65EAEBC00D40FB913803BC5D646282
- 原生 DLL SHA-256：803075F5021D5A1EB0B7FA75C6BB1675DDB01F1F4FC99F6E118A62F2B594C6C2

下载脚本为 tools/Fetch-Lz4.ps1。原生二进制作为构建依赖缓存在 lib 中，未提交到源码仓库。
