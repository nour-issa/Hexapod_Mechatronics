# Embeds the exported STL files into the 3D demo page.
# usage: python -I build_demo.py <CAD folder>
import base64, json, pathlib, sys

cad = pathlib.Path(sys.argv[1])
models = {p.stem: base64.b64encode(p.read_bytes()).decode() for p in sorted((cad / "demo" / "stl").glob("*.stl"))}
tpl = (cad / "src" / "demo3d_template.html").read_text(encoding="utf-8")
out = cad / "demo" / "Hex3Bot_Demo.html"
out.write_text(tpl.replace("/*MODELS*/", "const MODELS = " + json.dumps(models) + ";"), encoding="utf-8")
print(out, f"{out.stat().st_size/1024:.0f} KB, {len(models)} parts")
