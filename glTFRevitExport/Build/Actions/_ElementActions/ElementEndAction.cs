using System;

using GLTFRevitExport.GLTF;
using GLTFRevitExport.Build.Actions.BaseTypes;

namespace GLTFRevitExport.Build.Actions {
    class ElementEndAction : BuildEndAction {
        public override void Execute(BuildContext ctx) {
            Logger.Log("- element end");

            // close instance node
            ctx.Builder.CloseNode();
            // close type node
            if (ctx.Configs.ExportHierarchy)
                ctx.Builder.CloseNode();
        }
    }
}
