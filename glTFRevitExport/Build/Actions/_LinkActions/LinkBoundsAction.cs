﻿using System;
using System.Collections.Generic;

using Autodesk.Revit.DB;

using GLTFRevitExport.GLTF.Extensions.BIM;

namespace GLTFRevitExport.Build.Actions {
    class LinkBoundsAction : ElementBoundsAction {
        public LinkBoundsAction() : base(null) { }

        public glTFBIMBounds Bounds {
            get => _bounds;
            set => _bounds = value;
        }
    }
}
