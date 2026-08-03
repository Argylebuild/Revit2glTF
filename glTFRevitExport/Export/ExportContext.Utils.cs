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
using GLTFRevitExport.GLTF.Extensions.BIM.Revit;

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

        glTFBIMBounds CalculateBounds(float[] matrix = null)
            => CalculateBoundsOf(_partStack, matrix);

        glTFBIMBounds CalculateBoundsOf(IEnumerable<PartData> parts, float[] matrix = null) {

            glTFBIMBounds CalculateMeshBounds(Transform xform) {
                float minx, miny, minz, maxx, maxy, maxz;
                minx = miny = minz = maxx = maxy = maxz = float.NaN;

                foreach (var partData in parts)
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

            // per-element segment budget (safety valve for pathological DWGs)
            int segmentBudget = _cfgs.MaxLineSegmentsPerElement;
            if (segmentBudget > 0 && _elementLineSegmentCount >= segmentBudget) {
                if (!_elementLineBudgetWarned) {
                    Logger.Log(
                        $"x line segment budget ({segmentBudget}) exceeded;"
                        + " dropping remaining linework for this element"
                        );
                    _elementLineBudgetWarned = true;
                }
                return;
            }

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

            // budget bookkeeping; the final chain may overshoot slightly
            _elementLineSegmentCount += lines.Count;

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

        #region Import linework chunking

        /// <summary>
        /// A single collected line segment with a reference back to its
        /// source color part (for vertices, color and transparency)
        /// </summary>
        class LineChunkSeg {
            public PartData Source;
            public SegmentData Seg;
        }

        /// <summary>
        /// Emit the collected linework of a project-scale CAD import as
        /// spatial chunk child nodes under the import's node (which is the
        /// open node scope when these actions execute), so the AR app can
        /// stream large drawings by area. Grid cells are
        /// LineChunkSizeHorizontal × LineChunkSizeVertical (glTF meters,
        /// Y up); overflowing cells split recursively
        /// </summary>
        void ChunkAndEnqueueImportLinework(Element e, float[] parentMatrix) {
            try {
                // flatten collected color parts into segment records
                var segs = new List<LineChunkSeg>();
                foreach (var part in _elementLineParts.Values)
                    foreach (var seg in part.Primitive.Lines)
                        segs.Add(new LineChunkSeg { Source = part, Seg = seg });

                if (segs.Count == 0)
                    return;

                float sizeH = _cfgs.LineChunkSizeHorizontal > 0 ? _cfgs.LineChunkSizeHorizontal : 10f;
                float sizeV = _cfgs.LineChunkSizeVertical > 0 ? _cfgs.LineChunkSizeVertical : 4f;

                // chunk vertices are collected in document space; the chunk
                // node matrix must be relative to the import's node matrix
                // (set by mesh localization when the import also has solids)
                var parentAnchor = new VectorData(0, 0, 0);
                if (parentMatrix != null)
                    parentAnchor = new VectorData(
                        parentMatrix[12], parentMatrix[13], parentMatrix[14]);

                // chunks bucket under one "CAD Imports" category in the AR
                // app instead of a per-DWG-file category (Revit categorizes
                // imports by file name, which pollutes the category list);
                // the file name is preserved as a subcategory. Dominant pen
                // weight comes from the import element
                var taxonomies = new List<string>();
                foreach (var taxonomy in glTFRevitUtils.GetTaxonomies(e)) {
                    int catIdx = taxonomy.IndexOf(
                        "/Categories/", StringComparison.Ordinal);
                    if (catIdx >= 0) {
                        int insertAt = catIdx + "/Categories/".Length;
                        taxonomies.Add(
                            taxonomy.Substring(0, insertAt)
                            + "CAD Imports/"
                            + taxonomy.Substring(insertAt));
                    }
                    else
                        taxonomies.Add(taxonomy);
                }
                int? lineWeight = GetElementLineWeight(e);

                EmitLineChunks(
                    segs, sizeH, sizeV, 0,
                    e.GetId(), taxonomies, lineWeight, parentAnchor
                    );
            }
            catch (Exception ex) {
                // never abort the export over one bad import
                Logger.Log($"x linework chunking failed: {ex.Message}");
            }
            finally {
                _elementLineParts.Clear();
            }
        }

        void EmitLineChunks(List<LineChunkSeg> segs, float sizeH, float sizeV,
                            int depth, string parentId, List<string> taxonomies,
                            int? lineWeight, VectorData parentAnchor) {
            // bin segments by the grid cell of their midpoint
            var bins = new Dictionary<string, List<LineChunkSeg>>();
            var cells = new Dictionary<string, int[]>();
            foreach (var s in segs) {
                var v1 = s.Source.Primitive.Vertices[(int)s.Seg.V1];
                var v2 = s.Source.Primitive.Vertices[(int)s.Seg.V2];
                int ix = (int)Math.Floor(((v1.X + v2.X) / 2f) / sizeH);
                int iy = (int)Math.Floor(((v1.Y + v2.Y) / 2f) / sizeV);
                int iz = (int)Math.Floor(((v1.Z + v2.Z) / 2f) / sizeH);
                string key = ix + ":" + iy + ":" + iz;
                if (!bins.TryGetValue(key, out List<LineChunkSeg> list)) {
                    list = new List<LineChunkSeg>();
                    bins[key] = list;
                    cells[key] = new int[] { ix, iy, iz };
                }
                list.Add(s);
            }

            foreach (var bin in bins) {
                // overflowing cells split in half per axis, up to 1/8 size
                if (_cfgs.MaxLineSegmentsPerNode > 0
                        && bin.Value.Count > _cfgs.MaxLineSegmentsPerNode
                        && depth < 3) {
                    EmitLineChunks(
                        bin.Value, sizeH / 2f, sizeV / 2f, depth + 1,
                        parentId, taxonomies, lineWeight, parentAnchor
                        );
                    continue;
                }

                EmitLineChunk(
                    bin.Value, cells[bin.Key], depth,
                    parentId, taxonomies, lineWeight, parentAnchor
                    );
            }
        }

        void EmitLineChunk(List<LineChunkSeg> segs, int[] cell, int depth,
                           string parentId, List<string> taxonomies,
                           int? lineWeight, VectorData parentAnchor) {
            // rebuild per-color parts local to this chunk, deduping chain
            // vertices within the chunk. Vertices are COPIED because a chain
            // vertex can be shared by segments landing in different chunks,
            // and localization below mutates them
            var chunkParts = new Dictionary<PartData, PartData>();
            var indexMaps = new Dictionary<PartData, Dictionary<uint, uint>>();

            foreach (var s in segs) {
                if (!chunkParts.TryGetValue(s.Source, out PartData chunkPart)) {
                    chunkPart = new PartData(
                        new PrimitiveData(
                            new List<VectorData>(), new List<SegmentData>())) {
                        Color = s.Source.Color,
                        Transparency = s.Source.Transparency
                    };
                    chunkParts[s.Source] = chunkPart;
                    indexMaps[s.Source] = new Dictionary<uint, uint>();
                }

                var map = indexMaps[s.Source];
                chunkPart.Primitive.Lines.Add(new SegmentData {
                    V1 = MapChunkVertex(s.Source, s.Seg.V1, chunkPart, map),
                    V2 = MapChunkVertex(s.Source, s.Seg.V2, chunkPart, map)
                });
            }

            var parts = new List<PartData>(chunkParts.Values);

            // declared bounds from document-space vertices (link-host bounds
            // handling included via the shared helper)
            glTFBIMBounds bounds = CalculateBoundsOf(parts);

            // localize chunk vertices to their own centroid; the chunk node
            // matrix places them (same convention as LocalizePartStack)
            float minx, miny, minz, maxx, maxy, maxz;
            minx = miny = minz = float.MaxValue;
            maxx = maxy = maxz = float.MinValue;
            foreach (var part in parts)
                foreach (var vtx in part.Primitive.Vertices) {
                    minx = vtx.X < minx ? vtx.X : minx;
                    miny = vtx.Y < miny ? vtx.Y : miny;
                    minz = vtx.Z < minz ? vtx.Z : minz;
                    maxx = vtx.X > maxx ? vtx.X : maxx;
                    maxy = vtx.Y > maxy ? vtx.Y : maxy;
                    maxz = vtx.Z > maxz ? vtx.Z : maxz;
                }

            var anchor = new VectorData(
                (minx + maxx) / 2f, (miny + maxy) / 2f, (minz + maxz) / 2f);

            foreach (var part in parts)
                foreach (var vtx in part.Primitive.Vertices) {
                    vtx.X -= anchor.X;
                    vtx.Y -= anchor.Y;
                    vtx.Z -= anchor.Z;
                }

            float[] matrix = new float[16] {
                1f, 0f, 0f, 0f,
                0f, 1f, 0f, 0f,
                0f, 0f, 1f, 0f,
                anchor.X - parentAnchor.X,
                anchor.Y - parentAnchor.Y,
                anchor.Z - parentAnchor.Z,
                1f
            };

            string chunkName = $"Lines [{cell[0]},{cell[1]},{cell[2]}]";
            if (depth > 0)
                chunkName += $" ~{depth}";
            string chunkId =
                $"{parentId}-lines-{depth}-{cell[0]}-{cell[1]}-{cell[2]}";

            _actions.Enqueue(
                new LineChunkBeginAction(chunkId, chunkName, taxonomies, matrix));
            foreach (var part in parts)
                _actions.Enqueue(new PartFromDataAction(part));
            _actions.Enqueue(new ElementBoundsAction(bounds));
            if (lineWeight is int weight)
                _actions.Enqueue(new ElementLineWeightAction(weight));
            _actions.Enqueue(new LineChunkEndAction());
        }

        uint MapChunkVertex(PartData source, uint srcIdx, PartData chunkPart,
                            Dictionary<uint, uint> map) {
            if (!map.TryGetValue(srcIdx, out uint localIdx)) {
                var src = source.Primitive.Vertices[(int)srcIdx];
                localIdx = (uint)chunkPart.Primitive.Vertices.Count;
                chunkPart.Primitive.Vertices.Add(
                    new VectorData(src.X, src.Y, src.Z));
                map[srcIdx] = localIdx;
            }
            return localIdx;
        }

        #endregion
    }
}
