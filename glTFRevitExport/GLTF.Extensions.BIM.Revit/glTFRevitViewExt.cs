using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;

using Autodesk.Revit.DB;

using GLTF2BIM.GLTF.Extensions.BIM.Schema;
using GLTF2BIM.GLTF.Extensions.BIM.Containers;

using GLTFRevitExport.Build;
using GLTFRevitExport.Extensions;

namespace GLTFRevitExport.GLTF.Extensions.BIM.Revit {
    class glTFRevitViewExt : glTFBIMSceneExtension {
        public glTFRevitViewExt() {
            Id = Guid.NewGuid().ToString();
        }

        public glTFRevitViewExt(View v, BuildContext ctx) {
            // identity data
            Id = (v as Element).GetId();
            Taxonomies = glTFRevitUtils.GetTaxonomies(v);

            // include parameters
            if (ctx.Configs.ExportParameters) {
                if (ctx.PropertyContainer is glTFBIMPropertyContainer)
                    // record properties
                    ctx.PropertyContainer.Record(Id, glTFRevitUtils.GetProperties(this, v));
                else
                    // embed properties
                    Properties = glTFRevitUtils.GetProperties(this, v);
            }
        }
    }
}
