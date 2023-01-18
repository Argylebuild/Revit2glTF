using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

using GLTF2BIM.GLTF.Schema;
using GLTF2BIM.GLTF.Package;
using GLTF2BIM.GLTF.Extensions.BIM.Schema;
using GLTFRevitExport.Extensions;
using GLTFRevitExport.Build;
using GLTFRevitExport.Build.Actions;
using GLTFRevitExport.Build.Geometry;

namespace GLTFRevitExport.Export {
#if REVIT2019
    sealed partial class ExportContext : IExportContext, IModelExportContext {
#else
    sealed partial class ExportContext : IExportContext, IExportContextBase, IModelExportContext {
#endif
        public List<Element> NonExportedElements { get; set; } = new List<Element>();

        #region Start, Stop, Cancel
        // Runs once at beginning of export. Sets up the root node
        // and scene.
        public bool Start() {
            // Do not need to do anything here
            // _glTF is already instantiated
            Logger.Log("+ start collect");

            // reset other stacks
            _processed.Clear();
            _skipElement = false;

            var doc = _docStack.Last();
            _docStack.Clear();
            // place the root document on the stack
            _docStack.Push(doc);

            return true;
        }

        // Runs once at end of export
        // Collects any data that is not passed by default to this context
        public void Finish() {
            Logger.Log("- end collect");
        }

        // This method is invoked many times during the export process
        public bool IsCanceled() {
            if (_cfgs.CancelToken.IsCancellationRequested) {
                Logger.Log("x cancelled");
                ResetExporter();
            }
            return _cfgs.CancelToken.IsCancellationRequested;
        }
        #endregion

        #region Views
        // revit calls this on every view that is being processed
        // all other methods are called after a view has begun
        public RenderNodeAction OnViewBegin(ViewNode node) {
            // if active doc and view is valid
            if (_docStack.Peek() is Document doc) {
                if (doc.GetElement(node.ViewId) is View view) {
                    if (RecordOrSkip(view, "x duplicate view", setFlag: true))
                        return RenderNodeAction.Skip;

                    // if active doc and view is valid
                    _actions.Enqueue(new SceneBeginAction(view: view));
                    _viewStack.Push(view);

                    // add an action to the queue that collects the elements
                    // not collected by the IExporter
                    QueueLevelActions(doc, view);
                    QueueGridActions(doc);
                    QueuePartFromElementActions(
                        doc,
                        view,
                        new ElementClassFilter(typeof(TopographySurface))
                        );

                    Logger.LogElement("+ view begin", view);
                    return RenderNodeAction.Proceed;
                }
            }
            // otherwise skip the view
            return RenderNodeAction.Skip;
        }

        void QueueLevelActions(Document doc, View view) {
            Logger.Log("> collecting levels");

            // collect levels from project or view only?
            foreach (var e in new FilteredElementCollector(doc, view.Id)
                                  .OfCategory(BuiltInCategory.OST_Levels)
                                  .WhereElementIsNotElementType())
                _actions.Enqueue(
                    new LevelAction(element: e, extents: e.get_BoundingBox(view))
                    );
        }

        void QueueGridActions(Document doc) {
            Logger.Log("> collecting grids");

            // first collect the multisegment grids and record their children
            // multi-segment grids are not supported and the segments will not
            // be procesed as grids
            var childGrids = new HashSet<ElementId>();
            foreach (var e in new FilteredElementCollector(doc).OfClass(typeof(MultiSegmentGrid)).WhereElementIsNotElementType()) {
                if (e is MultiSegmentGrid multiGrid) {
                    childGrids.UnionWith(multiGrid.GetGridIds());
                }
            }

            // then record the rest of the grids and omit the already recorded ones
            foreach (var e in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Grids).WhereElementIsNotElementType())
                if (!childGrids.Contains(e.Id))
                    _actions.Enqueue(new GridAction(element: e));
        }

        void QueuePartFromElementActions(Document doc, View view, ElementFilter filter) {
            foreach (var e in new FilteredElementCollector(view.Document, view.Id).WherePasses(filter))
                _actions.Enqueue(new PartFromElementAction(view: view, element: e));
        }

        public void OnViewEnd(ElementId elementId) {
            if (_skipElement)
                _skipElement = false;
            else {
                Logger.Log("- view end");
                _actions.Enqueue(new SceneEndAction());
                _viewStack.Pop();
            }
        }
        #endregion

        #region Elements
        // Runs once for each element.
        public RenderNodeAction OnElementBegin(ElementId eid) {
            if (_docStack.Peek() is Document doc) {
                Element e = doc.GetElement(eid);

                // TODO: take a look at elements that have no type
                // skipping these for now
                // DB.CurtainGridLine
                // DB.Opening
                // DB.FaceSplitter
                // DB.Spatial
                if (!(doc.GetElement(e.GetTypeId()) is ElementType et))
                    goto SkipElementLabel;

                // TODO: fix inneficiency in getting linked elements multiple times
                // this affects links that have multiple instances
                // remember glTF nodes can not have multiple parents
                // https://github.com/KhronosGroup/glTF/tree/master/specification/2.0#nodes-and-hierarchy
                if (!doc.IsLinked) {
                    // check if this element has been processed before
                    if (RecordOrSkip(e, "x duplicate element", setFlag: true))
                        return RenderNodeAction.Skip;
                }

                // Begin: Element
                switch (e) {
                    // Skip all these element types
                    case View _:
                    case Level _:
                    case Grid _:
                        goto SkipElementLabel;

                    case RevitLinkInstance linkInst:
                        if (_cfgs.ExportLinkedModels) {
                            Logger.LogElement("+ element (link) begin", e);
                            _actions.Enqueue(
                                new LinkBeginAction(
                                    link: linkInst,
                                    linkType: (RevitLinkType)et,
                                    linkedDoc: linkInst.GetLinkDocument()
                                    )
                                );
                            break;
                        }
                        else {
                            Logger.Log("~ exclude link element");
                            goto SkipElementLabel;
                        }

                    case FamilyInstance famInst:
                        Logger.LogElement("+ element (instance) begin", e);
                        _actions.Enqueue(
                            new ElementBeginAction(element: famInst, type: et)
                            );
                        break;

                    case Element generic:
                        var c = e.Category;
                        if (c is null) {
                            Logger.LogElement($"+ element (generic) begin", e);
                            _actions.Enqueue(
                                new ElementBeginAction(
                                    element: generic,
                                    type: et
                                    )
                                );
                        }
                        else {
                            if (c.IsBIC(BuiltInCategory.OST_Cameras)) {
                                // TODO: enqueue camera node
                                goto SkipElementLabel;
                            }
                            else {
                                var cname = c.Name.ToLower();
                                Logger.LogElement($"+ element ({cname}) begin", e);
                                _actions.Enqueue(
                                    new ElementBeginAction(
                                        element: generic,
                                        type: et
                                        )
                                    );
                            }
                        }
                        break;
                }

                return RenderNodeAction.Proceed;
            }
            return RenderNodeAction.Skip;

        SkipElementLabel:
            _skipElement = true;
            return RenderNodeAction.Skip;
        }

        // Runs at the end of an element being processed, after all other calls for that element.
        public void OnElementEnd(ElementId eid) {
            if (_skipElement)
                _skipElement = false;
            else {
                // if has mesh data
                if (_partStack.Count > 0) {
                    // calculate the bounding box from the parts data
                    glTFBIMBounds bounds;
                    switch (_actions.Last()) {
                        // when element is a Revit family instance
                        case ElementTransformAction etAction:
                            // transform bounds with existing transform
                            Logger.Log("> determine instance bounding box");
                            bounds = CalculateBounds(etAction.Matrix);
                            break;

                        // when element is a system family
                        default:
                            Logger.Log("> determine bounding box");
                            bounds = CalculateBounds();

                            Logger.Log("> localized transform");
                            float[] xform = LocalizePartStack();
                            _actions.Enqueue(new ElementTransformAction(xform));
                            break;
                    }

                    _actions.Enqueue(new ElementBoundsAction(bounds));

                    // Preventing elements which have null lists of faces or vertices.
                    bool shouldSkip = false;
                    foreach (var pd in _partStack)
                    {
                        if (!pd.Primitive.Vertices.Any() || !pd.Primitive.Faces.Any())
                        {
                            shouldSkip = true;
                            if (_docStack.Peek() is Document document)
                            {
                                try
                                {
                                    Element e = document.GetElement(eid);
                                    NonExportedElements.Add(e);
                                }
                                catch { }
                            }
                            break;
                        }
                    }

                    if (!shouldSkip)
                    {
                        foreach (var partData in _partStack)
                            _actions.Enqueue(new PartFromDataAction(partData));
                    }
                }
                _partStack.Clear();

                // end the element
                Logger.Log("- element end");
                if (_docStack.Peek() is Document doc) {
                    Element e = doc.GetElement(eid);

                    if (e is RevitLinkInstance)
                        _actions.Enqueue(new LinkEndAction());
                    else
                        _actions.Enqueue(new ElementEndAction());
                }
            }
        }

        // This is called when family instances are encountered, after OnElementBegin
        public RenderNodeAction OnInstanceBegin(InstanceNode node) {
            Logger.Log("+ instance start");
            return RenderNodeAction.Proceed;
        }

        public void OnInstanceEnd(InstanceNode node) {
            // NOTE: only add the transform if geometry has already collected
            // for this instance, from the OnFace and OnPolymesh calls between
            // OnInstanceBegin and  OnInstanceEnd
            if (_partStack.Count > 0) {
                Logger.Log("> transform");
                float[] matrix = node.GetTransform().ToGLTF();
                _actions.Enqueue(new ElementTransformAction(matrix));
            }
            Logger.Log("- instance end");
        }
        #endregion

        #region Linked Models
        public RenderNodeAction OnLinkBegin(LinkNode node) {
            if (_docStack.Peek() is Document) {
                if (_cfgs.ExportLinkedModels) {
                    // Link element info is processed by the OnElement before
                    // we will just push the linked doc in the stack
                    // so all subsequent calls to OnElement can grab the element
                    // from the linked document correctly
                    Logger.Log("+ link document begin");
                    _docStack.Push(node.GetDocument());

                    Logger.Log("> link matrix");
                    _linkMatrix = node.GetTransform().ToGLTF();

                    return RenderNodeAction.Proceed;
                }
                else
                    Logger.Log("~ exclude link document");
            }
            return RenderNodeAction.Skip;
        }

        public void OnLinkEnd(LinkNode node) {
            if (_skipElement)
                _skipElement = false;
            else {
                if (_cfgs.ExportLinkedModels) {

                    Logger.Log("> transform (link)");
                    float[] matrix = node.GetTransform().ToGLTF();
                    _actions.Enqueue(new LinkTransformAction(matrix));

                    if (!_cfgs.EmbedLinkedModels)
                        _actions.Enqueue(new LinkBoundsAction());

                    Logger.Log("- link document end");
                    _docStack.Pop();
                    _linkMatrix = null;
                }
            }
        }
        #endregion

        #region Material and Geometry
        // Runs every time, and immediately prior to, a mesh being processed
        // e.g. OnMaterial->OnFace->OnPolymesh
        // It supplies the material for the mesh, and we use this to create
        // a new material in our material container, or switch the
        // current material if it already exists
        // TODO: Handle more complex materials.
        public void OnMaterial(MaterialNode node) {
            if (_docStack.Peek() is Document doc) {
                Material m = doc.GetElement(node.MaterialId) as Material;
                // if there is a material element
                if (m != null) {
                    // if mesh stack has a mesh
                    if (_partStack.Count > 0
                            && _partStack.Peek() is PartData partPrim) {
                        // if material is same as active, ignore
                        if (partPrim.Material != null
                                && m.UniqueId == partPrim.Material.UniqueId) {
                            Logger.Log("> material keep");
                            return;
                        }
                    }
                    Logger.LogElement("> material", m);
                    _partStack.Push(
                        new PartData(primitive: null) {
                            Material = m,
                            Color = node.Color,
                            Transparency = node.Transparency
                        });
                }
                // or there is no material
                // lets grab the color and transparency from node
                else {
                    Logger.Log("x material empty (use color)");
                    // if mesh stack has a mesh
                    if (_partStack.Count > 0
                            && _partStack.Peek() is PartData partPrim) {
                        // if color and transparency are the same
                        if (partPrim.Material is null
                                && node.Color.IsValid
                                && partPrim.Color.IsValid
                                && node.Color.Compare(partPrim.Color)
                                && node.Transparency == partPrim.Transparency) {
                            Logger.Log("> material keep");
                            return;
                        }
                    }
                    Logger.LogElement("> material", m);
                    _partStack.Push(
                        new PartData(primitive: null) {
                            Color = node.Color,
                            Transparency = node.Transparency
                        });
                }
            }
        }

        // provides access to the DB.Face that includes the polymesh
        // can be used to extract more information from the actual face
        public RenderNodeAction OnFaceBegin(FaceNode node) {
            Logger.Log("+ face begin");
            return RenderNodeAction.Proceed;
        }

        // Runs for every polymesh being processed. Typically this is a single
        // face of an element's mesh
        public void OnPolymesh(PolymeshTopology polymesh) {
            // TODO: anything to do with .GetUV?
            if (_partStack.Count > 0) {
                Logger.Log("> polymesh");
                var activePart = _partStack.Peek();

                List<VectorData> vertices =
                    polymesh.GetPoints().Select(x => new VectorData(x)).ToList();

                List<FacetData> faces =
                    polymesh.GetFacets().Select(x => new FacetData(x)).ToList();

                var newPrim = new PrimitiveData(vertices, faces);

                if (activePart.HasPartData)
                    activePart.Primitive += newPrim;
                else
                    activePart.Primitive = newPrim;
            }
        }

        public void OnFaceEnd(FaceNode node) {
            Logger.Log("- face end");
        }
        #endregion

        #region Misc
        public void OnRPC(RPCNode node) {
            Logger.Log("> rpc");
        }

        public void OnLight(LightNode node) {
            Logger.Log("> light");
        }

        public RenderNodeAction OnCurve(CurveNode node) {
            Logger.Log("> curve");
            return RenderNodeAction.Skip;
        }

        public RenderNodeAction OnPolyline(PolylineNode node) {
            Logger.Log("> polyline");
            return RenderNodeAction.Skip;
        }

        public void OnLineSegment(LineSegment segment) {
            Logger.Log("> line segment");
        }

        public void OnPolylineSegments(PolylineSegments segments) {
            Logger.Log("> polyline segment");
        }

        public void OnText(TextNode node) {
            Logger.Log("> text");
        }

        public RenderNodeAction OnPoint(PointNode node) {
            Logger.Log("> point");
            return RenderNodeAction.Skip;
        }

        //public RenderNodeAction OnElementBegin2D(ElementNode node) {
        //    Logger.Log("+ element begin 2d");
        //    return RenderNodeAction.Proceed;
        //}

        //public void OnElementEnd2D(ElementNode node) {
        //    Logger.Log("- element end 2d");
        //}

        //public RenderNodeAction OnFaceEdge2D(FaceEdgeNode node) {
        //    Logger.Log("> face edge 2d");
        //    return RenderNodeAction.Proceed;
        //}

        //public RenderNodeAction OnFaceSilhouette2D(FaceSilhouetteNode node) {
        //    Logger.Log("> face silhouette 2d");
        //    return RenderNodeAction.Proceed;
        //}
        #endregion
    }
}
