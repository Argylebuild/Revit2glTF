//#define WRITE_BBOXES
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;

using GLTF2BIM.GLTF.Schema;
using GLTF2BIM.GLTF.Package.BaseTypes;
using GLTF2BIM.GLTF.Extensions.BIM.Schema;
using GLTFRevitExport.Build;
using GLTFRevitExport.Build.Actions;
using GLTFRevitExport.Build.Actions.BaseTypes;
using GLTFRevitExport.GLTF.Extensions.BIM.Revit;

namespace GLTFRevitExport.Export {
#if REVIT2019
    sealed partial class ExportContext : IExportContext, IModelExportContext {
#else
    sealed partial class ExportContext : IExportContext, IExportContextBase, IModelExportContext {
#endif
        public ExportContext(Document doc, GLTFExportConfigs exportConfigs = null) {
            // ensure base configs
            _cfgs = exportConfigs is null ? new GLTFExportConfigs() : exportConfigs;

            // reset stacks
            ResetExporter();
            // place doc on the stack
            _docStack.Push(doc);
        }

        public List<GLTFPackageItem> Build(ElementFilter filter = null) {
            // build asset info
            var doc = _docStack.Last();

            // create main gltf builder
            var mainCtx = new BuildContext("model", doc, _cfgs);
            var buildContexts = new List<BuildContext> { mainCtx };

            // combine default filter with build filter
            ElementFilter actionFilter = null;
            if (filter != null) {
                actionFilter = new LogicalOrFilter(
                    new List<ElementFilter> {
                        // always include these categories no matter the build filter
                        new ElementMulticategoryFilter(
                            new List<BuiltInCategory> {
                                BuiltInCategory.OST_RvtLinks,
                                BuiltInCategory.OST_Views,
                            }
                        ),
                        filter
                    }
                );
            }

            Logger.Log("+ start build");

            // filter and process each action
            // the loop tests each BEGIN action with a filter
            // and needs to remember the result of the filter test
            // so it knows whether to run the corresponding END action or not
            var passResults = new Stack<bool>();
            BuildContext currentCtx = mainCtx;
            BuildContext activeLinkCtx = null;
            int counter = 0;
            foreach (var action in _actions) {
                // build progress update
                _cfgs.UpdateProgress(counter / (float)_actions.Count);
                counter++;

                if (!_cfgs.EmbedLinkedModels) {
                    if (action is LinkBeginAction linkBeg) {
                        linkBeg.Uri = $"{linkBeg.LinkId}.gltf";
                    }
                    else if (activeLinkCtx != null) {
                        // Note:
                        // LinkEndAction should be always preceded by ElementTransformAction
                        // switch to main builder. We need to switch to main builder on 
                        // ElementTransformAction to apply the correct transform
                        // to the link instance node in the main builder
                        if (action is LinkTransformAction) {
                            // switch to main builder
                            currentCtx = mainCtx;
                        }
                        else if (action is LinkBoundsAction lbAction) {
                            // grab the bounds from the build
                            glTFBIMBounds bounds = null;
                            for (uint idx = 0; idx < activeLinkCtx.Builder.NodeCount; idx++) {
                                var node = activeLinkCtx.Builder.GetNode(idx);
                                if (node.Extensions != null)
                                    foreach (var ext in node.Extensions)
                                        if (ext.Value is glTFBIMNodeExtension nodeExt)
                                            if (nodeExt.Bounds != null && nodeExt.Bounds.LinkHostBounds != null) {
                                                if (bounds is null)
                                                    bounds = new glTFBIMBounds(nodeExt.Bounds.LinkHostBounds);
                                                else
                                                    bounds.Union(nodeExt.Bounds.LinkHostBounds);
                                            }
                            }
                            lbAction.Bounds = bounds;
                        }
                        // close the link builder
                        else if (action is LinkEndAction) {
                            // close the link
                            activeLinkCtx.Builder.CloseNode();
                            activeLinkCtx.Builder.CloseScene();
                            buildContexts.Add(activeLinkCtx);
                            // switch to main builder
                            activeLinkCtx = null;
                            currentCtx = mainCtx;
                        }
                    }
                }

                switch (action) {
                    case BuildBeginAction beg:
                        if (actionFilter is null) {
                            beg.Execute(currentCtx);
                            passResults.Push(true);
                        }
                        else if (beg.Passes(actionFilter)) {
                            beg.Execute(currentCtx);
                            passResults.Push(true);
                        }
                        else
                            passResults.Push(false);
                        break;

                    case BuildEndAction end:
                        if (passResults.Pop())
                            end.Execute(currentCtx);
                        break;

                    case BaseAction ea:
                        ea.Execute(currentCtx);
                        break;
                }

                // use this link builder for the rest of actions
                // that happen inside the link
                if (!_cfgs.EmbedLinkedModels)
                    if (action is LinkBeginAction linkBeg) {
                        // create a new glTF for this link
                        activeLinkCtx = new BuildContext(
                            name: linkBeg.LinkId,
                            doc: linkBeg.LinkDocument,
                            cfgs: _cfgs
                        );

                        activeLinkCtx.Builder.OpenScene(
                            name: "default",
                            exts: new glTFExtension[] {
                                new glTFRevitViewExt()
                            },
                            extras: null
                            );
                        
                        activeLinkCtx.Builder.OpenNode(
                            name: "default",
                            matrix: null,
                            exts: new glTFExtension[] {
                                new glTFRevitElementExt()
                            },
                            extras: null);

                        // use this builder for all subsequent elements
                        currentCtx = activeLinkCtx;
                    }
            }

            Logger.Log("- end build");

            Logger.Log("+ start pack");

            // prepare pack
            var gltfPack = new List<GLTFPackageItem>();

            foreach (var buildCtx in buildContexts) {
#if DEBUG && WRITE_BBOXES
                for (uint idx = 0; idx < buildCtx.Builder.NodeCount; idx++) {
                    if (buildCtx.Builder.GetNode(idx) is glTFNode node)
                        if (node.Extensions != null)
                            foreach (var ext in node.Extensions)
                                if (ext.Value is glTFBIMNodeExtension nodeExt)
                                    if (nodeExt.Bounds != null) {
                                        // RE: script/bbox_preview.gh
                                        var b = nodeExt.Bounds;
                                        if (b != null && Environment.GetEnvironmentVariable("ARGYLEBBOXFILE") is string bboxPointsFile) {
                                            File.AppendAllText(
                                                bboxPointsFile,
                                                $"{node.Name ?? "?"},{b.Min.X},{b.Min.Y},{b.Min.Z},{b.Max.X},{b.Max.Y},{b.Max.Z}\n"
                                                );
                                        }
                                    }
                }
#endif

                gltfPack.AddRange(
                    buildCtx.Pack(_cfgs)
                    );
            }

            Logger.Log("- end pack");

            return gltfPack;
        }

        void ResetExporter() {
            // reset the logger
            Logger.Reset();
            _actions.Clear();
            _processed.Clear();
            _skipElement = false;
        }
    }
}
