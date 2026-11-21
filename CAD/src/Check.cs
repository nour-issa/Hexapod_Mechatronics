using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

class Check
{
    static void Main(string[] a)
    {
        var sw = (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
        var doc = (ModelDoc2)sw.ActiveDoc;
        Console.WriteLine("Active: " + doc.GetTitle());
        var asm = (AssemblyDoc)doc;
        var idm = asm.InterferenceDetectionManager;
        idm.TreatCoincidenceAsInterference = false;
        idm.IncludeMultibodyPartInterferences = false;
        var res = (object[])idm.GetInterferences();
        Console.WriteLine("Interferences: " + (res == null ? 0 : res.Length));
        if (res != null)
            foreach (IInterference it in res)
            {
                var cs = (object[])it.Components;
                string n = "";
                foreach (Component2 c in cs) n += c.Name2 + " / ";
                Console.WriteLine(string.Format("  {0} vol={1:F1} mm3", n, it.Volume * 1e9));
            }
        idm.Done();
        string[] views = { "*Isometric", "*Front", "*Top", "*Right" };
        foreach (var v in views)
        {
            doc.ShowNamedView2(v, -1);
            doc.ViewZoomtofit2();
            int e = 0, w = 0;
            string p = a[0] + "\\view_" + v.Trim('*') + ".png";
            doc.Extension.SaveAs(p, 0, 1, null, ref e, ref w);
        }
        doc.ShowNamedView2("*Isometric", -1);
        doc.ViewZoomtofit2();
    }
}
