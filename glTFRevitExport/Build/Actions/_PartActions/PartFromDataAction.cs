using System;
using System.Collections.Generic;

using GLTF2BIM.GLTF;
using GLTF2BIM.GLTF.Schema;
using GLTF2BIM.GLTF.Extensions.BIM.Schema;
using GLTFRevitExport.Extensions;
using GLTFRevitExport.Build.Geometry;
using GLTFRevitExport.Build.Actions.BaseTypes;
using GLTFRevitExport.GLTF.Extensions.BIM.Revit;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace GLTFRevitExport.Build.Actions {
    class PartFromDataAction : BaseAction {
        readonly PartData _partData;

        public PartFromDataAction(PartData partData) => _partData = partData;

        public override void Execute(BuildContext ctx)
        {
            if (_partData.HasPartData)
            {
                Logger.Log("> primitive");

                // make a new mesh and assign the new material
                var vertices = new List<float>();
                foreach (var vec in _partData.Primitive.Vertices)
                    vertices.AddRange(vec.ToArray());

                var faces = new List<uint>();
                foreach (var facet in _partData.Primitive.Faces)
                    faces.AddRange(facet.ToArray());

                var primIndex = ctx.Builder.AddPrimitive(
                    vertices: vertices.ToArray(),
                    normals: null,
                    faces: faces.ToArray()
                    );

                Logger.Log("> material");

                // if we are not exporting materials, use the default color
                if (!ctx.Configs.ExportMaterials)
                    UpdatePrimitiveMaterialByColor(ctx.Builder, primIndex, ctx.Configs.DefaultColor, 0.0d);

                // if material information is not provided, make a material
                // based on color and transparency
                else if (_partData.Material is null)
                {
                    // make sure color is valid, otherwise it will throw
                    // exception that color is not initialized
                    Color color = _partData.Color.IsValid ? _partData.Color : ctx.Configs.DefaultColor;
                    UpdatePrimitiveMaterialByColor(ctx.Builder, primIndex, color, _partData.Transparency);
                }

                // otherwise process the new material
                else
                    UpdatePrimitiveMaterialByMaterial(ctx.Builder, primIndex, _partData.Material, ctx);
            }
        }

        void UpdatePrimitiveMaterialByColor(GLTFBuilder gltf, uint primIndex, Color color, double transparency) {
            string matName = color.GetId();
            var existingMaterialIndex =
                gltf.FindMaterial((mat) => mat.Name == matName);

            // check if material already exists
            if (existingMaterialIndex >= 0) {
                gltf.UpdateMaterial(
                    primitiveIndex: primIndex,
                    materialIndex: (uint)existingMaterialIndex
                );
            }
            // otherwise make a new material from color and transparency
            else {
                gltf.AddMaterial(
                    primitiveIndex: primIndex,
                    name: matName,
                    color: color.ToGLTF(transparency.ToSingle()),
                    exts: null,
                    extras: null
                );
            }
        }

        void UpdatePrimitiveMaterialByMaterial(GLTFBuilder gltf, uint primIndex, Material material, BuildContext ctx) {
            var existingMaterialIndex =
                gltf.FindMaterial(
                    (mat) => {
                        if (mat.Extensions != null) {
                            foreach (var ext in mat.Extensions)
                                if (ext.Value is glTFRevitElementExt matExt)
                                    return matExt.Id == material.UniqueId;
                        }
                        return false;
                    }
                );

            // check if material already exists
            if (existingMaterialIndex >= 0) {
                gltf.UpdateMaterial(
                    primitiveIndex: primIndex,
                    materialIndex: (uint)existingMaterialIndex
                );
            }
            // otherwise make a new material and get its index
            else {
                gltf.AddMaterial(
                    primitiveIndex: primIndex,
                    name: material.Name,
                    color: material.Color.ToGLTF(material.Transparency / 128f),
                    exts: new glTFExtension[] {
                        new glTFRevitElementExt(material, ctx)
                    },
                    extras: null
                );
            }
        }
    }
}
