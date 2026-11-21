// Exports every Hex3Bot part as a binary STL in its own part coordinates (mm), for the web demo.
using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

class Export
{
    static void Main(string[] a)
    {
        string dir = a[0], outDir = Path.Combine(dir, "demo", "stl");
        Directory.CreateDirectory(outDir);
        var sw = (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
        var tTrans = (int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive;
        var tBin = (int)swUserPreferenceToggle_e.swSTLBinaryFormat;
        var iUnits = (int)swUserPreferenceIntegerValue_e.swExportStlUnits;
        bool oldTrans = sw.GetUserPreferenceToggle(tTrans), oldBin = sw.GetUserPreferenceToggle(tBin);
        int oldUnits = sw.GetUserPreferenceIntegerValue(iUnits);
        sw.SetUserPreferenceToggle(tTrans, true);
        sw.SetUserPreferenceToggle(tBin, true);
        sw.SetUserPreferenceIntegerValue(iUnits, (int)swLengthUnit_e.swMM);
        try
        {
            foreach (var f in Directory.GetFiles(dir, "*.SLDPRT"))
            {
                var m = (ModelDoc2)sw.GetOpenDocumentByName(f);
                if (m == null) { int e0 = 0, w0 = 0; m = sw.OpenDoc6(f, 1, 1, "", ref e0, ref w0); }
                string stl = Path.Combine(outDir, Path.GetFileNameWithoutExtension(f) + ".stl");
                int e = 0, w = 0;
                bool ok = m.Extension.SaveAs(stl, 0, 1, null, ref e, ref w);
                var box = (double[])((PartDoc)m).GetPartBox(true);
                Console.WriteLine(string.Format("{0}: {1} bytes={2}  box x {3:F1}..{4:F1}", Path.GetFileName(stl), ok,
                    File.Exists(stl) ? new FileInfo(stl).Length : 0, box[0] * 1000, box[3] * 1000));
            }
        }
        finally
        {
            sw.SetUserPreferenceToggle(tTrans, oldTrans);
            sw.SetUserPreferenceToggle(tBin, oldBin);
            sw.SetUserPreferenceIntegerValue(iUnits, oldUnits);
        }
    }
}
