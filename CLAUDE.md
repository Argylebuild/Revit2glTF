# Argyle.glTFRevitExport Submodule — Claude Context

Forked from an open-source Revit-to-glTF exporter ([Revit2glTF](https://github.com/AEC-hackathon-Revit2glTF/Revit2glTF)). This submodule converts Revit 3D views into glTF model files with Argyle-specific vendor extensions for BIM data.

**This is a shared submodule** — it is used by the Revit plugin and potentially other exporters. The assembly is renamed to `Argyle.glTFRevitExport` to avoid DLL conflicts with other glTF libraries.

---

## Structure

```
├── glTFRevitExport/              .NET Framework 4.8 project (Revit 2024)
│   ├── GLTFExporter.cs           Public entry point — creates export context, exports views
│   ├── GLTFExportConfigs.cs      Configuration: hierarchy, links, params, materials, etc.
│   ├── Export/                   Revit CustomExporter (IExportContext) implementation
│   │   ├── ExportContext.cs              Main context class
│   │   ├── ExportContext.IExportContext.cs  Revit export callbacks
│   │   ├── ExportContext.Data.cs         Data extraction
│   │   └── ExportContext.Utils.cs        Utility methods
│   ├── Build/                    glTF document construction from collected Revit data
│   │   ├── BuildContext.cs       Orchestrates glTF assembly
│   │   ├── Actions/              Action pattern for scene/element/link/part processing
│   │   └── Geometry/             Facet, primitive, and vector data types
│   ├── GLTF.Extensions.BIM.Revit/  Vendor extensions for BIM metadata in glTF
│   ├── Collector/                Revit element collection utilities
│   ├── Extensions/               Extension methods (Revit API, numbers, strings)
│   └── Utils/                    Geometry and general utilities
├── GLTFRevitExport.Net8/         SDK-style wrapper for Revit 2025–2027 (same pattern as Argyle.RevitNet8;
│                                 net8.0-windows for 2025–2026, net10.0-windows for 2027)
└── lib/
    └── glTF2BIM/                 glTF schema types and package model (nested submodule)
```

---

## Dual-Project Pattern

Same approach as the main plugin:
- `glTFRevitExport/` — .NET Framework 4.8 project with explicit `<Compile>` items (Revit 2024 only; 2021–2023 were dropped in 2026-10)
- `GLTFRevitExport.Net8/` — SDK-style project that compiles `glTFRevitExport/**/*.cs` via glob. The target framework follows the configuration: `*2025`/`*2026` → `net8.0-windows`, `*2027` → `net10.0-windows`. `lib/glTF2BIM/GLTF2BIM.Net8/` follows the same rule (`net8.0` / `net10.0`). Add a new Revit year to `Configurations`, its property groups and `RevitVersion` group in both projects.

**Edit source files in `glTFRevitExport/` only.**

---

## How It Integrates with the Plugin

The plugin creates a `GLTFExporter` instance and configures it:

```csharp
var exportCfgs = project.ExportSettings.GLTFExportConfigs;
exportCfgs.ExtrasBuilder = (node) => {
    if (node is Document doc)
        return new ArgyleExtras(project);  // injects projectId
    return null;
};
exportCfgs.ProgressReporter = (value) => {
    _progressWindow.UpdateProgress(value, "Building Export Data");
};
var glTFExporter = new GLTFExporter(doc: project.Document, configs: exportCfgs);
foreach (View view in project.ExportSettings.ExportViews)
    glTFExporter.ExportView(view: view, filter: null);
var gltfPackageItems = glTFExporter.BuildGLTF(filter: null);
```

### Extension point: `ExtrasBuilder`
The `GLTFExportConfigs.ExtrasBuilder` delegate allows the calling plugin to inject custom data into glTF extras. The Revit plugin uses this to add `projectId` via `ArgyleExtras`.

### Extension point: `ProgressReporter`
Callback for export progress updates (0.0–1.0 float).

---

## Key Conventions

- **Partial classes** for `ExportContext` — split across multiple files by concern
- **Action pattern** in `Build/Actions/` — discrete operations for scene begin/end, element begin/end, etc.
- Assembly name is `Argyle.glTFRevitExport` (not the namespace `GLTFRevitExport`) to avoid conflicts
- Revit API version is resolved via `Revit_All_Main_Versions_API_x64` NuGet package
- `NonExportedElements` — static list tracking elements that couldn't be exported (shown to user in report)

---

## Dependency: glTF2BIM

Nested submodule at `lib/glTF2BIM/`. Provides:
- glTF schema types (`glTFExtras`, `glTFNode`, etc.)
- `GLTFPackageItem` hierarchy for serializing glTF output (model JSON, binary buffers, external files)

Also has a `.Net8` variant at `lib/glTF2BIM/GLTF2BIM.Net8/`.

---

## Coordinate Precision and Origin Rebase

Revit delivers geometry in internal coordinates (feet, Z-up) as doubles. Some customer models sit hundreds or thousands of kilometers from the internal origin (survey coordinates baked into the internal frame). At that magnitude float32 only resolves 6–12 cm, so any cast to float before localizing snaps every vertex to that grid and no later step can recover it (project 1821, 2026-10). Rules:

- `VectorData` holds **doubles**. The only double→float casts are `VectorData.ToArray()` (buffer write, 1/10 mm rounding) and the node-matrix translations built in `LocalizePartStack` / `EmitLineChunk`, which are small by then.
- **Export origin** (`ExportContext._originOffset`, chosen once per session in `SetExportOriginOffset` at the first `OnViewBegin`): per-axis median of the visible elements' bounding-box centers, snapped to a 1 km grid, applied only when the content center is more than 10 km from the internal origin (`RebaseThresholdMeters`, `OriginGridMeters` — same values as the Navisworks exporter). Near-origin models export exactly as before (no `originOffset`), which protects saved AR alignments.
- The origin is applied as the **base of the transform stack** (`BaseTransform`), so polymesh points, line geometry, instance and link geometry are rebased in double automatically. Build actions that convert absolute Revit coordinates themselves must subtract `ctx.OriginOffset` first: `LevelAction` (elevation, extents), `GridAction` (endpoints), `PartFromElementAction` (topography meshes). Add any new such path to this list.
- Published as `EXT_bim_metadata.originOffset` (glTF space `(x, z, -y) · 0.3048`, `double[3]`) on every file's asset extension via `BuildContext.OriginOffset`. The AR app clusters origins within 10 km and composes `originOffset − reference` in double, so rebased Revit and Navisworks exports of one site stay aligned.
- **Link bounds:** link geometry is collected in **host** coordinates (`OnLinkBegin` pushes the link transform onto the stack), so `LinkHostBounds` is a copy of the element bounds. Never re-apply the link matrix. The link node's `matrix` in the host file is informational; consumers must not apply it to the linked file's content.
