"""Turn REA's IL2CPP metadata evidence into a method table for ExportGame.java.

Usage: build_method_table.py <il2cpp-evidence.json> <out-dir>
Writes methods.tsv (rva, export flag, class, method, signature), types.tsv and strings.tsv.
Output holds game names and text: keep it outside the repo, never distribute it.
"""
import json
import os
import sys


def main(evidence_path, out_dir):
    bundle = json.load(open(evidence_path, encoding="utf-8"))
    meta = next(r for r in bundle["records"] if r["operation"] == "inspect_il2cpp_metadata")["normalized_result"]
    types, methods = meta["types"], meta["methods"]

    image_of = {}
    for image in meta["images"]:
        for t in range(image["type_start"], image["type_start"] + image["type_count"]):
            image_of[t] = image["name"]

    def full_name(t):
        row = types[t]
        name = row["name"]
        if row["declaring_type"] >= 0:
            return full_name(row["declaring_type"]) + "+" + name
        return (row["namespace"] + "." if row["namespace"] else "") + name

    params = {}
    for row, kind in zip(meta["parameters"], meta["parameter_types"]):
        params.setdefault(row["method"], []).append((kind["name"] or "?") + " " + row["name"])

    os.makedirs(out_dir, exist_ok=True)
    exported = 0
    with open(os.path.join(out_dir, "methods.tsv"), "w", encoding="utf-8", newline="\n") as f:
        for i, (m, a, r) in enumerate(zip(methods, meta["method_addresses"], meta["return_types"])):
            if a["state"] != "code":
                continue
            t = m["declaring_type"]
            game = image_of.get(t) in ("Assembly-CSharp.dll", "Assembly-CSharp-firstpass.dll")
            exported += game
            signature = (r["name"] or "?") + " " + m["name"] + "(" + ", ".join(params.get(i, [])) + ")"
            f.write("\t".join([a["rva"], "1" if game else "0", full_name(t), m["name"], signature]) + "\n")

    with open(os.path.join(out_dir, "types.tsv"), "w", encoding="utf-8", newline="\n") as f:
        for t in range(len(types)):
            f.write("\t".join([image_of.get(t, ""), full_name(t), str(types[t]["method_count"])]) + "\n")

    with open(os.path.join(out_dir, "strings.tsv"), "w", encoding="utf-8", newline="\n") as f:
        for i, s in enumerate(meta["string_literals"]):
            f.write(str(i) + "\t" + json.dumps(s["value"], ensure_ascii=False) + "\n")

    print(f"types {len(types)}, methods with code to export {exported}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
