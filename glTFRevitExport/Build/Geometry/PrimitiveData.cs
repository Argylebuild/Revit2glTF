using System;
using System.Collections.Generic;

using GLTFRevitExport.Properties;

namespace GLTFRevitExport.Build.Geometry {
    class PrimitiveData {
        public List<VectorData> Vertices { get; private set; }
        public List<FacetData> Faces { get; private set; }

        public PrimitiveData(List<VectorData> vertices, List<FacetData> faces) {
            if (vertices is null || faces is null)
                throw new Exception(StringLib.VertexFaceIsRequired);
            Vertices = vertices;
            Faces = faces;
        }

        public static PrimitiveData operator +(PrimitiveData left, PrimitiveData right) {
            int startIdx = left.Vertices.Count;

            // new vertices array
            var vertices = new List<VectorData>(left.Vertices);
            vertices.AddRange(right.Vertices);

            // shift face indices
            var faces = new List<FacetData>(left.Faces);
            foreach (var faceIdx in right.Faces)
                faces.Add(faceIdx + (ushort)startIdx);

            return new PrimitiveData(vertices, faces);
        }
    }
}