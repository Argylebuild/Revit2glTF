using System;

using Autodesk.Revit.DB;

namespace GLTFRevitExport.GLTF.Extensions.BIM.Revit {
    [AttributeUsage(AttributeTargets.Property)]
    class RevitBuiltinParametersAttribute : Attribute {
        public BuiltInParameter TypeParam { get; private set; }
        public BuiltInParameter InstanceParam { get; private set; }

        public RevitBuiltinParametersAttribute(BuiltInParameter typeParam, BuiltInParameter instParam) {
            TypeParam = typeParam;
            InstanceParam = instParam;
        }
    }
}
