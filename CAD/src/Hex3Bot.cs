// Hex3Bot (3-servo hexapod) - builds all parts + assembly in SolidWorks through the API.
// All dimensions in mm (converted to metres for the API).
// Design frame used in this file: X = forward, Y = left, Z = up, ground at Z = 0.
// SolidWorks frame mapping: SW_X = Y, SW_Y = Z (up), SW_Z = X.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

class Hex3Bot
{
    static SldWorks sw;
    static string outDir;
    static string partTpl = @"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\Part.PRTDOT";
    static string asmTpl = @"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\Assembly.ASMDOT";
    const double MM = 0.001;
    static double T30 = Math.Tan(Math.PI / 6);

    // ---------------- key design numbers (mm) ----------------
    const double T = 6.0;            // 6 mm expanded PVC
    const double BaseBot = 49.0, BaseTop = 55.0;
    const double RearX = 32.65, FrontX = 153.3, PivY = 34.15; // leg pivots (link c-c = 120.65)
    const double LegPlateZ = 72.0;   // underside of upper leg plates
    const double SparX = 100.0;      // middle spar rear face
    const double MidPivY = 72.2, MidPivZ = BaseTop + 6.35;
    const double ShortLinkCC = 82.6;

    static ModelDoc2 m;

    static void Main(string[] args)
    {
        outDir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outDir);
        sw = (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
        sw.Visible = true;

        var parts = new Dictionary<string, string>();
        parts["Base"] = Build("Base", BuildBase, Grey());
        parts["Servo"] = Build("Servo_Standard", BuildServo, Black());
        parts["StarHorn"] = Build("Servo_StarHorn", BuildStarHorn, White());
        parts["RoundHorn"] = Build("Servo_RoundHorn", BuildRoundHorn, White());
        parts["UpperRear"] = Build("Leg_Upper_Rear", () => BuildUpperLeg(true), Grey());
        parts["UpperFront"] = Build("Leg_Upper_Front", () => BuildUpperLeg(false), Grey());
        parts["Lower"] = Build("Leg_Lower_FrontRear", BuildLowerLeg, Grey());
        parts["Middle"] = Build("Leg_Middle", BuildMiddleLeg, Grey());
        parts["LongLink"] = Build("Link_Long_Brass", BuildLongLink, Brass());
        parts["ShortLink"] = Build("Link_Short_Brass", BuildShortLink, Brass());
        parts["Spar"] = Build("Middle_Spar", BuildSpar, Grey());
        parts["Spacer"] = Build("Front_Leg_Spacer_Spar", BuildSpacer, Grey());
        parts["Mount"] = Build("Middle_Servo_Mount", BuildMount, Grey());

        BuildAssembly(parts);
        Console.WriteLine("DONE");
    }

    // ---------------- helpers ----------------
    static double[] Grey() { return new double[] { 0.45, 0.47, 0.50 }; }
    static double[] Black() { return new double[] { 0.08, 0.08, 0.08 }; }
    static double[] White() { return new double[] { 0.92, 0.92, 0.90 }; }
    static double[] Brass() { return new double[] { 0.85, 0.68, 0.25 }; }

    static string Build(string name, Action body, double[] rgb)
    {
        m = (ModelDoc2)sw.NewDocument(partTpl, 0, 0, 0);
        if (m == null) throw new Exception("Could not create part from template");
        m.SketchManager.AddToDB = true;
        body();
        m.ClearSelection2(true);
        m.SketchManager.AddToDB = false;
        m.MaterialPropertyValues = new double[] { rgb[0], rgb[1], rgb[2], 1, 1, 0.5, 0.3, 0, 0 };
        m.ForceRebuild3(false);
        m.ShowNamedView2("*Isometric", 7);
        m.ViewZoomtofit2();
        string path = Path.Combine(outDir, name + ".SLDPRT");
        int err = 0, warn = 0;
        bool ok = m.Extension.SaveAs(path, 0, 1, null, ref err, ref warn);
        var box = (double[])((PartDoc)m).GetPartBox(true);
        Console.WriteLine(string.Format("{0}: saved={1} box=({2:F1},{3:F1},{4:F1})..({5:F1},{6:F1},{7:F1}) mm",
            name, ok, box[0] / MM, box[1] / MM, box[2] / MM, box[3] / MM, box[4] / MM, box[5] / MM));
        return path;
    }

    static Feature FrontPlane()
    {
        var f = (Feature)m.FirstFeature();
        while (f != null)
        {
            if (f.GetTypeName2() == "RefPlane") return f; // Front, Top, Right: Front comes first
            f = (Feature)f.GetNextFeature();
        }
        throw new Exception("no ref plane");
    }

    class Sk
    {
        public List<double[]> polys = new List<double[]>(); // flat x,y lists (closed)
        public List<double[]> circles = new List<double[]>(); // x,y,r
        public Sk Poly(params double[] xy) { polys.Add(xy); return this; }
        public Sk Rect(double x0, double y0, double x1, double y1) { return Poly(x0, y0, x1, y0, x1, y1, x0, y1); }
        public Sk Hole(double x, double y, double d) { circles.Add(new double[] { x, y, d / 2 }); return this; }
    }

    // Sketch on the Front plane (local XY) and extrude between local z0 and z1.
    static void Extrude(Sk s, double z0, double z1)
    {
        m.ClearSelection2(true);
        FrontPlane().Select2(false, 0);
        m.SketchManager.InsertSketch(true);
        var sm = m.SketchManager;
        foreach (var p in s.polys)
        {
            int n = p.Length / 2;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                sm.CreateLine(p[2 * i] * MM, p[2 * i + 1] * MM, 0, p[2 * j] * MM, p[2 * j + 1] * MM, 0);
            }
        }
        foreach (var c in s.circles) sm.CreateCircleByRadius(c[0] * MM, c[1] * MM, 0, c[2] * MM);

        var fm = m.FeatureManager;
        Feature feat;
        if (z0 == 0)
            feat = fm.FeatureExtrusion2(true, false, false, 0, 0, z1 * MM, 0, false, false, false, false, 0, 0, false, false, false, false, true, true, true, 0, 0, false);
        else if (z0 < 0 && z1 > 0)
            feat = fm.FeatureExtrusion2(false, false, false, 0, 0, z1 * MM, -z0 * MM, false, false, false, false, 0, 0, false, false, false, false, true, true, true, 0, 0, false);
        else if (z0 > 0)
            feat = fm.FeatureExtrusion2(true, false, false, 0, 0, (z1 - z0) * MM, 0, false, false, false, false, 0, 0, false, false, false, false, true, true, true, 3, z0 * MM, false);
        else // z0 < 0, z1 <= 0 : start plane offset below sketch, extrude upward
            feat = fm.FeatureExtrusion2(true, false, false, 0, 0, (z1 - z0) * MM, 0, false, false, false, false, 0, 0, false, false, false, false, true, true, true, 3, -z0 * MM, true);
        if (feat == null) throw new Exception(string.Format("extrude failed z0={0} z1={1}", z0, z1));
        m.ClearSelection2(true);
    }

    // ---------------- parts ----------------
    static void BuildBase()
    {
        // 7" x 3-1/2" plate, notches for the two leg servos at the rear, chamfered front corners
        var s = new Sk().Poly(
            0, -44.45, 22.2, -44.45, 22.2, -23.85, 63.5, -23.85, 63.5, -44.45,
            167.8, -44.45, 177.8, -34.45, 177.8, 34.45, 167.8, 44.45,
            63.5, 44.45, 63.5, 23.85, 22.2, 23.85, 22.2, 44.45, 0, 44.45);
        foreach (double sy in new double[] { 1, -1 })
        {
            foreach (double x in new double[] { RearX - 13.8, RearX + 34.2 })      // servo ear screws
            { s.Hole(x, sy * (PivY - 5), 3.2); s.Hole(x, sy * (PivY + 5), 3.2); }
            s.Hole(FrontX, sy * PivY, 3.6);                                          // front leg pivot (6-32)
            s.Hole(SparX + 9, sy * 25.4, 3.2);                                       // spar L-bracket
        }
        s.Hole(86, -12, 3.2).Hole(86, 32, 3.2);                                      // servo-mount L-brackets
        s.Hole(80, 0, 12.7).Hole(130, 0, 12.7);                                      // wire pass-through
        Extrude(s, 0, T);
    }

    static void BuildServo()
    {
        // generic standard servo; origin = output shaft axis at the underside of the mounting ears
        Extrude(new Sk().Rect(-10, -9.9, 30.4, 9.9), -26, 10);
        var ears = new Sk().Rect(-17.1, -9.9, -10, 9.9);
        Extrude(ears.Hole(-13.8, 5, 4.2).Hole(-13.8, -5, 4.2), 0, 2.5);
        Extrude(new Sk().Rect(30.4, -9.9, 37.5, 9.9).Hole(34.2, 5, 4.2).Hole(34.2, -5, 4.2), 0, 2.5);
        Extrude(new Sk().Hole(0, 0, 12), 10, 12);
        Extrude(new Sk().Hole(0, 0, 6), 12, 14);
    }

    static void BuildStarHorn()
    {
        var pts = new List<double>();
        for (int i = 0; i < 12; i++)
        {
            double a = i * Math.PI / 6, r = (i % 2 == 0) ? 12.5 : 5.5;
            pts.Add(r * Math.Cos(a)); pts.Add(r * Math.Sin(a));
        }
        Extrude(new Sk().Poly(pts.ToArray()).Hole(0, 0, 3), 0, 3);
    }

    static void BuildRoundHorn()
    {
        // large round horn; drilled at 5 and 7 o'clock for the short links
        var s = new Sk().Hole(0, 0, 25.4); // outer disc drawn as a circle
        s.Hole(0, 0, 3).Hole(5, -8.66, 2.4).Hole(-5, -8.66, 2.4);
        Extrude(s, 0, 3);
    }

    static void BuildUpperLeg(bool rear)
    {
        // 2-7/8" x 1-7/16" chamfered plate; local +X points outward, origin = pivot
        var s = new Sk().Poly(-19.05, -11.1, -6.35, -18.25, 41.3, -18.25, 53.975, -6.35,
                              53.975, 6.35, 41.3, 18.25, -6.35, 18.25, -19.05, 11.1);
        s.Hole(19.05, 0, 3.2);                    // long-link hole
        s.Hole(28.6, 0, 3.2).Hole(49.2, 0, 3.2);  // #4 screws into lower leg edge
        if (rear) s.Hole(0, 0, 6.35).Hole(0, 11.1, 3.2).Hole(0, -11.1, 3.2); // servo star hub
        else s.Hole(0, 0, 3.6);                    // 6-32 pivot screw
        Extrude(s, 0, T);
    }

    static double LowerH() { return LegPlateZ; } // vertical height of lower leg (foot on ground)

    static void BuildLowerLeg()
    {
        // parallelogram, ends cut at 30 deg; local X outward, Y up, origin at pivot under plate
        double h = LowerH(), dx = h * T30, a = 22.9, w = 34.9;
        Extrude(new Sk().Poly(a, 0, a + w, 0, a + w + dx, -h, a + dx, -h), -T / 2, T / 2);
    }

    static double MidTheta() { return Math.Acos(MidPivZ / (84.1 - 15.9)); }

    static void BuildMiddleLeg()
    {
        // local X = outward, Y = up, origin = pivot hole on the spar
        double th = MidTheta(), sn = Math.Sin(th), c = Math.Cos(th);
        double dX = sn, dY = -c, nX = c, nY = sn;
        double toX = -15.9 * dX + 15.1 * nX, toY = -15.9 * dY + 15.1 * nY;
        double tiX = -15.9 * dX - 15.1 * nX, tiY = -15.9 * dY - 15.1 * nY;
        double t = (toY + MidPivZ) / c;
        double foX = toX + t * dX, foY = -MidPivZ;
        double fiX = foX - 16.0, fiY = foY;
        var s = new Sk().Poly(tiX, tiY, toX, toY, foX, foY, fiX, fiY);
        s.Hole(0, 0, 3.2).Hole(22.2 * dX, 22.2 * dY, 3.2);
        Extrude(s, 0, T);
    }

    static void BuildLongLink()
    {
        Extrude(new Sk().Rect(-9.525, -6.35, 130.175, 6.35).Hole(0, 0, 3.2).Hole(120.65, 0, 3.2), 0, 0.5);
    }

    static void BuildShortLink()
    {
        Extrude(new Sk().Rect(-4.75, -6.35, ShortLinkCC + 4.75, 6.35).Hole(0, 0, 3.2).Hole(ShortLinkCC, 0, 3.2), 0, 0.5);
    }

    static void BuildSpar()
    {
        var s = new Sk().Rect(-80.17, 0, 80.17, 12.7);
        s.Hole(-MidPivY, 6.35, 3.2).Hole(MidPivY, 6.35, 3.2).Hole(-25.4, 6.35, 3.2).Hole(25.4, 6.35, 3.2);
        Extrude(s, 0, T);
    }

    static void BuildSpacer()
    {
        Extrude(new Sk().Rect(-6.35, -44.45, 6.35, 44.45).Hole(0, PivY, 3.6).Hole(0, -PivY, 3.6), 0, T);
    }

    static double HornZ()
    {
        double th = MidTheta();
        double h2y = MidPivY + 22.2 * Math.Sin(th), h2z = MidPivZ - 22.2 * Math.Cos(th);
        double dy = h2y - 5.0;
        double holeZ = h2z - Math.Sqrt(ShortLinkCC * ShortLinkCC - dy * dy);
        return holeZ + 8.66;
    }

    static void BuildMount()
    {
        // vertical plate under the base; local X = world Y, local Y = world Z
        double zc = HornZ();
        var s = new Sk().Rect(-22, Math.Max(zc - 15, 3), 42, BaseBot);
        s.Poly(-10.3, zc - 10.3, 30.7, zc - 10.3, 30.7, zc + 10.3, -10.3, zc + 10.3); // servo pocket
        s.Hole(-13.8, zc - 5, 3.2).Hole(-13.8, zc + 5, 3.2).Hole(34.2, zc - 5, 3.2).Hole(34.2, zc + 5, 3.2);
        Extrude(s, 0, T);
    }

    // ---------------- assembly ----------------
    static double[] V(double x, double y, double z) { return new double[] { x, y, z }; }
    static double[] Neg(double[] a) { return V(-a[0], -a[1], -a[2]); }
    static double[] Cross(double[] a, double[] b)
    {
        return V(a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]);
    }
    static double[] Sw(double[] d) { return V(d[1], d[2], d[0]); } // design -> SW frame
    static readonly double[] X = { 1, 0, 0 }, Y = { 0, 1, 0 }, Z = { 0, 0, 1 };

    static AssemblyDoc asm;
    static MathUtility mu;

    static void Place(string path, double[] origin, double[] ex, double[] ey, string label)
    {
        var ez = Cross(ex, ey);
        var comp = (Component2)asm.AddComponent5(path, 0, "", false, "", 0, 0, 0);
        if (comp == null) throw new Exception("AddComponent failed: " + path);
        double[] a = Sw(ex), b = Sw(ey), c = Sw(ez), o = Sw(origin);
        var arr = new double[] { a[0], a[1], a[2], b[0], b[1], b[2], c[0], c[1], c[2],
                                 o[0] * MM, o[1] * MM, o[2] * MM, 1, 0, 0, 0 };
        comp.Transform2 = (MathTransform)mu.CreateTransform(arr);
        Console.WriteLine("  placed " + label + " -> " + comp.Name2);
    }

    static void BuildAssembly(Dictionary<string, string> p)
    {
        var doc = (ModelDoc2)sw.NewDocument(asmTpl, 0, 0, 0);
        asm = (AssemblyDoc)doc;
        mu = (MathUtility)sw.GetMathUtility();
        double zc = HornZ();
        Console.WriteLine(string.Format("Middle-servo horn centre height = {0:F2} mm, middle leg angle = {1:F1} deg", zc, MidTheta() * 180 / Math.PI));

        Place(p["Base"], V(0, 0, BaseBot), X, Y, "base");
        Place(p["Spacer"], V(FrontX, 0, BaseTop), X, Y, "front spacer 1");
        Place(p["Spacer"], V(FrontX, 0, BaseTop + T), X, Y, "front spacer 2");
        Place(p["Spar"], V(SparX, 0, BaseTop), Y, Z, "middle spar");
        Place(p["Mount"], V(SparX - 9.5, 0, 0), Y, Z, "middle servo mount");
        Place(p["Servo"], V(SparX - 3.5, 0, zc), Y, Z, "middle servo");
        Place(p["RoundHorn"], V(SparX + 10.5, 0, zc), Y, Z, "round horn");

        foreach (int side in new int[] { -1, 1 }) // -1 = right, +1 = left
        {
            double[] outv = side < 0 ? Neg(Y) : Y;
            double[] along = side < 0 ? X : Neg(X);          // ey so that ez = +Z
            string sname = side < 0 ? "R" : "L";
            // rear leg (servo driven) and front leg (idler), joined by the long brass link
            Place(p["Servo"], V(RearX, side * PivY, BaseTop), X, Y, sname + " leg servo");
            Place(p["StarHorn"], V(RearX, side * PivY, BaseTop + 14), X, Y, sname + " star horn");
            Place(p["UpperRear"], V(RearX, side * PivY, LegPlateZ), outv, along, sname + " rear upper leg");
            Place(p["UpperFront"], V(FrontX, side * PivY, LegPlateZ), outv, along, sname + " front upper leg");
            Place(p["Lower"], V(RearX, side * PivY, LegPlateZ), outv, Z, sname + " rear lower leg");
            Place(p["Lower"], V(FrontX, side * PivY, LegPlateZ), outv, Z, sname + " front lower leg");
            Place(p["LongLink"], V(RearX, side * (PivY + 19.05), LegPlateZ + T + 6.35), X, Y, sname + " long link");
            // middle leg on the spar, driven by the short link from the round horn
            double legX = side < 0 ? SparX + 13.5 : SparX + 7.5;
            Place(p["Middle"], V(legX, side * MidPivY, MidPivZ), outv, Z, sname + " middle leg");
            double th = MidTheta();
            double h2y = side * (MidPivY + 22.2 * Math.Sin(th)), h2z = MidPivZ - 22.2 * Math.Cos(th);
            double hy = side * 5.0, hz = zc - 8.66;
            double dy = h2y - hy, dz = h2z - hz, L = Math.Sqrt(dy * dy + dz * dz);
            double[] ex = V(0, dy / L, dz / L), ey = V(0, -dz / L, dy / L);
            // the two links overlap at the horn, so the left one sits one washer (0.6 mm) further forward
            Place(p["ShortLink"], V(SparX + 13.5 + (side > 0 ? 0.6 : 0), hy, hz), ex, ey, sname + string.Format(" short link (c-c check {0:F2})", L));
        }

        // fix everything (positioned, no mates)
        doc.ClearSelection2(true);
        var comps = (object[])asm.GetComponents(true);
        foreach (Component2 cp in comps) cp.Select4(true, null, false);
        asm.FixComponent();
        doc.ClearSelection2(true);
        doc.ForceRebuild3(false);
        doc.ShowNamedView2("*Isometric", 7);
        doc.ViewZoomtofit2();
        int err = 0, warn = 0;
        string path = Path.Combine(outDir, "Hex3Bot_Assembly.SLDASM");
        bool ok = doc.Extension.SaveAs(path, 0, 1, null, ref err, ref warn);
        Console.WriteLine("Assembly saved=" + ok + " -> " + path + " err=" + err);
    }
}
