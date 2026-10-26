"""Research: per scene, how many objects carry the given game scripts, plus non-kinematic Rigidbodies."""
import sys
from pathlib import Path
import UnityPy

data = Path(sys.argv[1]); wanted = set(sys.argv[2].split(","))
names = ["SceneAihasto", "SceneLoading", "SceneMenu"] + [f"Scene {i}" for i in range(1, 21)] + ["MinigameShooter"]
for i, name in enumerate(names):
    f = data / f"level{i}"
    if not f.exists():
        continue
    env = UnityPy.load(str(f))
    counts = {w: 0 for w in wanted}; bodies = 0
    for o in env.objects:
        if o.type.name == "Rigidbody":
            try:
                if not o.read_typetree().get("m_IsKinematic", 0): bodies += 1
            except Exception: pass
        elif o.type.name == "MonoBehaviour":
            try:
                script = o.read().m_Script.read()
                cls = getattr(script, "m_ClassName", None) or script.read_typetree().get("m_ClassName")
                if cls in counts: counts[cls] += 1
            except Exception: pass
    print(f"{name:16} bodies={bodies:3} " + " ".join(f"{k}={v}" for k, v in sorted(counts.items()) if v))
