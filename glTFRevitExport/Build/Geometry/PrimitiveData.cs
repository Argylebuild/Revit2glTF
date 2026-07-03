using System;
using System.Collections.Generic;

using GLTFRevitExport.Properties;

namespace GLTFRevitExport.Build.Geometry {
    class PrimitiveData {
        public List<VectorData> Vertices { get; private set; }
        public List<FacetData> Faces { get; private set; }
        public List<SegmentData> Lines { get; private set; }

        public PrimitiveData(List<VectorData> vertices, List<FacetData> faces) {
            if (vertices is null || faces is null)
                throw new Exception(StringLib.VertexFaceIsRequired);
            Vertices = vertices;
            Faces = faces;
            Lines = new List<SegmentData>();
        }

        public PrimitiveData(List<VectorData> vertices, List<SegmentData> lines) {
            if (vertices is null || lines is null)
                throw new Exception(StringLib.VertexFaceIsRequired);
            Vertices = vertices;
            Faces = new List<FacetData>();
            Lines = lines;
        }

        public static PrimitiveData operator +(PrimitiveData left, PrimitiveData right) {
            uint startIdx = (uint) left.Vertices.Count;

            // new vertices array
            var vertices = new List<VectorData>(left.Vertices);
            vertices.AddRange(right.Vertices);

            // shift face indices
            var faces = new List<FacetData>(left.Faces);
            foreach (var faceIdx in right.Faces)
                faces.Add(faceIdx + startIdx);

            // shift line segment indices
            var lines = new List<SegmentData>(left.Lines);
            foreach (var segmentIdx in right.Lines)
                lines.Add(segmentIdx + startIdx);

            var prim = new PrimitiveData(vertices, faces);
            prim.Lines = lines;
            return prim;
        }
    }
}