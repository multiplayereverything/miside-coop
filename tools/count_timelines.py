"""Research: how many PlayableDirector components each level file (scene) of the user's game holds."""
import sys
from pathlib import Path
import UnityPy

data = Path(sys.argv[1])
names = ["SceneAihasto", "SceneLoading", "SceneMenu"] + [f"Scene {i}" for i in range(1, 21)] + ["MinigameShooter"]
for i, name in enumerate(names):
    f = data / f"level{i}"
    if not f.exists():
        continue
    env = UnityPy.load(str(f))
    n = sum(1 for o in env.objects if o.type.name == "PlayableDirector")
    print(f"level{i:2} {name:16} directors={n}")
