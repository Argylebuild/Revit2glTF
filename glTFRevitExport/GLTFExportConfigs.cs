using System;
using System.Threading;
using Autodesk.Revit.DB;

using GLTF2BIM.GLTF.Schema;
using GLTFRevitExport.Extensions;
using GLTFRevitExport.Properties;

namespace GLTFRevitExport {
    /// <summary>
    /// Export configurations
    /// </summary>
    public class GLTFExportConfigs {
        /// <summary>
        /// Id of the generator
        /// </summary>
        public string GeneratorId => StringLib.GLTFGeneratorName;

        /// <summary>
        /// Generator copyright message
        /// </summary>
        public string CopyrightMessage;

        /// <summary>
        /// Export Revit type data
        /// </summary>
        public bool ExportHierarchy { get; set; } = true;

        /// <summary>
        /// Export linked Revit models
        /// </summary>
        public bool ExportLinkedModels { get; set; } = true;

        /// <summary>
        /// Embed linked Revit models
        /// </summary>
        public bool EmbedLinkedModels { get; set; } = false;

        /// <summary>
        /// Export Revit element parameter data
        /// </summary>
        public bool ExportParameters { get; set; } = true;

        /// <summary>
        /// Whether to embed parameter data inside glTF file or write to external file
        /// </summary>
        public bool EmbedParameters { get; set; } = false;

        /// <summary>
        /// Export Revit material data
        /// </summary>
        public bool ExportMaterials { get; set; } = false;

        /// <summary>
        /// Export material base color textures (embedded as data URIs) and
        /// per-vertex UVs. Only effective when ExportMaterials is true.
        /// </summary>
        public bool ExportTextures { get; set; } = false;

        /// <summary>
        /// Export thin-line geometry (model curves, MEP centerlines)
        /// as glTF LINES primitives
        /// </summary>
        public bool ExportLines { get; set; } = true;

        /// <summary>
        /// Safety cap on thin-line segments collected per element. Oversized
        /// linework is truncated with a warning. Zero or negative disables
        /// the cap
        /// </summary>
        public int MaxLineSegmentsPerElement { get; set; } = 500000;

        /// <summary>
        /// Pool project-scale CAD import (DWG) linework into spatial chunk
        /// nodes — one child node per occupied grid cell under the import's
        /// own node — so the AR app can stream large drawings by area.
        /// Revit element linework (model lines, MEP centerlines) always
        /// keeps per-element nodes; CAD imports nested inside families flow
        /// into the family element like any other family geometry
        /// </summary>
        public bool ChunkImportLinework { get; set; } = true;

        /// <summary>
        /// Horizontal (plan) size of a linework chunk cell, in meters
        /// </summary>
        public float LineChunkSizeHorizontal { get; set; } = 10f;

        /// <summary>
        /// Vertical size of a linework chunk cell, in meters. Shorter than
        /// the horizontal size so each storey's linework chunks separately
        /// </summary>
        public float LineChunkSizeVertical { get; set; } = 4f;

        /// <summary>
        /// Target max segments per chunk node; an overflowing cell splits in
        /// half per axis recursively (keeps per-node meshes near Unity's
        /// 16-bit index boundary and mesh builds frame-friendly)
        /// </summary>
        public int MaxLineSegmentsPerNode { get; set; } = 50000;

        /// <summary>
        /// Write the export log to a timestamped file (works in release
        /// builds; the Revit-side equivalent of the AR app's ArgyleLogs)
        /// </summary>
        public bool LogToFile { get; set; } = true;

        /// <summary>
        /// Directory for export log files. Null uses
        /// %LOCALAPPDATA%\Argyle\ExportLogs
        /// </summary>
        public string LogDirectory { get; set; } = null;

        /// <summary>
        /// Cancellation toke for cancelling the export progress
        /// </summary>
        public CancellationToken CancelToken;

        public Color DefaultColor = new Color(255, 255, 255);

        /// <summary>
        /// Export all buffers into a single binary file
        /// </summary>
        public bool UseSingleBinary { get; set; } = false;

        /// <summary>
        /// Maximum binary size, default 50MB
        /// </summary>
        public int MaxBinarySize { get; set; } = 50 * 1024 * 1024;

        public delegate glTFExtras GLTFExtrasBuilder(object node);
        public GLTFExtrasBuilder ExtrasBuilder;

        public bool HasExtrasBuilder => ExtrasBuilder != null;

        internal glTFExtras BuildExtras(object node)
            => ExtrasBuilder?.Invoke(node);

        public delegate void GLTFProgressReporter(float value);
        public GLTFProgressReporter ProgressReporter;

        internal void UpdateProgress(float value)
            => ProgressReporter?.Invoke(value);
    }
}