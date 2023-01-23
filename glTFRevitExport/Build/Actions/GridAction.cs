using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Autodesk.Revit.DB;

using GLTF2BIM.GLTF.Schema;
using GLTF2BIM.GLTF.Extensions.BIM.Schema;

using GLTFRevitExport.Extensions;
using GLTFRevitExport.Build.Actions.BaseTypes;
using GLTFRevitExport.GLTF.Extensions.BIM.Revit;

namespace GLTFRevitExport.Build.Actions {
    class GridAction : BuildBeginAction {
        public GridAction(Element element) : base(element) { }

        public override void Execute(BuildContext ctx) {
            Logger.Log("> grid");

            Grid grid = (Grid)element;

            // TODO: make a matrix from grid
            float[] gridMatrix = null;

            if (grid.Curve is Line gridLine) {
                // add gltf-bim extension data
                var gltfBim = new glTFRevitElementExt(grid, ctx);

                // grab the two ends of the grid line as grid bounds
                gltfBim.Bounds = new glTFBIMBounds(
                    gridLine.GetEndPoint(0).ToGLTFVector(),
                    gridLine.GetEndPoint(1).ToGLTFVector()
                );

                // create level node
                var gridNodeIdx = ctx.Builder.OpenNode(
                    name: grid.Name,
                    matrix: gridMatrix,
                    exts: new glTFExtension[] { gltfBim },
                    extras: ctx.Configs.BuildExtras(grid)
                );

                ctx.Builder.CloseNode();

                // record the grid in asset
                if (ctx.AssetExtension != null) {
                    if (ctx.AssetExtension.Grids is null)
                        ctx.AssetExtension.Grids = new List<uint>();
                    ctx.AssetExtension.Grids.Add(gridNodeIdx);
                }
            }

            // not need to do anything else
            return;
        }
    }
}