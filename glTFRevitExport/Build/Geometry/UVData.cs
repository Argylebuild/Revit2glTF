namespace GLTFRevitExport.Build.Geometry {
    /// <summary>
    /// Raw surface UV parameter for one vertex, as returned by Revit's
    /// tessellation (unscaled, bottom-left origin). Scaling by the texture's
    /// real-world repeat size and the glTF V-flip happen at build time.
    /// </summary>
    class UVData {
        public float U { get; set; }
        public float V { get; set; }

        public UVData(double u, double v) {
            U = (float)u;
            V = (float)v;
        }
    }
}
