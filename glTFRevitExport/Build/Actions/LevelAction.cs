using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Autodesk.Revit.DB;

using GLTF2BIM.GLTF;
using GLTF2BIM.GLTF.Schema;
using GLTF2BIM.GLTF.Extensions.BIM.Schema;
using GLTFRevitExport.Extensions;
using GLTFRevitExport.Build.Actions.BaseTypes;
using GLTFRevitExport.GLTF.Extensions.BIM.Revit;

namespace GLTFRevitExport.Build.Actions {
    class LevelAction : BuildBeginAction {
        BoundingBoxXYZ _extentsBbox;

        public LevelAction(Element element, BoundingBoxXYZ extents) : base(element) {
            _extentsBbox = extents;
        }

        public override void Execute(BuildContext ctx) {
            Logger.Log("> level");

            Level level = (Level)element;

            // make a matrix from level elevation
            float elev = level.ProjectElevation.ToGLTFLength();
            float[] elevMatrix = null;
            // no matrix is specified for a level at elev 0
            if (elev != 0f) {
                elevMatrix = new float[16] {
                            1f,   0f,   0f,   0f,
                            0f,   1f,   0f,   0f,
                            0f,   0f,   1f,   0f,
                            0f,   elev, 0f,   1f
                        };
            }

            // create level node
            var levelNodeIdx = ctx.Builder.OpenNode(
                name: level.Name,
                matrix: elevMatrix,
                exts: new glTFExtension[] {
                        new glTFRevitElementExt(level, ctx)
                },
                extras: ctx.Configs.BuildExtras(level)
            );

            // set level bounds
            if (_extentsBbox != null) {
                var bounds = _extentsBbox.TOGLTFBounds();
                glTFNode node = ctx.Builder.GetNode(levelNodeIdx);
                if (node.Extensions != null) {
                    foreach (var ext in node.Extensions) {
                        if (ext.Value is glTFBIMNodeExtension nodeExt) {
                            if (nodeExt.Bounds != null)
                                nodeExt.Bounds.Union(bounds);
                            else
                                nodeExt.Bounds = new glTFBIMBounds(bounds);
                        }
                    }
                }
            }

            ctx.Builder.CloseNode();

            // record the level in asset
            if (ctx.AssetExtension != null) {
                if (ctx.AssetExtension.Levels is null)
                    ctx.AssetExtension.Levels = new List<uint>();
                ctx.AssetExtension.Levels.Add(levelNodeIdx);
            }

            // not need to do anything else
            return;
        }
    }
}