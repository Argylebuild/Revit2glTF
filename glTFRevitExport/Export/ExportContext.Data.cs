using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using GLTFRevitExport.Extensions;
using GLTFRevitExport.GLTF.Extensions.BIM;
using GLTFRevitExport.Build.Actions.BaseTypes;
using GLTFRevitExport.Build.Geometry;

namespace GLTFRevitExport.Export {
#if REVIT2019
    sealed partial class ExportContext : IExportContext, IModelExportContext {
#else
    sealed partial class ExportContext : IExportContext, IExportContextBase, IModelExportContext {
#endif
        /// <summary>
        /// Configurations for the active export
        /// </summary>
        readonly GLTFExportConfigs _cfgs = new GLTFExportConfigs();

        /// <summary>
        /// Document stack to hold the documents being processed.
        /// A stack is used to allow processing nested documents (linked docs)
        /// </summary>
        readonly Stack<Document> _docStack = new Stack<Document>();

        /// <summary>
        /// Transform of the embedded document
        /// </summary>
        float[] _linkMatrix = null;

        /// <summary>
        /// View stack to hold the view being processed.
        /// A stack is used to allow referencing view when needed.
        /// It is not expected for this stack to hold more than one view,
        /// however stack has been used for consistency
        /// </summary>
        readonly Stack<View> _viewStack = new Stack<View>();

        /// <summary>
        /// Queue of actions collected during export. These actions are then
        /// played back on each .Build call to create separate glTF outputs
        /// </summary>
        readonly Queue<BaseAction> _actions = new Queue<BaseAction>();

        /// <summary>
        /// List of processed elements by their unique id
        /// </summary>
        readonly List<string> _processed = new List<string>();

        /// <summary>
        /// Flag to mark current node as skipped
        /// </summary>
        bool _skipElement = false;


        readonly Stack<PartData> _partStack = new Stack<PartData>();
    }
}
