using Autodesk.Revit.DB;

namespace GLTFRevitExport.Build.Geometry {
    class FacetData {
        public uint V1 { get; set; }
        public uint V2 { get; set; }
        public uint V3 { get; set; }

        public FacetData(PolymeshFacet f) {
            V1 = (uint)f.V1;
            V2 = (uint)f.V2;
            V3 = (uint)f.V3;
        }
        
        public FacetData() { }

        public uint[] ToArray() => new uint[] { V1, V2, V3 };

        public static FacetData operator +(FacetData left, uint shift) {
            return new FacetData {
                V1 = left.V1 + shift,
                V2 = left.V2 + shift,
                V3 = left.V3 + shift
            };
        }

    }
}