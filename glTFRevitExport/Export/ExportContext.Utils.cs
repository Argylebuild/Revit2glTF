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

        void CollectLineGeometry(IList<XYZ> points, LineProperties lineProps) {
            try {
                CollectLineGeometryImpl(points, lineProps);
            }
            catch (Exception ex) {
                // never abort the export over a single bad curve
                // (CustomExporter.ShouldStopOnError is true)
                Logger.Log($"x line geometry failed: {ex.Message}");
            }
        }

        void CollectLineGeometryImpl(IList<XYZ> points, LineProperties lineProps) {
            if (points is null || points.Count < 2)
                return;

            var transform = CurrentTransform;

            // points form a connected chain; segments share the chain vertices
            var vertices = new List<VectorData>();
            foreach (var point in points)
                vertices.Add(new VectorData(transform.OfPoint(point)));

            var lines = new List<SegmentData>();
            for (int i = 0; i < vertices.Count - 1; i++) {
                // skip zero-length segments
                if (vertices[i].CompareTo(vertices[i + 1]) == 0)
                    continue;
                lines.Add(new SegmentData { V1 = (uint)i, V2 = (uint)(i + 1) });
            }
            if (lines.Count == 0)
                return;

            // as-drawn pen color, falling back to the element line settings.
            // copy the color since LineProperties is transient callback data
            Color color;
            if (lineProps != null
                    && lineProps.Color != null
                    && lineProps.Color.IsValid)
                color = new Color(
                    lineProps.Color.Red,
                    lineProps.Color.Green,
                    lineProps.Color.Blue
                    );
            else
                color = GetElementLineColor();

            // batch all segments of one color into a single part per element
            string colorKey = color.GetId();
            var newPrim = new PrimitiveData(vertices, lines);
            if (_elementLineParts.TryGetValue(colorKey, out PartData linePart))
                linePart.Primitive += newPrim;
            else
                _elementLineParts[colorKey] = new PartData(newPrim) {
                    Color = color,
                    Transparency = 0.0
                };
        }

        Color GetElementLineColor() {
            if (_currentElement is CurveElement curveElem
                    && curveElem.LineStyle is GraphicsStyle style
                    && style.GraphicsStyleCategory is Category styleCat
                    && styleCat.LineColor != null
                    && styleCat.LineColor.IsValid)
                return styleCat.LineColor;

            var category = _currentElement?.Category;
            if (category != null
                    && category.LineColor != null
                    && category.LineColor.IsValid)
                return category.LineColor;

            return _cfgs.DefaultColor;
        }

        int? GetElementLineWeight(Element e) {
            if (e is CurveElement curveElem
                    && curveElem.LineStyle is GraphicsStyle style
                    && style.GraphicsStyleCategory is Category styleCat)
                return styleCat.GetLineWeight(GraphicsStyleType.Projection);

            return e?.Category?.GetLineWeight(GraphicsStyleType.Projection);
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
