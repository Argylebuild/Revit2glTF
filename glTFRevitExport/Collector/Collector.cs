using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;

namespace GLTFRevitExport
{
    public static class Collector
    {
        public static List<Grid> GetAllGrids (Document doc)
        {
            using (var filter = new FilteredElementCollector(doc))
            {
                return filter
                    .OfCategory(BuiltInCategory.OST_Grids)
                    .WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Grid>()
                    .ToList();
            }
        }
        public static List<Element> GetVisibleElements(Document doc)
        {
            using (var filter = new FilteredElementCollector(doc))
            {
                return filter
                    .WhereElementIsNotElementType()
                    .ToElements()
                    .ToList();
            }
        }        
    }
}
