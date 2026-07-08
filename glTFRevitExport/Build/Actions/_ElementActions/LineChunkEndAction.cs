using GLTFRevitExport.Build.Actions.BaseTypes;

namespace GLTFRevitExport.Build.Actions {
    /// <summary>
    /// Closes a linework chunk node opened by LineChunkBeginAction
    /// (one node, unlike ElementEndAction which may also close a type node)
    /// </summary>
    class LineChunkEndAction : BaseAction {
        public override void Execute(BuildContext ctx) {
            Logger.Log("- line chunk end");
            ctx.Builder.CloseNode();
        }
    }
}
