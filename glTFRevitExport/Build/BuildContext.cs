using System;
using System.Collections.Generic;

using Autodesk.Revit.DB;

using GLTF2BIM.GLTF;
using GLTF2BIM.GLTF.Schema;
using GLTF2BIM.GLTF.Package;
using GLTF2BIM.GLTF.Package.BaseTypes;
using GLTF2BIM.GLTF.Extensions.BIM.Containers;

using GLTFRevitExport.Extensions;
using GLTFRevitExport.GLTF.Extensions.BIM.Revit;

namespace GLTFRevitExport.Build {
    class BuildContext {
        public GLTFBuilder Builder { get; }
        public GLTFExportConfigs Configs { get; }
        public glTFRevitDocumentExt AssetExtension { get; }
        public glTFBIMPropertyContainer PropertyContainer { get; }

        /// <summary>
        /// Export origin in Revit internal coordinates (feet, Z-up) that the
        /// collected geometry was rebased by. Build actions that convert
        /// absolute Revit coordinates themselves (levels, grids, element
        /// meshes) must subtract it before casting to float. XYZ.Zero when
        /// the export is not rebased
        /// </summary>
        public XYZ OriginOffset { get; }

        public BuildContext(string name, Document doc, GLTFExportConfigs cfgs, XYZ originOffset) {
            // create main gltf builder
            Builder = new GLTFBuilder(name);
            Configs = cfgs;
            OriginOffset = originOffset ?? XYZ.Zero;

            // build asset extension and property source (if needed)
            if (cfgs.EmbedParameters)
                AssetExtension = new glTFRevitDocumentExt(doc, this);
            else {
                PropertyContainer = new glTFBIMPropertyContainer($"{name}-properties.json");
                AssetExtension = new glTFRevitDocumentExt(doc, this);
            }

            // publish the origin (glTF space, double) so consumers can
            // reconstruct absolute coordinates; omitted (null) for
            // non-rebased exports so their output is unchanged
            AssetExtension.OriginOffset =
                OriginOffset.IsZeroLength() ? null : OriginOffset.ToGLTFDouble();

            Builder.SetAsset(
                generatorId: cfgs.GeneratorId,
                copyright: cfgs.CopyrightMessage,
                exts: new glTFExtension[] { AssetExtension },
                extras: cfgs.BuildExtras(doc)
            );
        }

        public List<GLTFPackageItem> Pack(GLTFExportConfigs cfgs) {
            var gltfPack = new List<GLTFPackageItem>();

            // pack the glTF data and get the container
            gltfPack.AddRange(
                Builder.Pack(new GLTFBuilderOptions {
                    UseSingleBinary = cfgs.UseSingleBinary,
                    MaxBinarySize = cfgs.MaxBinarySize
                })
            );

            if (PropertyContainer != null && PropertyContainer.HasPropertyData)
                gltfPack.Add(
                    new GLTFPackageJsonItem(PropertyContainer.Uri, PropertyContainer.Pack())
                );

            return gltfPack;
        }
    }
}