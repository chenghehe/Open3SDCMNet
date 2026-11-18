# Open3SDCMNet

[简体中文](README.md) · **English**

Open3SDCMNet is a managed C# library for .NET 10 (`net10.0`). It reads 3Shape dental scan meshes from DCM files in HPS XML format and exports STL, PLY, or OBJ with materials and textures.

The library supports HPS schemas `CA`, `CB`, `CC`, and `CE`. Input scans must already contain a triangle mesh. Medical DICOM slices require a separate volume-to-mesh reconstruction workflow.

## Project reference

Add a project reference to your .NET 10 application's `.csproj` file. The example assumes that your application directory and this repository are siblings. Adjust the path to match your layout.

```xml
<ItemGroup>
  <ProjectReference Include="../Open3SDCMNet/Open3SDCMNet.csproj" />
</ItemGroup>
```

The build restores `BouncyCastle.Cryptography`, which supplies the Blowfish implementation used to decode CE data blocks.

## Quick start

### Synchronous API

```csharp
using Open3SDCMNet;

var mesh = DcmReader.Read("scan.dcm");
MeshWriter.Write(mesh, "out/scan.stl");
```

### Asynchronous API

```csharp
using Open3SDCMNet;

var mesh = await DcmReader.ReadAsync("scan.dcm");
await MeshWriter.WriteAsync(mesh, "out/scan.stl");
```

## Export formats

`MeshWriter.Write` and `MeshWriter.WriteAsync` select a format from the output path's extension and overwrite existing output files with the same names.

| Extension | Output |
| --- | --- |
| `.stl`, `.stlb` | Binary STL with triangles and face normals. |
| `.ply` | ASCII PLY with vertices and triangles. When a base color is present, each vertex gets the same RGB values. |
| `.obj` | Wavefront OBJ. The exporter writes `.mtl` and `_texture0.jpg` companion files in the same directory when needed. It flips the V component of exported UV coordinates. |

## API reference

| Operation | Synchronous | Asynchronous |
| --- | --- | --- |
| Read DCM from a file or stream | `DcmReader.Read(path)` / `DcmReader.Read(stream)` | `DcmReader.ReadAsync(path)` / `DcmReader.ReadAsync(stream)` |
| Export to a file | `MeshWriter.Write(mesh, path)` | `MeshWriter.WriteAsync(mesh, path)` |
| Write STL to a stream | `MeshWriter.WriteStl(mesh, stream)` | `MeshWriter.WriteStlAsync(mesh, stream)` |
| Write PLY to a stream | `MeshWriter.WritePly(mesh, stream)` | `MeshWriter.WritePlyAsync(mesh, stream)` |

Reading starts at the stream's current position, and the caller is responsible for disposing of streams passed to the API. OBJ export takes a file path so the exporter can write material and texture files alongside the mesh. The async APIs use asynchronous I/O and accept an optional `CancellationToken`. Geometry decoding and mesh encoding run synchronously within the call.

### Mesh data and diagnostics

- `Schema` identifies the input file's HPS schema. `Vertices` stores single-precision 3D coordinates, and `Triangles` stores zero-based vertex indices.
- `BaseColor` is an optional color for the entire mesh. `CoordinateSets` and `Images` contain UV coordinates and image data. `GetTextureBinding()` returns the coordinate set and image used for export. UV coordinates are stored per triangle corner, and `null` marks a corner with no UV coordinate.
- Invalid HPS XML or geometry data raises `InvalidDataException`. Check `Warnings` for diagnostics such as CE vertex checksum mismatches and failures to decode optional UV data.

## Development and verification

Build the solution and run the regression tests from the repository root.

```sh
dotnet build Open3SDCMNet.slnx -c Release
dotnet test Open3SDCMNet.slnx -c Release
```

The tests cover triangle winding, 16-bit and 32-bit index fallback, truncated input, per-corner UV coordinates, asynchronous stream I/O, cancellation, and recovery of companion files after a failed OBJ export.

### Benchmarks

Place 3Shape DCM samples in `Files/` at the repository root, or set `OPEN3SDCM_BENCHMARK_INPUT_DIR` to the sample directory. The BenchmarkDotNet suite finds `.dcm` files regardless of extension casing.

Run a quick check of the synchronous and asynchronous reads from the repository root.

```sh
dotnet run --project benchmarks/Open3SDCMNet.Benchmarks.csproj -c Release -- --filter "*ReadDcm*" --job Dry --artifacts artifacts/benchmarks --noOverwrite
```

Remove `--job Dry` for a full measurement. Set the filter to `"*Convert100DcmToPly*"` for the synchronous and asynchronous 100-conversion benchmarks, or to `"*"` for the whole suite. CSV and Markdown reports are saved under `artifacts/benchmarks/`.

- Single-file benchmarks measure DCM reads, STL/PLY/OBJ writes, full DCM-to-PLY conversion, and managed allocations for each sample. Export-only cases load the mesh before timing begins.
- Concurrent benchmarks cycle through the sample files. Each reported operation is **one batch of 100 concurrent DCM-to-PLY conversions**.
- Export benchmarks write files to the system's temporary directory. Each case reuses its output paths and cleans up when it finishes. Use the same machine, samples, and command options when comparing versions.

## Credits and licenses

Original code is available under the MIT License. Portions adapted from third-party code are covered by the Boost Software License 1.0 (`BSL-1.0`). See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the license scope, original attribution, and full Boost license text.
