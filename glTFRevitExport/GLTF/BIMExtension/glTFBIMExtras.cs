﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GLTFRevitExport.GLTF.Types.BIMExtension {
    /// <summary>
    /// 
    /// </summary>
    public class glTFBIMExtras: glTFExtras {
        /// <summary>
        /// The Revit created UniqueId for this object
        /// </summary>
        public string UniqueId { get; set; }

        public GridParameters GridParameters { get; set; }

        public Dictionary<string, string> Properties { get; set; }
    }
}