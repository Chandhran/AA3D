using System;
using Grasshopper.Kernel;

namespace AA3D
{
    /// <summary>
    /// Grasshopper plugin registration. GH discovers this via reflection.
    /// </summary>
    public class AA3DPluginInfo : GH_AssemblyInfo
    {
        public override string Name        => "AA3D";
        public override string Description => "Local AI Architectural Generator — uses Ollama (qwen2.5-coder) to " +
                                              "generate 3-D architecture directly on the Grasshopper canvas.";
        public override string AuthorName  => "Architecture Atlas";
        public override string AuthorContact => "architectureatlas.india@gmail.com";
        public override Guid   Id          => new Guid("A3D10000-0000-0000-0000-AA3D00000001");
        // Return null to use default GH icon; replace with a 24×24 Bitmap for a custom icon.
        public override System.Drawing.Bitmap Icon => null;
    }
}
