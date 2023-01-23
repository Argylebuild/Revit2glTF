using System;
using System.Collections.Generic;

using Autodesk.Revit.DB;

using GLTF2BIM.GLTF.Extensions.BIM.Schema;
using GLTF2BIM.GLTF.Extensions.BIM.Containers;

using GLTFRevitExport.Build;
using GLTFRevitExport.Extensions;

namespace GLTFRevitExport.GLTF.Extensions.BIM.Revit {
    [Serializable]
    class glTFRevitDocumentExt : glTFBIMAssetExtension {
        public glTFRevitDocumentExt(Document d, BuildContext ctx) : base() {
            App = GetAppName(d);
            Id = GetDocumentId(d).ToString();
            Title = d.Title;
            Source = d.PathName;
            if (ctx.Configs.ExportParameters) {
                if (ctx.PropertyContainer is glTFBIMPropertyContainer) {
                    // record properties
                    ctx.PropertyContainer.Record(Id, GetProjectInfo(d));
                    // ensure property sources list is initialized
                    if (Containers is null)
                        Containers = new List<glTFBIMPropertyContainer>();
                    // add the new property source
                    if (!Containers.Contains(ctx.PropertyContainer))
                        Containers.Add(ctx.PropertyContainer);
                }
                else
                    // embed properties
                    Properties = GetProjectInfo(d);
            }
        }

        static string GetAppName(Document doc) {
            var app = doc.Application;
            var hostName = app.VersionName;
            hostName = hostName.Replace(app.VersionNumber, app.SubVersionNumber);
            return $"{hostName} {app.VersionBuild}";
        }

        static Guid GetDocumentId(Document doc) {
            if (doc?.IsValidObject != true)
                return Guid.Empty;
            return ExportUtils.GetGBXMLDocumentId(doc);
        }

        static Dictionary<string, object> GetProjectInfo(Document doc) {
            var docProps = new Dictionary<string, object>();
            if (doc != null) {
                var pinfo = doc.ProjectInformation;

                foreach (BuiltInParameter paramId in new BuiltInParameter[] {
                    BuiltInParameter.PROJECT_ORGANIZATION_NAME,
                    BuiltInParameter.PROJECT_ORGANIZATION_DESCRIPTION,
                    BuiltInParameter.PROJECT_NUMBER,
                    BuiltInParameter.PROJECT_NAME,
                    BuiltInParameter.CLIENT_NAME,
                    BuiltInParameter.PROJECT_BUILDING_NAME,
                    BuiltInParameter.PROJECT_ISSUE_DATE,
                    BuiltInParameter.PROJECT_STATUS,
                    BuiltInParameter.PROJECT_AUTHOR,
                    BuiltInParameter.PROJECT_ADDRESS,
                }) {
                    var param = pinfo.get_Parameter(paramId);
                    if (param != null) {
                        var paramValue = param.ToGLTF();
                        if (paramValue != null)
                            docProps.Add(GetValidName(param), paramValue);
                    }
                }

                foreach (Parameter param in pinfo.Parameters)
                    if (param.Id.IntegerValue > 0) {
                        var paramValue = param.ToGLTF();
                        if (paramValue != null)
                            docProps.Add(GetValidName(param), paramValue);
                    }
            }
            return docProps;


            string GetValidName(Parameter param)
			{
                string paramName = param.Definition.Name;
				if (docProps.ContainsKey(paramName))
				{
                    int i = 1;
                    while (docProps.ContainsKey($"{paramName}{i}"))
                        i++;
                    paramName += i;
				}

                return paramName;
			}

        }
    }
}
