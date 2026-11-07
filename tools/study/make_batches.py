"""Split the annotated game code into study batches, and check the study's coverage.

make_batches.py build <il2cpp-evidence.json> <GameAssembly.dll> <annotated-dir> <batch-dir> [target_bytes]
make_batches.py check <batch-dir> <notes-dir>

build: top-level scripts go with their nested types, related scripts stay together, and each script opens with its
field list. Scripts in a namespace are libraries (Steamworks, shaders' helpers) and are listed for one summary entry.
check: every script in scripts.txt needs a "## <name>" heading in the notes. Exits 1 and lists what is missing.
"""
import json
import os
import re
import sys

from pe_il2cpp import Binary


def top(name):
    return name.split("+")[0]


def group_of(name):
    return re.split(r"[_.]", name)[0]


def build(evidence_path, dll_path, code_dir, batch_dir, target):
    bundle = json.load(open(evidence_path, encoding="utf-8"))
    meta = next(r for r in bundle["records"] if r["operation"] == "inspect_il2cpp_metadata")["normalized_result"]
    types = meta["types"]
    game = [i for image in meta["images"] if image["name"] in ("Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll")
            for i in range(image["type_start"], image["type_start"] + image["type_count"])]

    def full(t):
        row = types[t]
        if row["declaring_type"] >= 0:
            return full(row["declaring_type"]) + "+" + row["name"]
        return (row["namespace"] + "." if row["namespace"] else "") + row["name"]

    names = {t: full(t) for t in game}
    declared = {}
    for row, kind in zip(meta["fields"], meta["field_types"]):
        if row["declaring_type"] in names:
            declared.setdefault(row["declaring_type"], []).append((kind["name"] or "?") + " " + row["name"])
    binary = Binary(dll_path, int(meta["binary"]["types_field"], 16))
    fields = {}
    for t, rows in declared.items():
        offsets = binary.offsets_of(t, len(rows))
        fields[t] = [(hex(o) if o is not None and o >= 0 else "?") + " " + row for o, row in zip(offsets, rows)]

    scripts, libraries = {}, {}
    for t, name in names.items():
        if name == "<Module>" or top(name).startswith("<"):
            continue
        (libraries if "." in top(name) else scripts).setdefault(top(name), []).append(t)

    def body(t):
        name = names[t]
        path = os.path.join(code_dir, re.sub(r"[^A-Za-z0-9_.+-]", "_", name) + ".c")
        code = open(path, encoding="utf-8").read() if os.path.exists(path) else "// no compiled methods\n"
        return "=== " + name + "\nFIELDS (declaration order): " + "; ".join(fields.get(t, [])) + "\n" + code + "\n"

    batches, current, size = [], [], 0
    last_group = None
    for name in sorted(scripts, key=lambda n: (group_of(n).lower(), n.lower())):
        text = "".join(body(t) for t in scripts[name])
        if current and size + len(text) > target and (group_of(name) != last_group or size > target):
            batches.append(current)
            current, size = [], 0
        current.append((name, text))
        size += len(text)
        last_group = group_of(name)
    if current:
        batches.append(current)

    os.makedirs(batch_dir, exist_ok=True)
    # FIELDS lines can be too long for a line-limited reader, so every type's fields are also listed one per line.
    with open(os.path.join(batch_dir, "fields.txt"), "w", encoding="utf-8", newline="\n") as f:
        for t in sorted(fields, key=lambda t: names[t].lower()):
            f.write("=== " + names[t] + "\n" + "\n".join(fields[t]) + "\n")
    with open(os.path.join(batch_dir, "scripts.txt"), "w", encoding="utf-8", newline="\n") as index:
        for i, batch in enumerate(batches, 1):
            with open(os.path.join(batch_dir, f"batch-{i:02}.txt"), "w", encoding="utf-8", newline="\n") as f:
                for name, text in batch:
                    f.write(text)
                    index.write(f"{name}\n")
            print(f"batch-{i:02}  {sum(len(t) for _, t in batch) / 1000:7.0f} KB  {len(batch):3} scripts  {batch[0][0]} .. {batch[-1][0]}")
        for name in sorted({n.split(".")[0] for n in libraries}):
            index.write(f"{name}.*\n")
    print(f"scripts {len(scripts)}, library groups {len({n.split('.')[0] for n in libraries})}, batches {len(batches)}")


def check(batch_dir, notes_dir):
    wanted = [l.strip() for l in open(os.path.join(batch_dir, "scripts.txt"), encoding="utf-8") if l.strip()]
    found = set()
    for name in os.listdir(notes_dir) if os.path.isdir(notes_dir) else []:
        if name.endswith(".md"):
            found.update(m.strip() for m in re.findall(r"^## +(.+)$", open(os.path.join(notes_dir, name), encoding="utf-8").read(), re.M))
    missing = [w for w in wanted if w not in found]
    print(f"covered {len(wanted) - len(missing)} of {len(wanted)}")
    for name in missing[:200]:
        print("MISSING", name)
    return 1 if missing else 0


if __name__ == "__main__":
    if sys.argv[1] == "build":
        build(sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5], int(sys.argv[6]) if len(sys.argv) > 6 else 330000)
    else:
        sys.exit(check(sys.argv[2], sys.argv[3]))
