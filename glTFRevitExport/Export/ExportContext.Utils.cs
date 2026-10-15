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
    sealed partial class ExportContext : IExportContext, IExportContextBase, IModelExportContext {
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

            // min/max accumulate in double (vertices are double) and are
            // cast to float once, at the end
            glTFBIMBounds CalculateMeshBounds(Transform xform) {
                double minx, miny, minz, maxx, maxy, maxz;
                minx = miny = minz = maxx = maxy = maxz = double.NaN;

                foreach (var partData in parts)
                {
                    if (partData.HasPartData)
                    {
                        foreach (var vertex in partData.Primitive.Vertices)
                        {
                            var vtx = vertex;
                            if (xform is Transform)
                                vtx = vtx.Transform(xform);

                            minx = double.IsNaN(minx) || vtx.X < minx ? vtx.X : minx;
                            miny = double.IsNaN(miny) || vtx.Y < miny ? vtx.Y : miny;
                            minz = double.IsNaN(minz) || vtx.Z < minz ? vtx.Z : minz;
                            maxx = double.IsNaN(maxx) || vtx.X > maxx ? vtx.X : maxx;
                            maxy = double.IsNaN(maxy) || vtx.Y > maxy ? vtx.Y : maxy;
                            maxz = double.IsNaN(maxz) || vtx.Z > maxz ? vtx.Z : maxz;
                        }
                    }
                }

                return new glTFBIMBounds(
                    (float)minx, (float)miny, (float)minz,
                    (float)maxx, (float)maxy, (float)maxz
                );
            };

            Transform mxform = null;
            if (matrix != null)
                mxform = matrix.FromGLTFMatrix();

            glTFBIMBounds finalBounds = CalculateMeshBounds(mxform);

            // Inside a linked document the vertices are already in host
            // coordinates (OnLinkBegin pushes the link transform onto the
            // transform stack), so the host-space bounds the main file needs
            // for the link node ARE these bounds. They used to be transformed
            // by the link matrix a second time, which put every link's
            // declared bounds — and the root asset bounds — millions of
            // meters off on far-from-origin models
            if (_linkDepth > 0)
                finalBounds.LinkHostBounds = new glTFBIMBounds(finalBounds);

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
            // the anchor and the shifted vertices are computed in double;
            // only the node matrix translation is cast to float, and by then
            // it is small (vertices were rebased by the export origin)
            double minx, miny, minz, maxx, maxy, maxz;
            minx = miny = minz = double.MaxValue;
            maxx = maxy = maxz = double.MinValue;

            foreach (var partData in _partStack)
            {
                if (partData.HasPartData)
                {
                    foreach (var vtx in partData.Primitive.Vertices)
                    {
                        minx = vtx.X < minx ? vtx.X : minx;
                        miny = vtx.Y < miny ? vtx.Y : miny;
                        minz = vtx.Z < minz ? vtx.Z : minz;
                        maxx = vtx.X > maxx ? vtx.X : maxx;
                        maxy = vtx.Y > maxy ? vtx.Y : maxy;
                        maxz = vtx.Z > maxz ? vtx.Z : maxz;
                    }
                }
            }

            var anchor = new VectorData(
                (minx + maxx) / 2.0, (miny + maxy) / 2.0, (minz + maxz) / 2.0);

            foreach (var partData in _partStack)
            {
                if (partData.HasPartData)
                {
                    foreach (var vtx in partData.Primitive.Vertices)
                    {
                        vtx.X -= anchor.X;
                        vtx.Y -= anchor.Y;
                        vtx.Z -= anchor.Z;
                    }
                }
            }

            return new float[16] {
                1f,                 0f,                 0f,                 0f,
                0f,                 1f,                 0f,                 0f,
                0f,                 0f,                 1f,                 0f,
                (float)anchor.X,    (float)anchor.Y,    (float)anchor.Z,    1f
            };
        }

        #region Export origin (position-scramble fix)

        /// <summary>
        /// Beyond this distance from the Revit internal origin (in meters)
        /// float32 can no longer hold a coordinate to ~1 mm: it resolves 6 cm
        /// at 700 km and 12 cm at 2000 km, and every vertex of a model that
        /// far out snaps to that grid (the "position scramble"). Content
        /// centered inside the threshold is exported exactly as before — no
        /// rebase, no originOffset — so existing projects and their saved AR
        /// alignments are untouched. Same value as the Navisworks exporter so
        /// both make the same decision for the same site
        /// </summary>
        const double RebaseThresholdMeters = 10000;

        /// <summary>
        /// Origin components snap to this grid (in meters). Coarse rounding
        /// keeps the origin stable across re-exports and sibling models of
        /// one site (content centers drift by meters, not kilometers); the
        /// AR app clusters origins within 10 km and composes the exact
        /// difference in double, so models of a site stay mutually aligned
        /// </summary>
        const double OriginGridMeters = 1000;

        /// <summary>
        /// Translation by the negated export origin: the base of the
        /// transform stack, so every point delivered by Revit is rebased in
        /// double precision before it is converted to glTF space
        /// </summary>
        Transform BaseTransform => Transform.CreateTranslation(_originOffset.Negate());

        /// <summary>
        /// Picks the export origin for this session from the content of the
        /// first exported view: the per-axis median of the visible elements'
        /// bounding-box centers (robust against a stray element far from the
        /// site, e.g. a title block at 0,0 inside a site DWG, which would drag
        /// a plain bounds center hundreds of km off), snapped to
        /// OriginGridMeters — but only when the content sits farther than
        /// RebaseThresholdMeters from the internal origin. Linked models
        /// count as one element each (their bounding box is in host space)
        /// </summary>
        void SetExportOriginOffset(Document doc, View view) {
            _originOffsetSet = true;
            _originOffset = XYZ.Zero;

            var xs = new List<double>();
            var ys = new List<double>();
            var zs = new List<double>();
            try {
                foreach (var e in new FilteredElementCollector(doc, view.Id)
                                      .WhereElementIsNotElementType()) {
                    BoundingBoxXYZ bbox = null;
                    try { bbox = e.get_BoundingBox(view); }
                    catch { continue; }
                    if (bbox is null || bbox.Min is null || bbox.Max is null)
                        continue;

                    var center = bbox.Transform.OfPoint((bbox.Min + bbox.Max) / 2.0);
                    if (!IsFinite(center.X) || !IsFinite(center.Y) || !IsFinite(center.Z))
                        continue;

                    xs.Add(center.X);
                    ys.Add(center.Y);
                    zs.Add(center.Z);
                }
            }
            catch (Exception ex) {
                // never fail an export over the origin heuristic; fall back
                // to the legacy (non-rebased) output
                Logger.Log($"x origin: element scan failed ({ex.Message}); exporting without rebase");
                return;
            }

            if (xs.Count == 0) {
                Logger.Log("> origin: no bounded elements in view; exporting without rebase");
                return;
            }

            // content center in Revit internal coordinates (feet)
            double cx = Median(xs), cy = Median(ys), cz = Median(zs);
            double ft = NumberExtensions.FeetToMeters;
            double maxAbsMeters =
                Math.Max(Math.Abs(cx), Math.Max(Math.Abs(cy), Math.Abs(cz))) * ft;
            bool needsRebase = maxAbsMeters > RebaseThresholdMeters;

            if (needsRebase) {
                double grid = OriginGridMeters / ft;
                _originOffset = new XYZ(
                    Math.Round(cx / grid) * grid,
                    Math.Round(cy / grid) * grid,
                    Math.Round(cz / grid) * grid
                    );
            }

            Logger.Log(
                $"> origin: content center ({cx * ft:F1}, {cy * ft:F1}, {cz * ft:F1}) m"
                + $" from {xs.Count} elements; needsRebase={needsRebase};"
                + $" originOffset ({_originOffset.X * ft:F1}, {_originOffset.Y * ft:F1}, {_originOffset.Z * ft:F1}) m (Revit X,Y,Z)");
        }

        static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        static double Median(List<double> values) {
            var sorted = values.OrderBy(v => v).ToList();
            int n = sorted.Count;
            if (n % 2 == 1)
                return sorted[n / 2];
            return (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }

        #endregion

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
            double minx, miny, minz, maxx, maxy, maxz;
            minx = miny = minz = double.MaxValue;
            maxx = maxy = maxz = double.MinValue;
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
                (minx + maxx) / 2.0, (miny + maxy) / 2.0, (minz + maxz) / 2.0);

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
                (float)(anchor.X - parentAnchor.X),
                (float)(anchor.Y - parentAnchor.Y),
                (float)(anchor.Z - parentAnchor.Z),
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
