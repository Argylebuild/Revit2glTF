using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.IO;
using System.Linq;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Visual;
using Autodesk.Revit.DB.Architecture;

using GLTF2BIM.GLTF;
using GLTF2BIM.GLTF.Schema;
using GLTF2BIM.GLTF.Extensions.BIM.Schema;
using GLTFRevitExport.Properties;
using GLTFRevitExport.Extensions;

namespace GLTFRevitExport.Build.Actions.BaseTypes {
    abstract class BaseAction {
        public abstract void Execute(BuildContext ctx);
    }

    abstract class BaseElementAction : BaseAction {
        protected Element element;

        public BaseElementAction(Element e) => element = e;

        public bool Passes(ElementFilter filter) {
            if (element is null)
                return true;
            return filter.PassesFilter(element);
        }
    }

    abstract class BuildBeginAction : BaseElementAction {
        public BuildBeginAction(Element e) : base(e) { }
    }

    abstract class BuildEndAction : BaseAction { }
}