// Hex3Bot walking-gait demo.
//   Demo.exe <CAD folder> live     -> animates the gait in the open SolidWorks window (3 cycles)
//   Demo.exe <CAD folder> record   -> also saves frames + writes demo/Hex3Bot_Demo.html
// The assembly Hex3Bot_Assembly.SLDASM must be open. Temporary "Drive_" angle mates are added
// to act as the three servos, then deleted at the end so the model is left in neutral.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

class Demo
{
    static SldWorks sw;
    static ModelDoc2 doc;
    static AssemblyDoc asm;
    const double D = Math.PI / 180;

    class Key { public double rock, r, l; public string text; public Key(double a, double b, double c, string t) { rock = a; r = b; l = c; text = t; } }

    static void Main(string[] args)
    {
        string dir = args[0];
        bool record = args.Length > 1 && args[1] == "record";
        sw = (SldWorks)Marshal.GetActiveObject("SldWorks.Application");
        doc = (ModelDoc2)sw.GetOpenDocumentByName(Path.Combine(dir, "Hex3Bot_Assembly.SLDASM"));
        if (doc == null) { Console.WriteLine("Open Hex3Bot_Assembly.SLDASM in SolidWorks first."); return; }
        int er = 0; sw.ActivateDoc3(doc.GetTitle(), false, 0, ref er);
        asm = (AssemblyDoc)doc;

        // hide reference geometry so the pictures are clean
        doc.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayPlanes, false);
        doc.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayAxes, false);
        doc.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayOrigins, false);
        doc.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayTemporaryAxes, false);

        // the three "servos": angle between planes, 90 deg = neutral
        var dR = Drive("Leg_Upper_Rear-1", 1, "Base-1", 1, "Drive_RightLegs");
        var dL = Drive("Leg_Upper_Rear-2", 1, "Base-1", 1, "Drive_LeftLegs");
        var dM = Drive("Servo_RoundHorn-1", 2, "Base-1", 0, "Drive_MiddleRocker");

        // which sign sweeps each side's feet backward (-X)?
        double sR = BackSign(dR, "Leg_Lower_FrontRear-1");
        double sL = BackSign(dL, "Leg_Lower_FrontRear-3");
        // which rocker sign pushes the RIGHT middle foot down?
        Set(dM, 90 + 45); doc.EditRebuild3();
        double zr = FootZ("Leg_Middle-1");
        double sM = zr < 0 ? 1 : -1;
        Set(dM, 90); doc.EditRebuild3();
        Console.WriteLine(string.Format("signs: right {0}, left {1}, rocker {2}", sR, sL, sM));

        // gait keyframes: rocker (+ = right middle foot down => right side of body lifted),
        // leg sweep (+ = feet pushed backward = power stroke when on the ground)
        const double S = 25, R = 45;
        var keys = new List<Key> {
            new Key(0, 0, 0, "Neutral: all six feet on the ground."),
            new Key(+R, 0, 0, "1. Middle servo turns: the RIGHT middle leg pushes down, the body rolls left. Right front/rear feet lift off. Tripod on the ground = right middle + left front + left rear."),
            new Key(+R, 0, +S, "2. POWER stroke: the left servo sweeps the left legs backward. They are on the ground, so they push the robot forward."),
            new Key(+R, -S, +S, "3. Return stroke: the right legs are in the air, so the right servo swings them forward without moving the robot."),
            new Key(-R, -S, +S, "4. Middle servo turns the other way: the LEFT middle leg pushes down, the body rolls right. Left front/rear feet lift off. Tripod = left middle + right front + right rear."),
            new Key(-R, +S, +S, "5. POWER stroke: the right legs (on the ground) sweep backward and push the robot forward."),
            new Key(-R, +S, -S, "6. Return stroke: the lifted left legs swing forward, ready for the next power stroke."),
            new Key(+R, +S, -S, "7. Rock back onto the RIGHT middle leg: left front/rear touch down, right front/rear lift."),
            new Key(+R, +S, +S, "8. POWER stroke: the left legs sweep backward again - the robot moves forward."),
            new Key(+R, -S, +S, "9. Return stroke: the right legs swing forward in the air. The cycle repeats from step 4."),
        };
        // play order: 0..9 once, then (live mode) loop 4..9 again
        var order = new List<int>();
        for (int k = 0; k < keys.Count; k++) order.Add(k);
        if (!record) for (int c = 0; c < 2; c++) for (int k = 4; k < keys.Count; k++) order.Add(k);

        int steps = 12;
        string outDir = Path.Combine(dir, "demo");
        var frames = new List<string[]>(); // file, caption
        if (record)
        {
            Directory.CreateDirectory(outDir);
            foreach (var f in Directory.GetFiles(outDir, "*.jpg")) File.Delete(f);
        }
        string[] views = record ? new[] { "iso", "front" } : new[] { "iso" };
        foreach (var view in views)
        {
            if (view == "iso") { doc.ShowNamedView2("*Isometric", -1); }
            else { doc.ShowNamedView2("*Front", -1); }
            doc.ViewZoomtofit2();
            int n = 0;
            for (int j = 1; j < order.Count; j++)
                {
                    var a = keys[order[j - 1]]; var b = keys[order[j]];
                    Console.WriteLine(b.text);
                    for (int i = 1; i <= steps; i++)
                    {
                        double t = (double)i / steps; t = t * t * (3 - 2 * t); // ease in/out
                        Set(dR, 90 + sR * (a.r + (b.r - a.r) * t));
                        Set(dL, 90 + sL * (a.l + (b.l - a.l) * t));
                        Set(dM, 90 + sM * (a.rock + (b.rock - a.rock) * t));
                        doc.EditRebuild3();
                        doc.GraphicsRedraw2();
                        if (record)
                        {
                            string name = string.Format("{0}_{1:D3}.jpg", view, n++);
                            // SolidWorks' JPG export returned a stale image, so export PNG and convert
                            int e = 0, w = 0;
                            string png = Path.Combine(outDir, "tmp.png");
                            doc.Extension.SaveAs(png, 0, 1, null, ref e, ref w);
                            using (var bmp = new System.Drawing.Bitmap(png))
                                bmp.Save(Path.Combine(outDir, name), System.Drawing.Imaging.ImageFormat.Jpeg);
                            File.Delete(png);
                            if (view == "iso") frames.Add(new[] { name.Substring(4), b.text });
                        }
                        else Thread.Sleep(30);
                    }
                    if (!record) Thread.Sleep(500);
                }
        }

        // back to neutral and remove the temporary drive mates
        Set(dR, 90); Set(dL, 90); Set(dM, 90); doc.EditRebuild3();
        foreach (var f in new[] { dR, dL, dM }) { doc.ClearSelection2(true); f.Select2(false, 0); doc.EditDelete(); }
        doc.ForceRebuild3(false);
        doc.ShowNamedView2("*Isometric", -1); doc.ViewZoomtofit2();
        int e2 = 0, w2 = 0;
        doc.Extension.SaveAs(doc.GetPathName(), 0, 1, null, ref e2, ref w2);

        if (record) WriteHtml(outDir, frames);
        Console.WriteLine("DONE");
    }

    static Feature Drive(string comp, int plane, string other, int oplane, string name)
    {
        doc.ClearSelection2(true);
        Plane(comp, plane).Select2(false, 1); Plane(other, oplane).Select2(true, 1);
        int err;
        var m = (Feature)asm.AddMate5((int)swMateType_e.swMateANGLE, 2, false, 0, 0, 0, 1, 1, 90 * D, 0, 0, false, false, 0, out err);
        if (m == null) throw new Exception("drive mate failed " + name + " err " + err);
        m.Name = name;
        doc.ClearSelection2(true);
        return m;
    }

    static Feature Plane(string comp, int idx)
    {
        var c = (Component2)asm.GetComponentByName(comp);
        var f = (Feature)c.FirstFeature(); int k = 0;
        while (f != null)
        {
            if (f.GetTypeName2() == "RefPlane") { if (k == idx) return f; k++; }
            f = (Feature)f.GetNextFeature();
        }
        throw new Exception("plane");
    }

    static void Set(Feature mate, double deg)
    {
        var dd = (DisplayDimension)mate.GetFirstDisplayDimension();
        var dim = (Dimension)dd.GetDimension2(0);
        dim.SetSystemValue3(deg * D, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);
    }

    static double[] World(string comp, double x, double y, double z)
    {
        var a = (double[])((Component2)asm.GetComponentByName(comp)).Transform2.ArrayData;
        x *= 0.001; y *= 0.001; z *= 0.001;
        return new[] { (x * a[2] + y * a[5] + z * a[8] + a[11]) * 1000,   // design X
                       (x * a[0] + y * a[3] + z * a[6] + a[9]) * 1000,    // design Y
                       (x * a[1] + y * a[4] + z * a[7] + a[10]) * 1000 }; // design Z
    }

    static double FootZ(string midLeg)
    {
        double th = Math.Acos(61.35 / 68.2);
        return World(midLeg, 68.2 * Math.Sin(th), -68.2 * Math.Cos(th), 3)[2];
    }

    static double BackSign(Feature drive, string lowerLeg)
    {
        double x0 = World(lowerLeg, 82, -72, 0)[0];
        Set(drive, 100); doc.EditRebuild3();
        double x1 = World(lowerLeg, 82, -72, 0)[0];
        Set(drive, 90); doc.EditRebuild3();
        return x1 < x0 ? 1 : -1;
    }

    static string Esc(string s) { return s.Replace("\\", "\\\\").Replace("\"", "\\\""); }

    static void WriteHtml(string outDir, List<string[]> frames)
    {
        var sb = new StringBuilder();
        sb.Append("const FRAMES=[");
        foreach (var f in frames) sb.Append("[\"" + f[0] + "\",\"" + Esc(f[1]) + "\"],");
        sb.Append("];");
        string tpl = File.ReadAllText(Path.Combine(outDir, "..", "src", "demo_template.html"));
        File.WriteAllText(Path.Combine(outDir, "Hex3Bot_Demo.html"), tpl.Replace("/*FRAMES*/", sb.ToString()), Encoding.UTF8);
    }
}
