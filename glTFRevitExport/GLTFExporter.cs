using System;
using System.IO;
using System.Collections.Generic;

using Autodesk.Revit.DB;

using GLTF2BIM.GLTF.Package.BaseTypes;
using GLTFRevitExport.Build;
using GLTFRevitExport.Export;
using GLTFRevitExport.Properties;

namespace GLTFRevitExport {
    public class GLTFExporter {
        readonly ExportContext _ctx = null;
        readonly GLTFExportConfigs _configs;
        public static List<Element> NonExportedElements { get; set; } = new List<Element>();
        public GLTFExporter(Document doc, GLTFExportConfigs configs = null)
        {
            _configs = configs ?? new GLTFExportConfigs();
            _ctx = new ExportContext(doc, _configs);
            NonExportedElements = _ctx.NonExportedElements;
        }

        public void ExportView(View view, ElementFilter filter = null) {

            // file log spans the export pass and the build pass; closed at
            // the end of BuildGLTF (or below, if the export pass throws)
            if (_configs.LogToFile)
                Logger.StartFileLog(
                    _configs.LogDirectory ?? Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.LocalApplicationData),
                        "Argyle", "ExportLogs"));

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
                ShouldStopOnError = true,
                // deliver curves/polylines to OnCurve/OnPolyline so thin-line
                // geometry can be exported; without this Revit drops them
                // (verified empirically - no segment callbacks fire otherwise)
                IncludeGeometricObjects = true
            };

            // export View3D was deprecated in Revit 2020 and above
            try {
                exp.Export(view);
            }
            catch {
                // flush the log so failed exports are diagnosable
                Logger.StopFileLog();
                throw;
            }

            // TODO: handle cancel


            //// reset visibility changes
            //view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
        }

        public List<GLTFPackageItem> BuildGLTF(ElementFilter filter = null) {
            try {
                return _ctx.Build(filter);
            }
            finally {
                Logger.StopFileLog();
            }
        }
    }
}