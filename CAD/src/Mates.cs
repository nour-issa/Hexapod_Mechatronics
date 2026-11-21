// Adds kinematic mates to the Hex3Bot assembly (run after Hex3Bot.exe with the assembly open).
// Step 1: named reference axes/planes in each part.  Step 2: pin + plane mates in the assembly.
// Step 3: limit-angle mates for the three servos (range of motion).  Step 4: drive test.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

class Mates
{
    static SldWorks sw;
    static ModelDoc2 m;
    static string dir;
    const double MM = 0.001;

    static void Main(string[] args)
    {
        dir = args[0];
        string mode = args.Length > 1 ? args[1] : "all";
        sw = (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
        if (mode == "all" || mode == "refs") AddRefs();
        if (mode == "all" || mode == "mates") AddMates();
        if (mode == "test") Test();
        if (mode == "reset")
        {
            OpenAsm();
            Drive("Leg_Upper_Rear-1", 1, "Base-1", 1, 90);
            Drive("Leg_Upper_Rear-2", 1, "Base-1", 1, 90);
            Drive("Servo_RoundHorn-1", 2, "Base-1", 0, 90);
            adoc.ShowNamedView2("*Isometric", -1); adoc.ViewZoomtofit2();
            int e = 0, w = 0;
            Console.WriteLine("saved=" + adoc.Extension.SaveAs(adoc.GetPathName(), 0, 1, null, ref e, ref w));
        }
    }

    // ------------------------------------------------ part reference geometry
    static Feature DefPlane(int idx) // 0 Front, 1 Top, 2 Right
    {
        var f = (Feature)m.FirstFeature(); int n = 0;
        while (f != null)
        {
            if (f.GetTypeName2() == "RefPlane") { if (n == idx) return f; n++; }
            f = (Feature)f.GetNextFeature();
        }
        throw new Exception("plane");
    }

    static Feature Last() { return (Feature)m.FeatureByPositionReverse(0); }

    static Feature OffsetPlane(int baseIdx, double d, string name, bool hide)
    {
        m.ClearSelection2(true);
        DefPlane(baseIdx).Select2(false, 0);
        int c = 8 | (d < 0 ? 256 : 0); // distance (+ flip)
        m.FeatureManager.InsertRefPlane(c, Math.Abs(d) * MM, 0, 0, 0, 0);
        var f = Last(); f.Name = name;
        if (hide) { m.ClearSelection2(true); f.Select2(false, 0); m.BlankRefGeom(); }
        m.ClearSelection2(true);
        return f;
    }

    static void Axis(string name, double x, double y)
    {
        Feature px = x == 0 ? DefPlane(2) : OffsetPlane(2, x, name + "_px", true);
        Feature py = y == 0 ? DefPlane(1) : OffsetPlane(1, y, name + "_py", true);
        m.ClearSelection2(true);
        px.Select2(false, 0); py.Select2(true, 0);
        if (!m.InsertAxis2(true)) throw new Exception("axis " + name);
        var f = Last(); f.Name = name;
        var p = (double[])((RefAxis)f.GetSpecificFeature2()).GetRefAxisParams();
        Console.WriteLine(string.Format("    axis {0}: ({1:F2},{2:F2}) wanted ({3:F2},{4:F2})", name, p[0] / MM, p[1] / MM, x, y));
        if (Math.Abs(p[0] / MM - x) > 0.01 || Math.Abs(p[1] / MM - y) > 0.01) throw new Exception("axis misplaced " + name);
        m.ClearSelection2(true);
    }

    static void Part(string file, Action a)
    {
        string path = Path.Combine(dir, file + ".SLDPRT");
        m = (ModelDoc2)sw.GetOpenDocumentByName(path);
        if (m == null) { int e = 0, w = 0; m = sw.OpenDoc6(path, 1, 1, "", ref e, ref w); }
        int er = 0; sw.ActivateDoc3(m.GetTitle(), false, 0, ref er);
        Console.WriteLine("  " + file);
        m.SketchManager.AddToDB = true;
        a();
        m.SketchManager.AddToDB = false;
        m.ForceRebuild3(false);
        m.Save3(1, ref er, ref er);
    }

    static void AddRefs()
    {
        double th = Math.Acos((55.0 + 6.35) / (84.1 - 15.9));
        Part("Base", () => {
            OffsetPlane(0, 23.0, "LegPlane", false);    // underside of upper leg plates
            OffsetPlane(0, 35.35, "LinkPlane", false);  // underside of long links
            Axis("FrontPivot_R", 153.3, -34.15); Axis("FrontPivot_L", 153.3, 34.15);
        });
        Part("Servo_Standard", () => { Axis("Shaft", 0, 0); OffsetPlane(0, 14, "SplineTop", false); });
        foreach (var n in new[] { "Leg_Upper_Rear", "Leg_Upper_Front" })
            Part(n, () => { Axis("Pivot", 0, 0); Axis("LinkPin", 19.05, 0); });
        Part("Link_Long_Brass", () => { Axis("PinRear", 0, 0); Axis("PinFront", 120.65, 0); });
        Part("Middle_Spar", () => {
            Axis("Piv_R", -72.2, 6.35); Axis("Piv_L", 72.2, 6.35);
            OffsetPlane(0, 13.5, "Mid_R", false); OffsetPlane(0, 7.5, "Mid_L", false);
        });
        Part("Leg_Middle", () => { Axis("Pivot", 0, 0); Axis("LinkPin", 22.2 * Math.Sin(th), -22.2 * Math.Cos(th)); });
        Part("Servo_RoundHorn", () => {
            Axis("Hub", 0, 0); Axis("Hole_R", -5, -8.66); Axis("Hole_L", 5, -8.66);
            OffsetPlane(0, 3.0, "Link_R", false); OffsetPlane(0, 3.6, "Link_L", false);
        });
        Part("Link_Short_Brass", () => { Axis("PinHorn", 0, 0); Axis("PinLeg", 82.6, 0); });
    }

    // ------------------------------------------------ assembly mates
    static AssemblyDoc asm;
    static ModelDoc2 adoc;

    static Component2 C(string n)
    {
        var c = (Component2)asm.GetComponentByName(n);
        if (c == null) throw new Exception("no component " + n);
        return c;
    }

    static Feature F(Component2 c, string name)
    {
        if (name.StartsWith("#"))
        {
            int idx = int.Parse(name.Substring(1)), k = 0;
            var f = (Feature)c.FirstFeature();
            while (f != null)
            {
                if (f.GetTypeName2() == "RefPlane") { if (k == idx) return f; k++; }
                f = (Feature)f.GetNextFeature();
            }
        }
        var r = (Feature)c.FeatureByName(name);
        if (r == null) throw new Exception("no feature " + name + " in " + c.Name2);
        return r;
    }

    static int nMates = 0;
    static Feature Mate(int type, string c1, string e1, string c2, string e2, string label,
                        double angle = 0, double up = 0, double lo = 0)
    {
        adoc.ClearSelection2(true);
        if (type == 16)
        {
            C(c1).Select4(false, null, false); C(c2).Select4(true, null, false);
        }
        else
        {
            F(C(c1), e1).Select2(false, 1); F(C(c2), e2).Select2(true, 1);
        }
        int err = 0;
        var mate = asm.AddMate5(type, 2 /*closest*/, false, 0, 0, 0, 1, 1, angle, up, lo, false, false, 0, out err);
        adoc.ClearSelection2(true);
        if (mate == null) { Console.WriteLine("  !! FAILED " + label + " err=" + err); return null; }
        var mf = (Feature)mate; mf.Name = label; nMates++;
        Console.WriteLine("  ok " + label + (err != 1 ? " (status " + err + ")" : ""));
        return mf;
    }

    const int COINC = 0, LOCK = 16, ANGLE = 6;

    static Dictionary<string, double[]> Snap()
    {
        var d = new Dictionary<string, double[]>();
        foreach (Component2 c in (object[])asm.GetComponents(true)) d[c.Name2] = (double[])c.Transform2.ArrayData;
        return d;
    }

    static void OpenAsm()
    {
        string path = Path.Combine(dir, "Hex3Bot_Assembly.SLDASM");
        adoc = (ModelDoc2)sw.GetOpenDocumentByName(path);
        int er = 0; sw.ActivateDoc3(adoc.GetTitle(), false, 0, ref er);
        asm = (AssemblyDoc)adoc;
    }

    static void AddMates()
    {
        OpenAsm();
        adoc.ForceRebuild3(false);
        var before = Snap();

        // floating (moving) parts
        string[] moving = { "Leg_Upper_Rear-1", "Leg_Upper_Rear-2", "Leg_Upper_Front-1", "Leg_Upper_Front-2",
            "Leg_Lower_FrontRear-1", "Leg_Lower_FrontRear-2", "Leg_Lower_FrontRear-3", "Leg_Lower_FrontRear-4",
            "Servo_StarHorn-1", "Servo_StarHorn-2", "Link_Long_Brass-1", "Link_Long_Brass-2",
            "Leg_Middle-1", "Leg_Middle-2", "Servo_RoundHorn-1", "Link_Short_Brass-1", "Link_Short_Brass-2" };
        adoc.ClearSelection2(true);
        foreach (var n in moving) C(n).Select4(true, null, false);
        asm.UnfixComponent();
        adoc.ClearSelection2(true);

        string B = "Base-1";
        string[] S = { "R", "L" };
        for (int i = 0; i < 2; i++)
        {
            string s = S[i], k = (i + 1).ToString();
            string servo = "Servo_Standard-" + (i + 2), ur = "Leg_Upper_Rear-" + k, uf = "Leg_Upper_Front-" + k;
            // rear leg: revolute on the servo shaft
            Mate(COINC, ur, "Pivot", servo, "Shaft", "Pin_RearLeg_" + s);
            Mate(COINC, ur, "#0", B, "LegPlane", "Plane_RearLeg_" + s);
            // front leg: revolute on the 6-32 pivot screw
            Mate(COINC, uf, "Pivot", B, "FrontPivot_" + s, "Pin_FrontLeg_" + s);
            Mate(COINC, uf, "#0", B, "LegPlane", "Plane_FrontLeg_" + s);
            // rigid joints (screwed together)
            Mate(LOCK, "Leg_Lower_FrontRear-" + (2 * i + 1), null, ur, null, "Lock_RearLower_" + s);
            Mate(LOCK, "Leg_Lower_FrontRear-" + (2 * i + 2), null, uf, null, "Lock_FrontLower_" + s);
            Mate(LOCK, "Servo_StarHorn-" + k, null, ur, null, "Lock_StarHorn_" + s);
            // long link closes the parallelogram
            string ll = "Link_Long_Brass-" + k;
            Mate(COINC, ll, "PinRear", ur, "LinkPin", "Pin_LongLink_Rear_" + s);
            Mate(COINC, ll, "#0", B, "LinkPlane", "Plane_LongLink_" + s);
            Mate(COINC, ll, "PinFront", uf, "LinkPin", "Pin_LongLink_Front_" + s);
        }
        // middle-leg rocker
        Mate(COINC, "Servo_RoundHorn-1", "Hub", "Servo_Standard-1", "Shaft", "Pin_RoundHorn");
        Mate(COINC, "Servo_RoundHorn-1", "#0", "Servo_Standard-1", "SplineTop", "Plane_RoundHorn");
        for (int i = 0; i < 2; i++)
        {
            string s = S[i], k = (i + 1).ToString(), ml = "Leg_Middle-" + k, sl = "Link_Short_Brass-" + k;
            Mate(COINC, ml, "Pivot", "Middle_Spar-1", "Piv_" + s, "Pin_MiddleLeg_" + s);
            Mate(COINC, ml, "#0", "Middle_Spar-1", "Mid_" + s, "Plane_MiddleLeg_" + s);
            Mate(COINC, sl, "PinHorn", "Servo_RoundHorn-1", "Hole_" + s, "Pin_ShortLink_Horn_" + s);
            Mate(COINC, sl, "#0", "Servo_RoundHorn-1", "Link_" + s, "Plane_ShortLink_" + s);
            Mate(COINC, sl, "PinLeg", ml, "LinkPin", "Pin_ShortLink_Leg_" + s);
        }

        // servo travel limits (limit-angle mates). neutral = 90 deg between the planes.
        double D = Math.PI / 180;
        for (int i = 0; i < 2; i++)
            Mate(ANGLE, "Leg_Upper_Rear-" + (i + 1), "#1", B, "#1", "Limit_LegSweep_" + S[i] + " (+/-30deg)", 90 * D, 120 * D, 60 * D);
        Mate(ANGLE, "Servo_RoundHorn-1", "#2", B, "#0", "Limit_Rocker (+/-60deg)", 90 * D, 150 * D, 30 * D);

        adoc.ForceRebuild3(false);
        var after = Snap();
        double maxd = 0; string worst = "";
        foreach (var kv in before)
        {
            var a = after[kv.Key];
            for (int j = 0; j < 12; j++) { double dd = Math.Abs(a[j] - kv.Value[j]); if (dd > maxd) { maxd = dd; worst = kv.Key; } }
        }
        Console.WriteLine(string.Format("Mates added: {0}. Max change of any component transform after solve: {1:E2} ({2})", nMates, maxd, worst));
        int e = 0, w = 0;
        Console.WriteLine("saved=" + adoc.Extension.SaveAs(adoc.GetPathName(), 0, 1, null, ref e, ref w));
    }

    // ------------------------------------------------ drive test (temporary angle mates, then deleted)
    static double[] World(string comp, double x, double y, double z) // returns design-frame mm (X fwd, Y left, Z up)
    {
        var a = (double[])C(comp).Transform2.ArrayData;
        x *= MM; y *= MM; z *= MM;
        double sx = x * a[0] + y * a[3] + z * a[6] + a[9];
        double sy = x * a[1] + y * a[4] + z * a[7] + a[10];
        double sz = x * a[2] + y * a[5] + z * a[8] + a[11];
        return new double[] { sz / MM, sx / MM, sy / MM };
    }

    static string P(double[] p) { return string.Format("({0,7:F1},{1,7:F1},{2,7:F1})", p[0], p[1], p[2]); }

    static void Report(string title)
    {
        Console.WriteLine("  " + title);
        // feet: lower leg foot corner (local 64.47,-72,0); middle leg foot from the part geometry (approx local point on foot)
        Console.WriteLine("    R rear foot " + P(World("Leg_Lower_FrontRear-1", 82, -72, 0)) + "   R front foot " + P(World("Leg_Lower_FrontRear-2", 82, -72, 0)));
        Console.WriteLine("    L rear foot " + P(World("Leg_Lower_FrontRear-3", 82, -72, 0)) + "   L front foot " + P(World("Leg_Lower_FrontRear-4", 82, -72, 0)));
        double th = Math.Acos(61.35 / 68.2);
        double fx = 68.2 * Math.Sin(th), fy = -68.2 * Math.Cos(th);
        Console.WriteLine("    R middle foot " + P(World("Leg_Middle-1", fx, fy, 3)) + "   L middle foot " + P(World("Leg_Middle-2", fx, fy, 3)));
    }

    static void Drive(string comp, int plane, string other, int oplane, double deg)
    {
        adoc.ClearSelection2(true);
        F(C(comp), "#" + plane).Select2(false, 1); F(C(other), "#" + oplane).Select2(true, 1);
        int err;
        var mt = (Feature)asm.AddMate5(ANGLE, 2, false, 0, 0, 0, 1, 1, deg * Math.PI / 180, 0, 0, false, false, 0, out err);
        adoc.ForceRebuild3(false);
        Report(string.Format("{0} driven to {1} deg (mate {2}, status {3})", comp, deg, mt != null, err));
        if (mt != null) { adoc.ClearSelection2(true); mt.Select2(false, 0); adoc.EditDelete(); }
        adoc.ForceRebuild3(false);
    }

    static void Test()
    {
        OpenAsm();
        // suppress the limit mates while driving to exact values
        Report("neutral");
        Drive("Leg_Upper_Rear-1", 1, "Base-1", 1, 115);   // right legs swept 25 deg
        Drive("Servo_RoundHorn-1", 2, "Base-1", 0, 135);  // rocker +45 deg
        Drive("Servo_RoundHorn-1", 2, "Base-1", 0, 45);   // rocker -45 deg
        Report("after test (should be back near neutral if nothing else drives it)");
    }
}
