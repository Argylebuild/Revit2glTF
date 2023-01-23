using System;
using System.IO;
using System.Linq;

using Autodesk.Revit.DB;

using GLTF2BIM.GLTF;
using GLTF2BIM.GLTF.Schema;
using GLTF2BIM.GLTF.Extensions.BIM.Schema;
using GLTFRevitExport.Extensions;
using GLTFRevitExport.Build.Actions.BaseTypes;
using GLTFRevitExport.Properties;
using GLTFRevitExport.GLTF.Extensions.BIM.Revit;

namespace GLTFRevitExport.Build.Actions {
    class SceneBeginAction : BuildBeginAction {
        public View SceneView => element as View;

        public SceneBeginAction(View view) : base(view) { }

        public override void Execute(BuildContext ctx) {
            // start a new gltf scene
            Logger.Log("+ view begin");
            ctx.Builder.OpenScene(
                name: element.Name,
                exts: new glTFExtension[] {
                    new glTFRevitViewExt(SceneView, ctx)
                },
                extras: ctx.Configs.BuildExtras(element)
                );

            // open a root node for the scene
            ctx.Builder.OpenNode(
                name: string.Format(StringLib.SceneRootNodeName, element.Name),
                matrix: Transform.CreateTranslation(new XYZ(0, 0, 0)).ToGLTF(),
                exts: new glTFExtension[] {
                    new glTFRevitElementExt()
                },
                extras: null
                );
        }
    }
}
