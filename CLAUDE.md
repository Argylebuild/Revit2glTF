# Argyle.glTFRevitExport Submodule — Claude Context

Forked from an open-source Revit-to-glTF exporter ([Revit2glTF](https://github.com/AEC-hackathon-Revit2glTF/Revit2glTF)). This submodule converts Revit 3D views into glTF model files with Argyle-specific vendor extensions for BIM data.

**This is a shared submodule** — it is used by the Revit plugin and potentially other exporters. The assembly is renamed to `Argyle.glTFRevitExport` to avoid DLL conflicts with other glTF libraries.

---

## Structure

```
├── glTFRevitExport/              .NET Framework 4.8 project (Revit 2021–2024)
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
├── GLTFRevitExport.Net8/         .NET 8 wrapper (same pattern as Argyle.RevitNet8)
└── lib/
    └── glTF2BIM/                 glTF schema types and package model (nested submodule)
```

---

## Dual-Project Pattern

Same approach as the main plugin:
- `glTFRevitExport/` — .NET Framework 4.8 project with explicit `<Compile>` items
- `GLTFRevitExport.Net8/` — SDK-style .NET 8 project that compiles `glTFRevitExport/**/*.cs` via glob

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
