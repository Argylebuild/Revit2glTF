using System.Collections.Generic;

using GLTF2BIM.GLTF.Schema;
using GLTFRevitExport.Build.Actions.BaseTypes;
using GLTFRevitExport.GLTF.Extensions.BIM.Revit;

namespace GLTFRevitExport.Build.Actions {
    /// <summary>
    /// Opens a child node holding one spatial chunk of a CAD import's
    /// linework (see ExportContext.ChunkAndEnqueueImportLinework). Enqueued
    /// inside the import element's node scope so chunks become its children.
    /// Paired with LineChunkEndAction; PartFromDataAction /
    /// ElementBoundsAction / ElementLineWeightAction operate on the open
    /// chunk node between the two
    /// </summary>
    class LineChunkBeginAction : BaseAction {
        readonly string _id;
        readonly string _name;
        readonly List<string> _taxonomies;
        readonly float[] _matrix;

        public LineChunkBeginAction(string id, string name,
                                    List<string> taxonomies, float[] matrix) {
            _id = id;
            _name = name;
            _taxonomies = taxonomies;
            _matrix = matrix;
        }

        public override void Execute(BuildContext ctx) {
            Logger.Log("+ line chunk begin");

            // same taxonomies as the import element so the chunk lands in
            // the same category bucket in the AR app
            var ext = new glTFRevitElementExt {
                Id = _id
            };
            if (_taxonomies != null)
                ext.Taxonomies = new List<string>(_taxonomies);

            ctx.Builder.OpenNode(
                name: _name,
                matrix: _matrix,
                exts: new glTFExtension[] { ext },
                extras: null
            );
        }
    }
}
