using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;

using Autodesk.Revit.DB;

using GLTF2BIM.GLTF.Extensions.BIM.BaseTypes;
using GLTF2BIM.GLTF.Extensions.BIM.Schema;

using GLTFRevitExport.Build;
using GLTFRevitExport.Extensions;

namespace GLTFRevitExport.GLTF.Extensions.BIM.Revit {
    internal static class glTFRevitUtils {
        const string _revitPrefix = "Revit";

        static readonly BuiltInParameter[] excludeBuiltinParams =
            Enum.GetNames(typeof(BuiltInParameter))
                .Where(x =>
                    x.Contains("_NAME")
                 || x.Contains("NAME_")
                 || x.StartsWith("UNIFORMAT_")
                 || x.StartsWith("OMNICLASS_")
                 || x.StartsWith("HOST_ID_")
                 || x.StartsWith("INSTANCE_FREE_HOST_OFFSET_")
                )
                .Select(x => (BuiltInParameter)Enum.Parse(typeof(BuiltInParameter), x))
                .ToArray();

        public static Dictionary<string, object> GetProperties(glTFBIMExtension target, Element element) {
            // exclude list for parameters that are processed by this
            // constructor and should not be included in 'this.Properties'
            var excludeParams = new List<BuiltInParameter>(excludeBuiltinParams);

            bool isType = element is ElementType;

            // set the properties on this object from their associated builtin params
            foreach (var propInfo in target.GetType().GetProperties()) {
                var apiParamInfo =
                    propInfo.GetCustomAttributes(typeof(RevitBuiltinParametersAttribute), false)
                            .Cast<RevitBuiltinParametersAttribute>()
                            .FirstOrDefault();
                if (apiParamInfo != null) {
                    object paramValue =
                        isType ?
                        GetParamValue(element, apiParamInfo.TypeParam) :
                        GetParamValue(element, apiParamInfo.InstanceParam);

                    // if there is compatible value, set the prop on this
                    if (paramValue != null
                            && propInfo.PropertyType.IsAssignableFrom(paramValue.GetType()))
                        propInfo.SetValue(target, paramValue);

                    // add the processed params to exclude
                    excludeParams.Add(apiParamInfo.TypeParam);
                    excludeParams.Add(apiParamInfo.InstanceParam);
                }
            }

            return GetParamValues(element, exclude: excludeParams);
        }

        public static List<string> GetTaxonomies(Element e) {
            var taxonomies = new List<string>();
            // types show the hierarchical structure of data (vertical)
            if (e is ElementType et) {
                string categoryName = et.Category != null ? et.Category.Name : et.ToString();
                string familyName = et.FamilyName;
                taxonomies.Add(
                        $"{_revitPrefix}/Categories/{categoryName}/{familyName}".UriEncode()
                    );
            }
            // instances show various containers that include them (horizontal)
            else {
                // Element category
                string categoryName = e.Category != null ? e.Category.Name : e.ToString();

                if (e.Document.GetElement(e.GetTypeId()) is ElementType etype) {
                    string familyName = etype.FamilyName;
                    taxonomies.Add(
                        $"{_revitPrefix}/Categories/{categoryName}/{familyName}".UriEncode()
                    );
                }
                else {
                    taxonomies.Add(
                        $"{_revitPrefix}/Categories/{categoryName}".UriEncode()
                    );
                }

                // NOTE: Subcategories are another container but they are applied
                // to sub-elements in external families only
                // Phases
                string createdPhaseName = e.Document.GetElement(e.CreatedPhaseId)?.Name;
                if (createdPhaseName != null)
                    taxonomies.Add(
                            $"{_revitPrefix}/Phases/Created/{createdPhaseName}".UriEncode()
                        );
                string demolishedPhaseName = e.Document.GetElement(e.DemolishedPhaseId)?.Name;
                if (demolishedPhaseName != null)
                    taxonomies.Add(
                        $"{_revitPrefix}/Phases/Demolished/{demolishedPhaseName}".UriEncode()
                    );

                // Design options
                string designOptsName = e.DesignOption?.Name;
                if (designOptsName != null)
                    taxonomies.Add(
                        $"{_revitPrefix}/Design Options/{designOptsName}".UriEncode()
                    );

                // Worksets
                if (e.Document.IsWorkshared
                        && e.WorksetId != WorksetId.InvalidWorksetId) {
                    var ws = e.Document.GetWorksetTable().GetWorkset(e.WorksetId);
                    if (ws != null)
                        taxonomies.Add(
                            $"{_revitPrefix}/WorkSets/{ws.Name}".UriEncode()
                        );
                }

                // Groups
                if (e.GroupId != ElementId.InvalidElementId) {
                    var grp = e.Document.GetElement(e.GroupId);
                    if (grp != null)
                        taxonomies.Add(
                            $"{_revitPrefix}/Groups/{grp.Name}".UriEncode()
                        );
                }
            }

            return taxonomies;
        }

        public static List<string> GetClasses(Element e) {
            var classes = new List<string>();
            // TODO: get correct uniformat category
            classes.Add(
                $"uniformat/{GetParamValue(e, BuiltInParameter.UNIFORMAT_CODE)}".UriEncode()
                );
            classes.Add(
                $"omniclass/{GetParamValue(e, BuiltInParameter.OMNICLASS_CODE)}".UriEncode()
                );

            // TODO: get classed from various industry standards e.g. IFC
            switch (e) {
                case Level level:
                    classes.Add("ifc4/IfcBuildingStorey"); break;
                case Grid grid:
                case MultiSegmentGrid multiGrid:
                    classes.Add("ifc4/IfcGrid"); break;
            }

            return classes;
        }

        /// <summary>
        /// From Jeremy Tammik's RvtVa3c exporter:
        /// https://github.com/va3c/RvtVa3c
        /// Return a dictionary of all the given 
        /// element parameter names and values.
        /// </summary>
        static Dictionary<string, object> GetParamValues(Element e, List<BuiltInParameter> exclude = null) {
            // private function to find a parameter in a list of builins
            bool containsParameter(List<BuiltInParameter> paramList, Parameter param) {
                if (param.Definition is InternalDefinition paramDef)
                    foreach (var paramId in paramList)
                        if (paramDef.Id.IntegerValue == (int)paramId)
                            return true;
                return false;
            }
            // TODO: this needs a formatter for prop name and value
            var paramData = new Dictionary<string, object>();
            foreach (var param in e.GetOrderedParameters()) {
                // exclude requested params (only applies to internal params)
                if (exclude != null && containsParameter(exclude, param))
                    continue;

                // otherwise process the parameter value
                // skip useless names
                string paramName = param.Definition.Name;
                // skip useless values
                var paramValue = param.ToGLTF();
                if (paramValue is null) continue;
                if (paramValue is int intVal && intVal == -1) continue;

                // add value to dict
                if (!paramData.ContainsKey(paramName))
                    paramData.Add(paramName, paramValue);
            }
            return paramData;
        }

        static object GetParamValue(Element e, BuiltInParameter p) {
            if (e.get_Parameter(p) is Parameter param)
                return param.ToGLTF();
            return null;
        }
    }
}
