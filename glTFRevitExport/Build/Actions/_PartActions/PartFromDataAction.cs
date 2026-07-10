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

                // resolve the texture BEFORE building the primitive — UVs are
                // only emitted when a texture actually resolved for the
                // material (a textured material without UVs, or UVs without a
                // texture, are both invalid shapes)
                TextureUtils.ResolvedTexture texture = null;
                if (ctx.Configs.ExportTextures
                        && ctx.Configs.ExportMaterials
                        && _partData.Material != null
                        && _partData.Primitive.UVs != null
                        && _partData.Primitive.UVs.Count == _partData.Primitive.Vertices.Count)
                    texture = TextureUtils.ResolveTexture(_partData.Material);

                float[] uvs = null;
                if (texture != null) {
                    // scale surface UVs by the texture's real-world repeat
                    // size so tiling matches Revit's display, then flip V for
                    // glTF's top-left texture origin
                    uvs = new float[_partData.Primitive.UVs.Count * 2];
                    int uvIdx = 0;
                    foreach (var uv in _partData.Primitive.UVs) {
                        uvs[uvIdx++] = (float)(uv.U / texture.ScaleU);
                        uvs[uvIdx++] = (float)(1.0 - uv.V / texture.ScaleV);
                    }
                }

                var primIndex = ctx.Builder.AddPrimitive(
                    vertices: vertices.ToArray(),
                    normals: null,
                    uvs: uvs,
                    faces: faces.ToArray()
                    );

                Logger.Log("> material");

                uint? textureIdx = null;
                if (texture != null)
                    textureIdx = ctx.Builder.AddTexture(texture.Bytes, texture.MimeType);

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
                    UpdatePrimitiveMaterialByMaterial(ctx.Builder, primIndex, _partData.Material, textureIdx, ctx);
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

        void UpdatePrimitiveMaterialByMaterial(GLTFBuilder gltf, uint primIndex, Material material, uint? textureIdx, BuildContext ctx) {
            var existingMaterialIndex =
                gltf.FindMaterial(
                    (mat) => {
                        if (mat.Extensions != null) {
                            foreach (var ext in mat.Extensions)
                                if (ext.Value is glTFRevitElementExt matExt)
                                    // texture presence must match: a prim
                                    // without UVs must not bind a textured
                                    // material (and vice versa)
                                    return matExt.Id == material.UniqueId
                                        && (mat.PBRMetallicRoughness?.BaseColorTexture != null) == textureIdx.HasValue;
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
                // textured materials get a white base color factor — the
                // factor multiplies the texture, so the material's real color
                // would tint the texture down
                float[] color = textureIdx.HasValue
                    ? new float[] { 1f, 1f, 1f, 1f }
                    : material.Color.ToGLTF(material.Transparency / 128f);

                gltf.AddMaterial(
                    primitiveIndex: primIndex,
                    name: material.Name,
                    color: color,
                    textureIdx: textureIdx,
                    exts: new glTFExtension[] {
                        new glTFRevitElementExt(material, ctx)
                    },
                    extras: null
                );
            }
        }
    }
}
