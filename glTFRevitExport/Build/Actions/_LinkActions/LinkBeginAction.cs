using System;
using System.Collections.Generic;

using Autodesk.Revit.DB;

using GLTFRevitExport.GLTF.Extensions.BIM;

namespace GLTFRevitExport.Build.Actions {
    class LinkBeginAction : ElementBeginAction {
        public Document LinkDocument { get; private set; }
        public string LinkId { get; private set; }

        public LinkBeginAction(RevitLinkInstance link, RevitLinkType linkType, Document linkedDoc)
            : base(link, linkType) {
            LinkDocument = linkedDoc;
            LinkId = element.UniqueId;
        }
    }
}
