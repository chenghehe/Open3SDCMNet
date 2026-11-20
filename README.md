# Open3SDCMNet

**简体中文** · [English](README.en.md)

Open3SDCMNet 是面向 .NET 10（`net10.0`）的纯托管 C# 类库。它读取 3Shape HPS XML 格式的 DCM 牙科扫描网格，并导出 STL、PLY 或带材质和纹理的 OBJ。

支持 HPS 的 `CA`、`CB`、`CC` 和 `CE` 格式。输入文件须包含三角网格，普通 DICOM 医学影像切片需要先重建网格。

## 引用项目

在你的 .NET 10 项目的 `.csproj` 中添加项目引用。下面假设调用项目目录与仓库目录同级，路径按实际位置调整。

```xml
<ItemGroup>
  <ProjectReference Include="../Open3SDCMNet/Open3SDCMNet.csproj" />
</ItemGroup>
```

构建时会还原 `BouncyCastle.Cryptography`，用于 CE 数据块的 Blowfish 解码。

## 快速开始

### 同步 API

```csharp
using Open3SDCMNet;

var mesh = DcmReader.Read("scan.dcm");
MeshWriter.Write(mesh, "out/scan.stl");
```

### 异步 API

```csharp
using Open3SDCMNet;

var mesh = await DcmReader.ReadAsync("scan.dcm");
await MeshWriter.WriteAsync(mesh, "out/scan.stl");
```

## 导出格式

`MeshWriter.Write` 和 `MeshWriter.WriteAsync` 根据输出路径的扩展名选择格式，并替换同名输出文件。

| 扩展名 | 输出内容 |
| --- | --- |
| `.stl`、`.stlb` | 二进制 STL，包含三角面和面法线。 |
| `.ply` | ASCII PLY，包含顶点、按相邻三角面面积加权计算的顶点法线和三角面。颜色优先从纹理与 UV 采样，其次使用原生顶点色，最后用底色。 |
| `.obj` | Wavefront OBJ，按需在同一目录生成 `.mtl` 和 `_texture0.jpg`。导出 UV 时翻转 V 坐标。 |

PLY 导出默认交换纹理采样的红、蓝通道，适配采用该通道顺序的 3Shape 扫描。当贴图已经是 RGB 顺序时，可用带 `swapTextureRedBlue: false` 的导出重载。异步重载同时接收 `cancellationToken`。

## API 参考

| 操作 | 同步 | 异步 |
| --- | --- | --- |
| 从文件或流读取 DCM | `DcmReader.Read(path 或 stream)` | `DcmReader.ReadAsync(path 或 stream)` |
| 导出到文件 | `MeshWriter.Write(mesh, path)` | `MeshWriter.WriteAsync(mesh, path)` |
| 导出 STL 到流 | `MeshWriter.WriteStl(mesh, stream)` | `MeshWriter.WriteStlAsync(mesh, stream)` |
| 导出 PLY 到流 | `MeshWriter.WritePly(mesh, stream)` | `MeshWriter.WritePlyAsync(mesh, stream)` |

流式读取从流的当前位置开始，传入的流由调用方负责释放。OBJ 导出使用文件路径，以便同时写入材质和纹理文件。异步 API 使用异步方法读写数据，并接受可选的 `CancellationToken`。几何解码和网格编码在调用过程中同步执行。

### 网格数据与诊断

- `Schema` 记录输入文件的 HPS 格式。`Vertices` 保存单精度三维坐标，`Triangles` 保存从零开始的顶点索引。
- `BaseColor` 是可选的整网格底色。`VertexColors` 保存从 BGRA `VertexColorSet` 读取的原生 RGB 顶点色，缺失时为空。`CoordinateSets` 和 `Images` 保存 UV 与图像数据，`GetTextureBinding()` 返回导出所用的坐标集和图像。UV 按三角面的角点排列，`null` 表示该角点缺少 UV。
- 无效的 HPS XML 或几何数据会抛出 `InvalidDataException`。CE 顶点校验和不匹配、可选 UV 数据解码失败等诊断可从 `Warnings` 读取。

## 开发与验证

在仓库根目录构建解决方案并运行回归测试。

```sh
dotnet build Open3SDCMNet.slnx -c Release
dotnet test Open3SDCMNet.slnx -c Release
```

测试覆盖面片绕序、16/32 位索引回退、截断输入、逐角 UV、异步流读写、取消和 OBJ 发布失败时的附属文件恢复。

### 性能基准

将 3Shape DCM 样本放到仓库根目录的 `Files/`，或将环境变量 `OPEN3SDCM_BENCHMARK_INPUT_DIR` 设为样本目录。BenchmarkDotNet 会查找目录中的 `.dcm` 文件，扩展名大小写均可。

在仓库根目录快速验证同步和异步读取基准。

```sh
dotnet run --project benchmarks/Open3SDCMNet.Benchmarks.csproj -c Release -- --filter "*ReadDcm*" --job Dry --artifacts artifacts/benchmarks --noOverwrite
```

去掉 `--job Dry` 即可正式测量。将筛选改为 `"*Convert100DcmToPly*"` 可运行同步和异步的 100 并发转换，改为 `"*"` 可运行全部用例。CSV 和 Markdown 报告保存在 `artifacts/benchmarks/` 下。

- 单文件基准按样本测量读取、STL/PLY/OBJ 写出、DCM → PLY 完整转换，并记录托管内存分配。只测导出时，网格在计时前读取。
- 并发基准轮流使用所有样本。报告中的一次操作是 **100 个 DCM → PLY 转换任务全部完成的一批**。
- 导出基准实际写入系统临时目录。各用例复用输出路径，并在结束后清理生成的文件。比较版本时使用相同的机器、样本和命令参数。

## 来源与许可证

原创代码采用 MIT 许可证，第三方移植部分采用 Boost Software License 1.0（`BSL-1.0`）。许可范围、原始署名和完整的 Boost 许可文本见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
