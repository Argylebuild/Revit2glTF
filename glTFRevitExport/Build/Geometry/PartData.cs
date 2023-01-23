using Autodesk.Revit.DB;

namespace GLTFRevitExport.Build.Geometry {
    class PartData {
        public PartData(PrimitiveData primitive) => Primitive = primitive;

        public PrimitiveData Primitive;

        public Material Material;
        public Color Color;
        public double Transparency;

        public bool HasPartData => Primitive != null;

        public static PartData operator +(PartData left, PartData right) {
            PrimitiveData prim;
            if (left.Primitive is null)
                prim = right.Primitive;
            else if (right.Primitive is null)
                prim = left.Primitive;
            else
                prim = left.Primitive + right.Primitive;

            return new PartData(prim) {
                Material = left.Material,
                Color = left.Color,
                Transparency = left.Transparency,
            };
        }
    }
}