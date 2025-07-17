using System;
using Vector3D = System.Numerics.Vector3;
using Autodesk.Revit.DB;

using GLTF2BIM.GLTF.Extensions.BIM.Schema;

namespace GLTFRevitExport.Extensions {
    static class RevitAPIExtensions {
        // Z-Up to Y-Up basis transform
        public static Transform ZTOY =
            Transform.CreateRotation(new XYZ(1, 0, 0), -Math.PI / 2.0);

        public static string GetId(this Element e) => e?.UniqueId;

        public static string GetId(this Color c)
            => (
            "#"
            + c.Red.ToString("X2")
            + c.Blue.ToString("X2")
            + c.Green.ToString("X2")
            ).ToLower();

        public static bool Compare(this Color left, Color right)
            => left.Red == right.Red
            && left.Blue == right.Blue
            && left.Green == right.Green;

        /// <summary>
        /// From Jeremy Tammik's RvtVa3c exporter:
        /// </summary>
        // https://github.com/va3c/RvtVa3c
        // Return an integer value for a Revit Color.
        public static float[] ToGLTF(this Color color, float transparency = 0f) {
            return new float[] {
                color.Red / 255f,
                color.Green / 255f,
                color.Blue / 255f,
                1f - transparency
            };
        }

        public static float[] ToGLTF(this XYZ vector) {
            var np = ZTOY.OfPoint(vector);
            return new float[] { np.X.ToGLTFLength(), np.Y.ToGLTFLength(), np.Z.ToGLTFLength() };
        }

        /// <summary>
        /// Convert Revit transform to floating-point 4x4 transformation
        /// matrix stored in column major order
        /// </summary>
        public static float[] ToGLTF(this Transform xform) {
            if (xform == null || xform.IsIdentity) return null;

            var yupxform = ZTOY * xform * ZTOY.Inverse;

            var bx = yupxform.BasisX;
            var by = yupxform.BasisY;
            var bz = yupxform.BasisZ;
            var or = yupxform.Origin;

            return new float[16] {
                bx.X.ToSingle(),         bx.Y.ToSingle(),         bx.Z.ToSingle(),         0f,
                by.X.ToSingle(),         by.Y.ToSingle(),         by.Z.ToSingle(),         0f,
                bz.X.ToSingle(),         bz.Y.ToSingle(),         bz.Z.ToSingle(),         0f,
                or.X.ToGLTFLength(),     or.Y.ToGLTFLength(),     or.Z.ToGLTFLength(),     1f
            };
        }

        public static Transform FromGLTFMatrix(this float[] matrix) {
            var xform = Transform.Identity;
            xform.BasisX = new XYZ(matrix[0], matrix[1], matrix[2]);
            xform.BasisY = new XYZ(matrix[4], matrix[5], matrix[6]);
            xform.BasisZ = new XYZ(matrix[8], matrix[9], matrix[10]);
            xform.Origin = new XYZ(matrix[12], matrix[13], matrix[14]);
            return xform;
        }

        public static double ToGLTF(this Parameter p, double value) {
            // TODO: read value unit and convert correctly
            string typeId = p.Definition.GetDataType().TypeId;
            if (typeId.StartsWith("autodesk.spec.aec:length"))
                return value.ToGLTFLength();
            return value;
        }

        public static Tuple<string,object> ToGLTF(this Parameter param) {
            try
            {
                object value = null;
				switch (param.StorageType)
				{
					case StorageType.None: break;

					case StorageType.String:
						value = param.AsString();
                        break;

					case StorageType.Integer:
#if REVIT2022 || REVIT2023 || REVIT2024
                    if (param.Definition.GetDataType().TypeId.StartsWith("autodesk.spec:spec.bool"))
#else
						if (param.Definition.ParameterType == ParameterType.YesNo)
#endif
							value = param.AsInteger() != 0;
						else
							value = param.AsInteger();
                    break;

					case StorageType.Double:
						value = param.ToGLTF(param.AsDouble());
                        break;

					case StorageType.ElementId:
						value = param.AsElementId().IntegerValue;
                        break;
				}

                string typeString = param.Definition.GetDataType().TypeId;
                return new Tuple<string, object>(TrimParamType(typeString), value);
			}
            catch (Exception)
            {
                Console.WriteLine($"Error converting parameter {param.Definition.Name} to glTF");
            }
            return null;
        }

        private static string TrimParamType(string typeId)
        {


			if (!string.IsNullOrWhiteSpace(typeId))
			{
				int colonIndex = typeId.IndexOf(":");
				int dashIndex = typeId.IndexOf("-");

				if (colonIndex != -1 && dashIndex != -1)
				{
					string trimmedTypeId = typeId.Substring(colonIndex + 1, dashIndex - colonIndex - 1);
                    
                    int dotIndex = trimmedTypeId.LastIndexOf('.');
                    if(dotIndex != -1)
						trimmedTypeId = trimmedTypeId.Substring(dotIndex + 1);

					return trimmedTypeId;
				}
			}

            return "generic";
		}

        public static bool IsBIC(this Category c, BuiltInCategory bic)
            => c.Id.IntegerValue == (int)bic;


        public static glTFBIMBounds TOGLTFBounds(this BoundingBoxXYZ bbox) {
            return new glTFBIMBounds(
                min: bbox.Min.ToGLTFVector(),
                max: bbox.Max.ToGLTFVector()
                );
        }

        public static glTFBIMVector ToGLTFVector(this XYZ pt) {
            float[] vector = pt.ToGLTF();
            return new glTFBIMVector(
                x: vector[0],
                y: vector[1],
                z: vector[2]
            );
        }

        public static bool CurveIntersects(this BoundingBoxXYZ bbox, Curve curve)
        {
            if (bbox == null || curve == null)
            {
                return false;
            }

            var startPoint = curve.Evaluate(0, true);
            var endPoint = curve.Evaluate(1, true);

            var b1 = Utils.CreateVec3DByPoint(bbox.Min);
            var b2 = Utils.CreateVec3DByPoint(bbox.Max); 
            var l1 = Utils.CreateVec3DByPoint(startPoint); 
            var l2 = Utils.CreateVec3DByPoint(endPoint);
            var hit = new Vector3D();

            return GeometryUtils.BBoxCurveIntersection(b1, b2, l1, l2, ref hit);
        }
    }
}
