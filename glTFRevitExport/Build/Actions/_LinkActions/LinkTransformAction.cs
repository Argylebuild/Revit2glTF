using System;
using System.Collections.Generic;

using Autodesk.Revit.DB;

using GLTFRevitExport.GLTF.Extensions.BIM;

namespace GLTFRevitExport.Build.Actions {
    class LinkTransformAction : ElementTransformAction {
        public LinkTransformAction(float[] xform) : base(xform) { }
    }
}
