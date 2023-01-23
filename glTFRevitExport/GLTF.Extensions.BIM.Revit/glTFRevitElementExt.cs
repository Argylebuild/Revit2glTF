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
    [Serializable]
    class glTFRevitElementExt : glTFBIMNodeExtension {
        public glTFRevitElementExt() {
            Id = Guid.NewGuid().ToString();
        }

        public glTFRevitElementExt(Element e, BuildContext ctx) {
            // identity data
            Id = e.GetId();
            Taxonomies = glTFRevitUtils.GetTaxonomies(e);
            Classes = glTFRevitUtils.GetClasses(e);

            // include parameters
            if (ctx.Configs.ExportParameters) {
                if (ctx.PropertyContainer is glTFBIMPropertyContainer)
                    // record properties
                    ctx.PropertyContainer.Record(Id, glTFRevitUtils.GetProperties(this, e));
                else
                    // embed properties
                    Properties = glTFRevitUtils.GetProperties(this, e);
            }
        }

        [JsonProperty("mark", Order = 4)]
        [RevitBuiltinParameters(
            BuiltInParameter.ALL_MODEL_TYPE_MARK,
            BuiltInParameter.ALL_MODEL_MARK
            )
        ]
        public override string Mark { get; set; }

        [JsonProperty("description", Order = 5)]
        [RevitBuiltinParameters(
            BuiltInParameter.ALL_MODEL_DESCRIPTION,
            BuiltInParameter.ALL_MODEL_DESCRIPTION
            )
        ]
        public override string Description { get; set; }

        [JsonProperty("comment", Order = 6)]
        [RevitBuiltinParameters(
            BuiltInParameter.ALL_MODEL_TYPE_COMMENTS,
            BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS
            )
        ]
        public override string Comment { get; set; }


        [JsonProperty("dataUrl", Order = 8)]
        [RevitBuiltinParameters(
            BuiltInParameter.ALL_MODEL_URL,
            BuiltInParameter.ALL_MODEL_URL
            )
        ]
        public override string DataUrl { get; set; }

        [JsonProperty("imageUrl", Order = 9)]
        [RevitBuiltinParameters(
            BuiltInParameter.ALL_MODEL_TYPE_IMAGE,
            BuiltInParameter.ALL_MODEL_IMAGE
            )
        ]
        public override string ImageUrl { get; set; }
    }
}
