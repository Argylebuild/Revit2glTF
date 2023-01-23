using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

using GLTF2BIM.GLTF.Schema;
using GLTF2BIM.GLTF.Package;
using GLTF2BIM.GLTF.Extensions.BIM.Schema;
using GLTFRevitExport.Extensions;
using GLTFRevitExport.Build;
using GLTFRevitExport.Build.Actions;
using GLTFRevitExport.Build.Geometry;

namespace GLTFRevitExport.Export {
#if REVIT2019
    sealed partial class ExportContext : IExportContext, IModelExportContext {
#else
    sealed partial class ExportContext : IExportContext, IExportContextBase, IModelExportContext {
#endif
        bool RecordOrSkip(Element e, string skipMessage, bool setFlag = false) {
            bool skip = false;
            if (e is null) {
                Logger.Log(skipMessage);
                skip = true;
            }
            else if (e != null && _processed.Contains(e.GetId())) {
                Logger.LogElement(skipMessage, e);
                skip = true;
            }
            else
                _processed.Add(e.GetId());

            if (setFlag)
                _skipElement = skip;
            return skip;
        }

        glTFBIMBounds CalculateBounds(float[] matrix = null) {

            glTFBIMBounds CalculateMeshBounds(Transform xform) {
                float minx, miny, minz, maxx, maxy, maxz;
                minx = miny = minz = maxx = maxy = maxz = float.NaN;

                foreach (var partData in _partStack)
                {
                    if (partData.HasPartData)
                    {
                        foreach (var vertex in partData.Primitive.Vertices)
                        {
                            var vtx = vertex;
                            if (xform is Transform)
                                vtx = vtx.Transform(xform);

                            minx = minx is float.NaN || vtx.X < minx ? vtx.X : minx;
                            miny = miny is float.NaN || vtx.Y < miny ? vtx.Y : miny;
                            minz = minz is float.NaN || vtx.Z < minz ? vtx.Z : minz;
                            maxx = maxx is float.NaN || vtx.X > maxx ? vtx.X : maxx;
                            maxy = maxy is float.NaN || vtx.Y > maxy ? vtx.Y : maxy;
                            maxz = maxz is float.NaN || vtx.Z > maxz ? vtx.Z : maxz;
                        }
                    }
                }

                return new glTFBIMBounds(
                    minx, miny, minz,
                    maxx, maxy, maxz
                );
            };

            Transform mxform = null;
            if (matrix != null)
                mxform = matrix.FromGLTFMatrix();

            // if link matrix is available we are processing mesh in a link
            // therefore .LinkHostBounds must be set on the calculated bounds
            glTFBIMBounds linkHostBounds = null;
            if (_linkMatrix != null) {
                var lxform = _linkMatrix.FromGLTFMatrix();
                if (mxform != null)
                    lxform = lxform.Multiply(mxform);
                linkHostBounds = CalculateMeshBounds(lxform);
                if (_cfgs.EmbedLinkedModels)
                    return linkHostBounds;
            }

            glTFBIMBounds finalBounds;
            if (mxform != null)
                finalBounds = CalculateMeshBounds(mxform);
            else
                finalBounds = CalculateMeshBounds(null);

            finalBounds.LinkHostBounds = linkHostBounds;
            return finalBounds;
        }

        float[] LocalizePartStack() {
            List<float> vx = new List<float>();
            List<float> vy = new List<float>();
            List<float> vz = new List<float>();

            foreach (var partData in _partStack)
            {
                if (partData.HasPartData)
                {
                    foreach (var vtx in partData.Primitive.Vertices)
                    {
                        vx.Add(vtx.X);
                        vy.Add(vtx.Y);
                        vz.Add(vtx.Z);
                    }
                }

            }

            var min = new VectorData(vx.Min(), vy.Min(), vz.Min());
            var max = new VectorData(vx.Max(), vy.Max(), vz.Max());
            var anchor = min + ((max - min) / 2f);
            var translate = new VectorData(0, 0, 0) - anchor;

            foreach (var partData in _partStack)
            {
                if (partData.HasPartData)
                {
                    foreach (var vtx in partData.Primitive.Vertices)
                    {
                        vtx.X += translate.X;
                        vtx.Y += translate.Y;
                        vtx.Z += translate.Z;
                    }
                }
            }

            return new float[16] {
                1f,             0f,             0f,             0f,
                0f,             1f,             0f,             0f,
                0f,             0f,             1f,             0f,
                -translate.X,   -translate.Y,   -translate.Z,    1f
            };
        }
    }
}
