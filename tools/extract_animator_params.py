"""Build step: reads every AnimatorController in the user's own MiSide data and writes its parameter table.

MiSide's IL2CPP build strips Animator.parameters/GetParameter, so the mod cannot list parameters at runtime.
Output (one line per parameter): <controller name>\t<parameter name>\t<type>   (type = AnimatorControllerParameterType)
Usage: python -I extract_animator_params.py <MiSideFull_Data> <out.tsv>
"""
import sys
from pathlib import Path
import UnityPy

data, out = Path(sys.argv[1]), Path(sys.argv[2])
rows = set()
for f in sorted(data.iterdir()):
    if f.name.endswith(".resS") or f.name.endswith(".resource"):
        continue
    if not (f.suffix == ".assets" or (f.name.startswith("level") and f.suffix == "")):
        continue
    env = UnityPy.load(str(f))
    for obj in env.objects:
        if obj.type.name != "AnimatorController":
            continue
        tree = obj.read_typetree()
        tos = dict(tree.get("m_TOS", []))
        values = tree["m_Controller"]["m_Values"]["data"]["m_ValueArray"]
        for v in values:
            name = tos.get(v["m_ID"])
            if name:
                rows.add((tree["m_Name"], name, int(v["m_Type"])))
out.parent.mkdir(parents=True, exist_ok=True)
out.write_text("".join(f"{c}\t{n}\t{t}\n" for c, n, t in sorted(rows)), encoding="utf-8")
print(f"{len(rows)} parameters from {len({r[0] for r in rows})} controllers -> {out}")
