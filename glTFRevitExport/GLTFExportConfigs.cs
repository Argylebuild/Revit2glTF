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