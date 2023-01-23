using System;

using GLTF2BIM.GLTF;
using GLTF2BIM.GLTF.Schema;
using GLTF2BIM.GLTF.Extensions.BIM.Schema;
using GLTFRevitExport.Build.Actions.BaseTypes;

namespace GLTFRevitExport.Build.Actions {
    class ElementBoundsAction : BaseAction {
        protected glTFBIMBounds _bounds;

        public ElementBoundsAction(glTFBIMBounds bounds) => _bounds = bounds;

        public override void Execute(BuildContext ctx) {
            if (_bounds != null &&
                    ctx.Builder.GetActiveNode() is glTFNode activeNode) {
                Logger.Log("> bounds");
                UpdateBounds(
                    ctx.Builder,
                    ctx.Builder.GetNodeIndex(activeNode),
                    new glTFBIMBounds(_bounds)
                    );
            }
            else
                Logger.Log("x transform");
        }

        void UpdateBounds(GLTFBuilder gltf, uint idx, glTFBIMBounds bounds) {
            if (bounds != null) {
                glTFNode node = gltf.GetNode(idx);
                if (node.Extensions != null)
                    foreach (var ext in node.Extensions)
                        if (ext.Value is glTFBIMNodeExtension nodeExt) {
                            if (nodeExt.Bounds != null)
                                nodeExt.Bounds.Union(bounds);
                            else
                                nodeExt.Bounds = new glTFBIMBounds(bounds);

                            // if node has a parent, update the parent bounds
                            int parentIdx = gltf.FindParentNode(idx);
                            if (parentIdx >= 0)
                                UpdateBounds(gltf, (uint)parentIdx, nodeExt.Bounds);
                            // otherwise update the active scene bounds
                            else
                                UpdateSceneBounds(gltf, nodeExt.Bounds);
                        }
            }
        }

        void UpdateSceneBounds(GLTFBuilder gltf, glTFBIMBounds bounds) {
            if (bounds != null) {
                if (gltf.PeekScene() is glTFScene scene)
                    if (scene.Extensions != null)
                        foreach (var ext in scene.Extensions)
                            if (ext.Value is glTFBIMSceneExtension sceneExt) {
                                if (sceneExt.Bounds != null)
                                    sceneExt.Bounds.Union(bounds);
                                else
                                    sceneExt.Bounds = new glTFBIMBounds(bounds);

                                // update the asset bounds after updating scene
                                UpdateAssetBounds(gltf, sceneExt.Bounds);
                            }
            }
        }

        void UpdateAssetBounds(GLTFBuilder gltf, glTFBIMBounds bounds) {
            if (bounds != null) {
                if (gltf.PeekAsset() is glTFAsset asset)
                    if (asset.Extensions != null)
                        foreach (var ext in asset.Extensions)
                            if (ext.Value is glTFBIMAssetExtension assetExt) {
                                if (assetExt.Bounds != null)
                                    assetExt.Bounds.Union(bounds);
                                else
                                    assetExt.Bounds = new glTFBIMBounds(bounds);
                            }
            }
        }
    }
}
