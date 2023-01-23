using System;

using Autodesk.Revit.DB;

using GLTFRevitExport.Extensions;

namespace GLTFRevitExport.Build.Geometry {
    class VectorData : IComparable<VectorData> {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }

        public VectorData(float x, float y, float z) {
            X = x;
            Y = y;
            Z = z;
        }

        public VectorData(XYZ vector) {
            var gltfVector = vector.ToGLTF();
            X = gltfVector[0];
            Y = gltfVector[1];
            Z = gltfVector[2];
        }

        public float[] ToArray() => new float[] { X.Round(), Y.Round(), Z.Round() };

        public int CompareTo(VectorData a) {
            float d = X - a.X;
            if (0 == d) {
                d = Y - a.Y;
                if (0 == d) {
                    d = Z - a.Z;
                }
            }
            return (0 == d) ? 0 : ((0 < d) ? 1 : -1);
        }

        public XYZ ToXYZ() => new XYZ(X, Y, Z);

        public VectorData Transform(float[] matrix) {
            return Transform(matrix.FromGLTFMatrix());
        }
        
        public VectorData Transform(Transform xform) {
            var xformedMin = xform.OfPoint(ToXYZ());
            return new VectorData(xformedMin.X.ToSingle(), xformedMin.Y.ToSingle(), xformedMin.Z.ToSingle());
        }

        public static VectorData operator +(VectorData left, VectorData right) {
            return new VectorData(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        public static VectorData operator -(VectorData left, VectorData right) {
            return new VectorData(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        public static VectorData operator /(VectorData left, float divisor) {
            return new VectorData(left.X / divisor, left.Y / divisor, left.Z / divisor);
        }
    }
}