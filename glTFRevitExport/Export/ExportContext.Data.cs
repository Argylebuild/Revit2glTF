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
    sealed partial class ExportContext : IExportContext, IExportContextBase, IModelExportContext {
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
        /// Nesting depth of linked documents being collected (0 = host).
        /// Link geometry is collected in HOST coordinates — OnLinkBegin
        /// pushes the link transform onto the transform stack — so element
        /// bounds computed inside a link are already host-space bounds
        /// </summary>
        int _linkDepth = 0;

        /// <summary>
        /// Export origin in Revit internal coordinates (feet, Z-up),
        /// subtracted in double precision from every point, placement and
        /// bounds value before it is cast to float32. Zero (no rebase) for
        /// content near the internal origin, so such exports are unchanged.
        /// Chosen once per export session — see SetExportOriginOffset — and
        /// published as EXT_bim_metadata "originOffset" (glTF space) so
        /// consumers can reconstruct absolute coordinates in double
        /// </summary>
        XYZ _originOffset = XYZ.Zero;

        /// <summary>
        /// Whether the export origin has been chosen for this session
        /// </summary>
        bool _originOffsetSet = false;

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

        readonly Stack<Transform> _transformStack = new Stack<Transform>();

        /// <summary>
        /// Element currently being processed, set at OnElementBegin.
        /// Used as fallback context for line color and weight lookups
        /// </summary>
        Element _currentElement = null;

        /// <summary>
        /// Line geometry parts collected for the current element, keyed by
        /// line color. Kept out of the part stack until OnElementEnd so they
        /// do not interfere with the OnMaterial/OnPolymesh mesh collection
        /// </summary>
        readonly Dictionary<string, PartData> _elementLineParts = new Dictionary<string, PartData>();

        /// <summary>
        /// Thin-line segments collected so far for the current element,
        /// enforcing GLTFExportConfigs.MaxLineSegmentsPerElement
        /// </summary>
        int _elementLineSegmentCount = 0;

        /// <summary>
        /// Whether the segment-budget warning was already logged for the
        /// current element (one warning per element, not per curve)
        /// </summary>
        bool _elementLineBudgetWarned = false;
    }
}
