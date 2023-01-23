using System;

using Autodesk.Revit.DB;

using GLTF2BIM.GLTF;
using GLTF2BIM.GLTF.Schema;
using GLTF2BIM.GLTF.Extensions.BIM.Schema;
using GLTFRevitExport.Extensions;
using GLTFRevitExport.Build.Actions.BaseTypes;
using GLTFRevitExport.GLTF.Extensions.BIM.Revit;

namespace GLTFRevitExport.Build.Actions {
    class ElementBeginAction : BuildBeginAction {
        readonly ElementType _elementType;

        public string Uri { get; set; } = null;

        public ElementBeginAction(Element element, ElementType type) : base(element) {
            _elementType = type;
        }

        public override void Execute(BuildContext ctx) {
            // open a new node and store its id
            Logger.Log("+ element begin");

            // node filter to pass to gltf builder
            string targetId = string.Empty;
            bool nodeFilter(glTFNode node) {
                if (node.Extensions != null) {
                    foreach (var ext in node.Extensions)
                        if (ext.Value is glTFBIMNodeExtension nodeExt)
                            return nodeExt.Id == targetId;
                }
                return false;
            }

            // create a node for its type
            // attemp at finding previously created node for this type
            // but only search children of already open node
            if (ctx.Configs.ExportHierarchy) {
                targetId = _elementType.GetId();
                var typeNodeIdx = ctx.Builder.FindChildNode(nodeFilter);

                if (typeNodeIdx >= 0) {
                    ctx.Builder.OpenExistingNode((uint)typeNodeIdx);
                }
                // otherwise create and open a new node for this type
                else {
                    var bimExt = new glTFRevitElementExt(_elementType, ctx);

                    ctx.Builder.OpenNode(
                        name: _elementType.Name,
                        matrix: null,
                        exts: new glTFExtension[] { bimExt },
                        extras: ctx.Configs.BuildExtras(_elementType)
                    );
                }
            }

            // create a node for this instance
            // attemp at finding previously created node for this instance
            // but only search children of already open type node
            targetId = element.GetId();
            var instNodeIdx = ctx.Builder.FindChildNode(nodeFilter);

            if (instNodeIdx >= 0) {
                ctx.Builder.OpenExistingNode((uint)instNodeIdx);
            }
            // otherwise create and open a new node for this type
            else {
                var bimExt = new glTFRevitElementExt(element, ctx) {
                    Uri = Uri
                };

                var newNodeIdx = ctx.Builder.OpenNode(
                    name: element.Name,
                    matrix: null,
                    exts: new glTFExtension[] { bimExt },
                    extras: ctx.Configs.BuildExtras(element)
                );
            }
        }
    }
}
