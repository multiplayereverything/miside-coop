"""Compare the -mt-snap snapshots of two PCs (src/Lab/LabSnapshot.cs).

Usage: compare_state.py <host snaps dir> <client snaps dir>
A difference is reported as lasting when the same thing differs in two snapshots in a row: one snapshot apart can just be
timing. Exit code 1 if anything lasts.
"""
import pathlib
import sys

KINDS = {"O": "object on/off", "L": "light", "A": "animator", "C": "collider", "R": "renderer"}


def load(path):
    out = {}
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        kind, key, *value = line.split("\t")
        out[(kind, key)] = "/".join(value)
    return out


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    host, client = pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2])
    names = sorted({p.name for p in host.glob("*.txt")} & {p.name for p in client.glob("*.txt")})
    if len(names) < 2:
        print(f"only {len(names)} snapshots on both PCs: nothing to compare")
        return 2
    runs, longest, first, sample = {}, {}, {}, {}
    for name in names:
        h, c = load(host / name), load(client / name)
        now = set()
        # An object destroyed on one PC is only in the other's snapshot.
        for k in h.keys() | c.keys():
            if h.get(k, "gone") != c.get(k, "gone"):
                now.add(k)
                sample[k] = (h.get(k, "gone"), c.get(k, "gone"))
                first.setdefault(k, name[:-4])
        for k in now:
            runs[k] = runs.get(k, 0) + 1
            longest[k] = max(longest.get(k, 0), runs[k])
        for k in set(runs) - now:
            runs[k] = 0
    lasting = {k: n for k, n in longest.items() if n >= 2}
    print(f"{len(names)} snapshots compared; {len(longest)} things ever differed; {len(lasting)} lasting")
    by = {}
    for (kind, key), n in lasting.items():
        by.setdefault(kind, []).append((n, key))
    for kind, items in sorted(by.items()):
        print(f"{KINDS[kind]}: {len(items)}")
        for n, key in sorted(items, reverse=True)[:25]:
            h, c = sample[(kind, key)]
            print(f"  {n} snapshots from {first[(kind, key)]}: host {h} / client {c}: {key}")
    return 1 if lasting else 0


if __name__ == "__main__":
    sys.exit(main())
