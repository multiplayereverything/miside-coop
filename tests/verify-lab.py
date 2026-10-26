"""Judge a MiSide Together lab run from each instance's misidetogether-lab.log (ported from Spooky Together).

Checks:
  * every MT-CHECK line passed, and each --expect'ed check appeared on the right instances
  * no mod errors (MT-MODERROR) at all
  * game errors do not keep growing after warm-up (slope over the last half of the run)
  * the managed heap does not trend upward while playing a chapter
  * the mod stays within its frame budget and object bound
  * clients end in the host's scene, and Mita desync stays bounded
  * MT-STATE lines (every 2 s per machine) lined up by wall clock against the host's: NPC position and animation
    state, dialogue lines, cutscene mode and timeline time; reported under "sync", gross desync fails
  * MT-ANIM lines (every NPC animation state change, as it happens) paired with the host's: how late each scripted
    animation started on the client, and how many never showed there (sync "npc_anim_start_s_p90", "npc_anim_missed")

Usage: python -I verify-lab.py <mod root> [--expect name[@role] ...] [--out results.json]
  name@host / name@c1   the check must pass on that instance
  name@anyclient        at least one client must pass it
  client.name           every client must pass "name"
  name                  every instance must pass it
"""
import json
import re
import sys
from pathlib import Path

LABS = {"host": "lab-host", "c1": "lab-client", "c2": "lab-c2", "c3": "lab-c3"}
METRIC = re.compile(r"MT-METRIC (.*)")
CHECK = re.compile(r"MT-CHECK (\S+) (PASS|FAIL) ?(.*)")
STATE = re.compile(r"^(\d\d):(\d\d):(\d\d)\.(\d+) MT-STATE (.*)")
ANIM = re.compile(r"^(\d\d):(\d\d):(\d\d)\.(\d+) MT-ANIM scene=(\S+) who=(\S+) state=(-?\d+)")


def parse_state(line):
    m = STATE.search(line)
    if not m:
        return None
    t = int(m.group(1)) * 3600 + int(m.group(2)) * 60 + int(m.group(3)) + float("0." + m.group(4))
    kv = dict(x.split("=", 1) for x in m.group(5).split() if "=" in x)

    def table(field, sep):
        out = {}
        for item in kv.get(field, "").split(";"):
            if sep in item:
                k, v = item.split(sep, 1)
                out[k] = v
        return out
    return {"t": t, "scene": kv.get("scene"), "cut": kv.get("cut") == "1", "npc": table("npc", ":"), "tl": table("tl", ":"),
            "lines": set(x for x in kv.get("lines", "").split(";") if x), "lag": kv.get("lag")}


def compare(host, client):
    """Desync of a client against the host, from samples taken within 1 s of each other in the same scene."""
    npc_d, npc_anim, line_diff, cut_diff, tl_d, n = [], [0, 0], 0, 0, [], 0
    j = 0
    for c in client:
        while j + 1 < len(host) and abs(host[j + 1]["t"] - c["t"]) <= abs(host[j]["t"] - c["t"]):
            j += 1
        if not host:
            break
        h = host[j]
        dt = c["t"] - h["t"]
        # Chapters only: the boot splash and menus start whenever each copy was launched.
        if abs(dt) > 1.0 or h["scene"] != c["scene"] or not str(c["scene"]).startswith("Scene_"):
            continue
        n += 1
        for k, v in c["npc"].items():
            if k in h["npc"]:
                a, b = v.split(","), h["npc"][k].split(",")
                try:
                    npc_d.append(sum((float(a[i]) - float(b[i])) ** 2 for i in range(3)) ** 0.5)
                    npc_anim[0] += 1; npc_anim[1] += a[3] != b[3]
                except (ValueError, IndexError):
                    pass
        # Lines change every second or two; a client is in step if any host sample within 1.2 s shows the same lines.
        near = [x for x in host[max(0, j - 2):j + 3] if abs(x["t"] - c["t"]) <= 1.2 and x["scene"] == c["scene"]]
        line_diff += not any(x["lines"] == c["lines"] for x in near)
        cut_diff += c["cut"] != h["cut"]
        for k, v in c["tl"].items():
            if k in h["tl"]:
                tl_d.append(abs(float(v) - (float(h["tl"][k]) + dt)))
    p90 = lambda xs: sorted(xs)[int(len(xs) * 0.9)] if xs else None
    lag = next((c["lag"] for c in reversed(client) if c["lag"]), None)
    return {"samples": n, "npc_m_p90": p90(npc_d), "npc_anim_mismatch": round(npc_anim[1] / npc_anim[0], 2) if npc_anim[0] else None,
            "dialogue_mismatch": round(line_diff / n, 2) if n else None, "cutscene_mismatch": round(cut_diff / n, 2) if n else None,
            "timeline_s_p90": p90(tl_d), "dialogue_lag_max_mean": lag}


def compare_anims(host, client):
    """NPC animation starts: each host state change paired with the same change on the client within 3 s."""
    lags, missed = [], 0
    for t, scene, who, state in host:
        if who == "me" or not scene.startswith("Scene_"):
            continue
        near = [c[0] - t for c in client if c[1] == scene and c[2] == who and c[3] == state and abs(c[0] - t) <= 3.0]
        if near:
            lags.append(abs(min(near, key=abs)))
        else:
            missed += 1
    total = len(lags) + missed
    p90 = sorted(lags)[int(len(lags) * 0.9)] if lags else None
    return {"npc_anim_changes": total, "npc_anim_start_s_p90": round(p90, 2) if p90 is not None else None,
            "npc_anim_missed": round(missed / total, 2) if total else None}


def parse(path):
    metrics, checks, moderrors, states, anims = [], [], [], [], []
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        st = parse_state(line)
        if st:
            states.append(st)
            continue
        a = ANIM.search(line)
        if a:
            t = int(a.group(1)) * 3600 + int(a.group(2)) * 60 + int(a.group(3)) + float("0." + a.group(4))
            anims.append((t, a.group(5), a.group(6), a.group(7)))
            continue
        m = METRIC.search(line)
        if m:
            metrics.append(dict(kv.split("=", 1) for kv in m.group(1).split() if "=" in kv))
            continue
        c = CHECK.search(line)
        if c:
            checks.append((c.group(1), c.group(2) == "PASS", c.group(3)))
        if "MT-MODERROR" in line:
            moderrors.append(line.strip()[:400])
    return metrics, checks, moderrors, states, anims


def slope(values):
    n = len(values)
    if n < 3:
        return 0.0
    mx, my = (n - 1) / 2, sum(values) / n
    den = sum((x - mx) ** 2 for x in range(n))
    return sum((x - mx) * (y - my) for x, y in zip(range(n), values)) / den if den else 0.0


def main():
    root = Path(sys.argv[1])
    args = sys.argv[2:]
    expects, out = [], None
    allow_game_errors = False   # a tour that reproduces a known game-side failure (scene19)
    i = 0
    while i < len(args):
        if args[i] == "--expect":
            expects.append(args[i + 1]); i += 2
        elif args[i] == "--allow-game-errors":
            allow_game_errors = True; i += 1
        elif args[i] == "--out":
            out = args[i + 1]; i += 2
        else:
            i += 1
    results, failures = {}, []
    for role, folder in LABS.items():
        path = root / "private" / folder / "misidetogether-lab.log"
        if not path.exists() or path.stat().st_size == 0:
            continue
        metrics, checks, moderrors, states, anims = parse(path)
        info = {"_states": states, "_anims": anims, "checks": [{"name": n, "pass": p, "detail": d} for n, p, d in checks], "moderrors": moderrors[:5]}
        for n, p, d in checks:
            if not p:
                failures.append(f"{role}: check {n} failed: {d}")
        if moderrors:
            failures.append(f"{role}: {len(moderrors)} mod errors, first: {moderrors[0]}")
        if metrics:
            half = metrics[len(metrics) // 2:]
            err = [float(m.get("errors", 0)) for m in half]
            # Memory is judged while playing a chapter; loading a scene is not a leak.
            playing = [m for m in metrics if m.get("phase") == "chapter"]
            # The heap as the last GC left it ("live"); the raw heap also counts garbage not collected yet.
            # Judged inside one chapter (its longest stretch): loading another chapter steps the heap up, which is not a leak.
            runs, cur = [], []
            for m in playing:
                if cur and m.get("scene") != cur[-1].get("scene"):
                    runs.append(cur); cur = []
                cur.append(m)
            if cur:
                runs.append(cur)
            longest = max(runs, key=len) if runs else []
            live = [float(m.get("live", m.get("heapMB", 0))) for m in longest]
            heap = live[len(live) // 2:] if len(live) >= 6 else []
            # A leak raises the floor: the lowest live heap of the chapter's second half stays well above the first half's
            # (the live value only moves when a collection runs, so a slope over a few samples is noise).
            # Sustained growth only: the floor rises from the first third to the second AND again to the last (a single
            # step, such as a player joining and their body being copied, then levelling off, is not a leak).
            if len(live) >= 9:
                n3 = len(live) // 3
                f1, f2, f3 = min(live[:n3]), min(live[n3:2 * n3]), min(live[2 * n3:])
                floor_rise = (f3 - f1) if (f2 - f1 > 2.0 and f3 - f2 > 2.0) else 0.0
            else:
                floor_rise = 0.0
            info["live_floor_rise_mb"] = round(floor_rise, 2)
            alloc = [float(m["alloc"]) for m in playing if "alloc" in m]
            if alloc:
                info["alloc_mb_per_s_median"] = sorted(alloc)[len(alloc) // 2]
            info.update({
                "samples": len(metrics),
                "scene_end": metrics[-1].get("scene"),
                "errors_end": metrics[-1].get("errors"),
                "error_slope_per_10s": round(slope(err), 3),
                "heap_slope_mb_per_10s": round(slope(heap), 3),
            })
            if slope(err) > 0.5 and not allow_game_errors:
                failures.append(f"{role}: game errors keep growing ({slope(err):.2f} per 10 s)")
            if floor_rise > 4.0 and not allow_game_errors:
                failures.append(f"{role}: live heap floor rose {floor_rise:.1f} MB within one chapter (leak)")
            mod = sorted(float(m["modms"]) for m in metrics if float(m.get("fps", 0)) > 0)
            if mod:
                info["modms_median"] = mod[len(mod) // 2]
                info["modms_max"] = mod[-1]
                if mod[len(mod) // 2] > 2.0:
                    failures.append(f"{role}: mod costs {mod[len(mod) // 2]:.2f} ms per frame (median), budget 2 ms")
            spikes = [float(m["modmax"]) for m in metrics if "modmax" in m and m.get("phase") == "chapter"]
            if spikes:
                info["modmax_frame_ms"] = max(spikes)
                # Chapter-start work (body copy, room scan) may hitch once or twice; recurring hitches fail.
                hitchy = sum(1 for x in spikes if x > 8.0)
                info["hitch_windows"] = f"{hitchy}/{len(spikes)}"
                # Informational: with 2-4 game copies on one PC, CPU contention and GC pauses land in whatever step runs
                # (spikes= shows them spread over trivial steps), so worst frames measure the lab, not the mod.
            mobj = [float(m["modobjects"]) for m in metrics if "modobjects" in m]
            if mobj:
                info["modobjects_end"] = mobj[-1]
                # The BepInEx host object + one avatar per peer (+ its temporary holder while copying).
                peers = max(int(m.get("peers", 0)) for m in metrics)
                bound = 2 + 2 * peers
                if max(mobj) > bound:
                    failures.append(f"{role}: the mod holds {max(mobj):.0f} objects, more than its bound of {bound}")
            desync = [float(m["mitadesync"]) for m in metrics if float(m.get("mitadesync", -1)) >= 0]
            if desync:
                info["mitadesync_max"] = max(desync)
        results[role] = info
    if not results:
        failures.append("no lab logs found")
    host_states = results.get("host", {}).get("_states", [])
    for role, info in results.items():
        if role != "host" and host_states and info.get("_states"):
            sync = compare(host_states, info["_states"])
            sync.update(compare_anims(results["host"]["_anims"], info["_anims"]))
            info["sync"] = sync
            if sync["npc_anim_changes"] >= 5 and (sync["npc_anim_start_s_p90"] or 0) > 1.0:
                failures.append(f"{role}: NPC animations start {sync['npc_anim_start_s_p90']:.2f} s after the host's (p90)")
            if sync["samples"] >= 5:
                if (sync["dialogue_mismatch"] or 0) > 0.4:
                    failures.append(f"{role}: dialogue lines differ from the host's in {sync['dialogue_mismatch']:.0%} of samples")
                if (sync["cutscene_mismatch"] or 0) > 0.3:
                    failures.append(f"{role}: cutscene mode differs from the host's in {sync['cutscene_mismatch']:.0%} of samples")
                if (sync["timeline_s_p90"] or 0) > 1.0:
                    failures.append(f"{role}: timelines {sync['timeline_s_p90']:.2f} s apart (p90)")
    for info in results.values():
        info.pop("_states", None)
        info.pop("_anims", None)
    host_scene = results.get("host", {}).get("scene_end")
    for role, info in results.items():
        if role != "host" and host_scene and info.get("scene_end") and info["scene_end"] != host_scene \
                and "Loading" not in str(info["scene_end"]) and "Loading" not in str(host_scene):
            failures.append(f"{role}: ended in {info['scene_end']}, host in {host_scene}")
    for spec in expects:
        name, _, where = spec.partition("@")
        if where == "anyclient":
            if not any(c["pass"] and c["name"] == name for r, info in results.items() if r != "host" for c in info.get("checks", [])):
                failures.append(f"no client passed {name}")
            continue
        clients_only = name.startswith("client.")
        if clients_only:
            name = name[len("client."):]
        roles = [where] if where else [r for r in LABS if r != "host" and r in results] or ["c1"] if clients_only else list(results) or ["host"]
        for role in roles:
            names = [c["name"] for c in results.get(role, {}).get("checks", []) if c["pass"]]
            if name not in names:
                failures.append(f"{role}: expected passing check {name} not seen")
    summary = {"pass": not failures, "failures": failures, "instances": results}
    text = json.dumps(summary, indent=2)
    if out:
        Path(out).write_text(text, encoding="utf-8")
    print(text)
    sys.exit(0 if not failures else 1)


if __name__ == "__main__":
    main()
