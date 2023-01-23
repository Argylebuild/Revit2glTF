using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Autodesk.Revit.DB;

using GLTFRevitExport.GLTF;

namespace GLTFRevitExport.Extensions {
    static class StringExtensions {
        public static string UriEncode(this string source)
            //=> HttpUtility.UrlPathEncode(source);
            => source;
    }
}
