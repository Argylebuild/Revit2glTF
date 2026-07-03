using GLTF2BIM.GLTF.Schema;
using GLTF2BIM.GLTF.Extensions.BIM.Schema;
using GLTFRevitExport.Build.Actions.BaseTypes;

namespace GLTFRevitExport.Build.Actions {
    class ElementLineWeightAction : BaseAction {
        readonly int _lineWeight;

        public ElementLineWeightAction(int lineWeight) => _lineWeight = lineWeight;

        public override void Execute(BuildContext ctx) {
            if (ctx.Builder.GetActiveNode() is glTFNode activeNode) {
                Logger.Log("> line weight");
                if (activeNode.Extensions != null)
                    foreach (var ext in activeNode.Extensions)
                        if (ext.Value is glTFBIMNodeExtension nodeExt) {
                            // keep the dominant (max) weight on the node
                            if (nodeExt.LineWeight is null
                                    || _lineWeight > nodeExt.LineWeight.Value)
                                nodeExt.LineWeight = _lineWeight;
                        }
            }
            else
                Logger.Log("x line weight");
        }
    }
}
