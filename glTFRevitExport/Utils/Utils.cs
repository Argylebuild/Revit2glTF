using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;
using Vector3D = System.Numerics.Vector3;

namespace GLTFRevitExport
{
    public class Utils
    {
        /// <summary>
        /// Retrieve the unified Bounding Box that contains all the visible element's Bounding Boxes in the current view.
        /// </summary>
        /// <param name="doc"></param>
        /// <returns></returns>
        public static BoundingBoxXYZ GetVisibleElementsBBox(Document doc, List<Element> elements)
        {
            var bbox = new BoundingBoxXYZ();
            var elementBbox = new BoundingBoxXYZ();

            foreach (Element e in elements)
            {
                elementBbox = e.get_BoundingBox(doc.ActiveView);

                if (elementBbox != null)
                {
                    if (bbox.Max == null)
                    {
                        bbox.Max = elementBbox.Max;
                    }
                    else
                    {
                        bbox.Max = new XYZ(
                          Math.Max(elementBbox.Max.X, bbox.Max.X),
                          Math.Max(elementBbox.Max.Y, bbox.Max.Y),
                          Math.Max(elementBbox.Max.Z, bbox.Max.Z)
                        );
                    }

                    if (bbox.Min == null)
                    {
                        bbox.Min = elementBbox.Min;
                    }
                    else
                    {
                        bbox.Min = new XYZ(
                            Math.Min(elementBbox.Min.X, bbox.Min.X),
                            Math.Min(elementBbox.Min.Y, bbox.Min.Y),
                            Math.Min(elementBbox.Min.Z, bbox.Min.Z)
                          );
                    }
                }
            }

            return bbox;
        }
    }
}
