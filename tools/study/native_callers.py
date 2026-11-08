import bisect, struct, sys
dll, tsv, *targets = sys.argv[1:]
data = open(dll, 'rb').read()
pe = struct.unpack_from('<I', data, 0x3c)[0]
nsec = struct.unpack_from('<H', data, pe + 6)[0]
opt = struct.unpack_from('<H', data, pe + 20)[0]
secs = []
for i in range(nsec):
    o = pe + 24 + opt + 40 * i
    name = data[o:o + 8].rstrip(b'\0')
    vsize, va, rsize, raw = struct.unpack_from('<IIII', data, o + 8)
    secs.append((name, va, vsize, raw, rsize))
meth = []
for line in open(tsv, encoding='utf-8'):
    p = line.rstrip('\n').split('\t')
    if p[0].startswith('0x'): meth.append((int(p[0], 16), p[2] + '.' + p[3]))
meth.sort(); addrs = [m[0] for m in meth]
def name(rva):
    i = bisect.bisect_right(addrs, rva) - 1
    return meth[i][1] + '+0x%x' % (rva - meth[i][0]) if i >= 0 else '?'
want = {int(t, 16) for t in targets}
for sname, va, vsize, raw, rsize in secs:
    if sname not in (b'.text', b'il2cpp'): continue
    buf = data[raw:raw + rsize]
    for i in range(len(buf) - 5):
        if buf[i] in (0xE8, 0xE9):
            rel = struct.unpack_from('<i', buf, i + 1)[0]
            tgt = va + i + 5 + rel
            if tgt in want: print(hex(tgt), 'call' if buf[i] == 0xE8 else 'jmp ', hex(va + i), name(va + i))

