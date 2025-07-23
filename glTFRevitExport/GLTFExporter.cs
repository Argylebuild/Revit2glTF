using System;
using System.Collections.Generic;

using Autodesk.Revit.DB;

using GLTF2BIM.GLTF.Package.BaseTypes;
using GLTFRevitExport.Export;
using GLTFRevitExport.Properties;

namespace GLTFRevitExport {
    public class GLTFExporter {
        readonly ExportContext _ctx = null;
        public static List<Element> NonExportedElements { get; set; } = new List<Element>();
        public GLTFExporter(Document doc, GLTFExportConfigs configs = null)
        {
            _ctx = new ExportContext(doc, configs ?? new GLTFExportConfigs());
            NonExportedElements = _ctx.NonExportedElements;
        }

        public void ExportView(View view, ElementFilter filter = null) {

            // make sure view is ready for export
            var levelsCat = view.Document.Settings.Categories.get_Item(BuiltInCategory.OST_Levels);
            if (view.GetCategoryHidden(levelsCat.Id))
                throw new Exception("Levels are hidden in this view.");

            //// make necessary view adjustments
            //if (view.CanUseTemporaryVisibilityModes()) {
            //    // make sure levels are visible
            //    view.EnableTemporaryViewPropertiesMode(view.Id);
            //    var levelsCat = view.Document.Settings.Categories.get_Item(BuiltInCategory.OST_Levels);
            //    view.SetCategoryHidden(levelsCat.Id, false);
            //}


            var exp = new CustomExporter(view.Document, _ctx) {
                ShouldStopOnError = true
            };

            // export View3D was deprecated in Revit 2020 and above
            exp.Export(view);

            // TODO: handle cancel


            //// reset visibility changes
            //view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
        }

        public List<GLTFPackageItem> BuildGLTF(ElementFilter filter = null)
            => _ctx.Build(filter);
    }
}