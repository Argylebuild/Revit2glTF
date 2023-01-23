using System;

using GLTF2BIM.GLTF;
using GLTF2BIM.GLTF.Schema;
using GLTFRevitExport.Build.Actions.BaseTypes;

namespace GLTFRevitExport.Build.Actions {
    class ElementTransformAction : BaseAction {
        public float[] Matrix;

        public ElementTransformAction(float[] matrix) => Matrix = matrix;

        public override void Execute(BuildContext ctx) {
            if (ctx.Builder.GetActiveNode() is glTFNode activeNode) {
                Logger.Log("> transform");
                activeNode.Matrix = Matrix;
            }
            else
                Logger.Log("x transform");
        }
    }
}
