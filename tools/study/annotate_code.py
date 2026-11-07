"""Make ExportGame.java's output readable: resolve IL2CPP metadata references and drop compiler boilerplate.

Usage: annotate_code.py <il2cpp-evidence.json> <GameAssembly.dll> <code-dir> <out-dir>
Each DAT_ address holding an encoded metadata usage (usage << 29 | index << 1 | 1) becomes the string, type or
method it stands for. Output is decompiled game code: keep it outside the repo, never distribute it.
"""
import json
import os
import re
import struct
import sys

from pe_il2cpp import Binary

BASE = 0x180000000
TYPE_INFO, TYPE_REF, METHOD_DEF, FIELD, STRING, METHOD_REF = 1, 2, 3, 4, 5, 6

INIT_GUARD = re.compile(r"^  if \(DAT_[0-9a-f]+ == '\\0'\) \{\n(?:    func_0x[0-9a-f]+\(&DAT_[0-9a-f]+\);\n)*    DAT_[0-9a-f]+ = '\\x01';\n  \}\n", re.M)
CCTOR_CHECK = re.compile(r"^ *if \(\*\(int \*\)\([^\n]*\+ 0xe0\) == 0\) \{\n *func_0x[0-9a-f]+\([^\n]*\);\n *\}\n", re.M)
GLOBALS_WARNING = re.compile(r"^/\* WARNING: Globals starting with '_' overlap smaller symbols at the same address \*/\n\n?", re.M)
DECLARATION = re.compile(r"^  (?:undefined\d?|code|char|int|uint|longlong|ulonglong|float|double|bool|byte|ushort|short)\s+\*{0,3}[A-Za-z_]\w*(?:\s*\[\d+\])?;\n", re.M)
ADDRESS = re.compile(r"&?_?(?:DAT|PTR_DAT)_(1[0-9a-f]{8})")


def read_sections(data):
    pe = struct.unpack_from("<I", data, 0x3c)[0]
    count, optional = struct.unpack_from("<xxHxxxxxxxxxxxxH", data, pe + 4)
    table = pe + 24 + optional
    sections = []
    for i in range(count):
        size, rva, raw_size, raw = struct.unpack_from("<IIII", data, table + i * 40 + 8)
        sections.append((rva, raw_size, raw))
    return sections


def main(evidence_path, dll_path, code_dir, out_dir):
    bundle = json.load(open(evidence_path, encoding="utf-8"))
    meta = next(r for r in bundle["records"] if r["operation"] == "inspect_il2cpp_metadata")["normalized_result"]
    data = open(dll_path, "rb").read()
    sections = read_sections(data)
    strings = meta["string_literals"]
    types = meta["types"]

    type_names = {}
    for rows in (meta["field_types"], meta["parameter_types"], meta["return_types"]):
        for row in rows:
            if row.get("name") and row.get("type") is not None:
                type_names[row["type"]] = row["name"]

    binary = Binary(dll_path, int(meta["binary"]["types_field"], 16))

    def type_label(index):
        if index in type_names:
            return type_names[index]
        definition = binary.type_definition(index)
        return types[definition]["name"] if definition is not None and definition < len(types) else "#" + str(index)

    def method_label(i):
        m = meta["methods"][i]
        return types[m["declaring_type"]]["name"] + "." + m["name"]

    def resolve(address):
        rva = address - BASE
        for start, size, raw in sections:
            if start <= rva < start + size and rva + 8 <= start + size:
                value = struct.unpack_from("<Q", data, raw + rva - start)[0]
                break
        else:
            return None
        if value == 0 or value & 1 == 0 or value >> 32:
            return None
        usage, index = (value >> 29) & 7, (value & 0x1fffffff) >> 1
        if usage == STRING and index < len(strings):
            return json.dumps(strings[index]["value"], ensure_ascii=False)
        if usage in (TYPE_INFO, TYPE_REF):
            return "type:" + type_label(index)
        if usage == METHOD_DEF and index < len(meta["methods"]):
            return "method:" + method_label(index)
        if usage == METHOD_REF:
            return "generic-method:#" + str(index)
        if usage == FIELD:
            return "field:#" + str(index)
        return None

    def replace(match):
        label = resolve(int(match.group(1), 16))
        return "{" + label + "}" if label else match.group(0)

    os.makedirs(out_dir, exist_ok=True)
    before = after = resolved = 0
    for name in sorted(os.listdir(code_dir)):
        text = open(os.path.join(code_dir, name), encoding="utf-8").read()
        before += len(text)
        text = GLOBALS_WARNING.sub("", text)
        text = INIT_GUARD.sub("", text)
        text = CCTOR_CHECK.sub("", text)
        text = DECLARATION.sub("", text)
        text, count = ADDRESS.subn(replace, text)
        resolved += text.count("{") and count
        text = re.sub(r"\n{3,}", "\n\n", text)
        after += len(text)
        open(os.path.join(out_dir, name), "w", encoding="utf-8", newline="\n").write(text)
    print(f"files {len(os.listdir(out_dir))}, {before / 1e6:.1f} MB -> {after / 1e6:.1f} MB")


if __name__ == "__main__":
    main(*sys.argv[1:5])
