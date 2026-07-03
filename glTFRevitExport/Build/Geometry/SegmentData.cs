namespace GLTFRevitExport.Build.Geometry {
    class SegmentData {
        public uint V1 { get; set; }
        public uint V2 { get; set; }

        public uint[] ToArray() => new uint[] { V1, V2 };

        public static SegmentData operator +(SegmentData left, uint shift) {
            return new SegmentData {
                V1 = left.V1 + shift,
                V2 = left.V2 + shift
            };
        }
    }
}
