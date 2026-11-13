# 代码分组与缓存路径审查

本轮先整理代码，再修改缓存行为。保留原命名空间、公开类型和 Harmony 补丁类型身份，避免改变插件及联机接口。

| 目录 | 职责 |
| --- | --- |
| `Plugin/` | BepInEx 入口、版本 |
| `Customisation/` | 资源登记、默认选择、取消恢复、原版选择入口补丁 |
| `Networking/` | DIY 消息、网络钩子、用户身份与选择同步 |
| `Loading/` | 按需调度、Unity 资源所有权、引用计数与回收 |
| `AssetPipeline/Import/` | 工作线程文件读取、OBJ 文本解析 |
| `AssetPipeline/Cache/` | 缓存容器、网格数据、纹理数据、后台压缩 |
| `AssetPipeline/Native/` | SHA-256、LZ4、临时原生像素内存 |
| `AssetPipeline/Models/` | 厨师与帽子模型、骨骼绑定、材质及渲染补丁 |
| `ChefGrid/` | 网格菜单、厨师列表 |
| `ChefGrid/Input/` | 输入规则、设备提示、加入退出保护 |
| `ChefGrid/Layout/` | 网格几何、导航、滚动条 |
| `ChefGrid/Lobby/` | 街机名字菜单、原版地图入口、输入所有权 |
| `ChefGrid/Portraits/` | 独立预览模型、姿势、取景、头像缓存 |
| `Compatibility/` | 可选外观插件兼容 |
| `Diagnostics/` | 进程内存诊断 |

`DIYChefCustomisation.cs` 原先混放的帽子、厨师资源、联机消息和绑定矩阵已各自拆分；混合的 `Patch.cs` 按选择、渲染、联机拆为同一个 partial 类型，原反射入口不变。磁盘缓存拆为文件容器、网格、纹理及像素所有权。构建项目、测试和研究工具均链接新位置。项目根目录的 `assets/` 仍只存文档图片。

审查发现：OBJ 原文件读取仍需要文本解析、坐标变换和面展开，不能替代 `.mesh`；资源 `.texture` 已支持原始像素及 LZ4；头像旧路径每次使用 `LoadImage`，新生成时还在主线程 `EncodeToPNG`。头像与大纹理应共享文件格式和校验实现，同时保持独立的小型后台写队列，以免头像迁移阻塞模型资源加载。

整理提交 `dc31956` 与父提交的独立审查对比了 93 个类型、918 个直接成员块：忽略注释、空白和新增的 `partial` 修饰后，方法体、字段、属性及 Harmony 标记的 token 均一致，没有缺失、增加或修改。拆分出的 partial 分片没有引入依赖跨文件初始化顺序的字段。后续缓存功能修改单独提交。
