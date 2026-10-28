using System;

using Autodesk.Revit.DB;

using GLTFRevitExport.Extensions;

namespace GLTFRevitExport.Build.Geometry {
    /// <summary>
    /// A point in glTF space (Y-up, meters). Components stay in double
    /// precision through the whole collect / bounds / localize pipeline and
    /// are only cast to float32 when serialized (ToArray). Casting earlier
    /// quantizes far-from-origin coordinates — float32 resolves ~6 cm at
    /// 700 km and ~12 cm at 2000 km from the Revit internal origin — and the
    /// per-element localization that follows cannot recover what was lost
    /// </summary>
    class VectorData : IComparable<VectorData> {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        public VectorData(double x, double y, double z) {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// From a Revit point (internal coordinates, feet, Z-up), converted
        /// to glTF space in double precision
        /// </summary>
        public VectorData(XYZ vector) {
            var gltfVector = vector.ToGLTFDouble();
            X = gltfVector[0];
            Y = gltfVector[1];
            Z = gltfVector[2];
        }

        /// <summary>
        /// float32 components for the glTF buffer, rounded to 1/10 mm
        /// </summary>
        public float[] ToArray() => new float[] { X.RoundToSingle(), Y.RoundToSingle(), Z.RoundToSingle() };

        public int CompareTo(VectorData a) {
            double d = X - a.X;
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
            var xformed = xform.OfPoint(ToXYZ());
            return new VectorData(xformed.X, xformed.Y, xformed.Z);
        }

        public static VectorData operator +(VectorData left, VectorData right) {
            return new VectorData(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        public static VectorData operator -(VectorData left, VectorData right) {
            return new VectorData(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        public static VectorData operator /(VectorData left, double divisor) {
            return new VectorData(left.X / divisor, left.Y / divisor, left.Z / divisor);
        }
    }
}