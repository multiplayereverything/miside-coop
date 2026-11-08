import bisect, struct, sys
import capstone
dll, tsv = sys.argv[1], sys.argv[2]
wanted = sys.argv[3:]
data = open(dll, 'rb').read()
pe = struct.unpack_from('<I', data, 0x3c)[0]
nsec = struct.unpack_from('<H', data, pe + 6)[0]
opt = struct.unpack_from('<H', data, pe + 20)[0]
secs = []
for i in range(nsec):
    o = pe + 24 + opt + 40 * i
    vsize, va, rsize, raw = struct.unpack_from('<IIII', data, o + 8)
    secs.append((va, vsize, raw))
def off(rva):
    for va, vsize, raw in secs:
        if va <= rva < va + vsize: return raw + rva - va
meth = []
for line in open(tsv, encoding='utf-8'):
    p = line.rstrip('\n').split('\t')
    if p[0].startswith('0x'): meth.append((int(p[0], 16), p[2] + '.' + p[3]))
meth.sort(); addrs = [m[0] for m in meth]
def exact(rva):
    i = bisect.bisect_left(addrs, rva)
    return meth[i][1] if i < len(addrs) and addrs[i] == rva else None
md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
for w in wanted:
    starts = [a for a, n in meth if n == w]
    for s in starts:
        i = bisect.bisect_right(addrs, s)
        end = addrs[i] if i < len(addrs) else s + 0x2000
        names = []
        for ins in md.disasm(data[off(s):off(min(end, s + 0x4000))], s):
            if ins.mnemonic in ('call', 'jmp') and ins.op_str.startswith('0x'):
                n = exact(int(ins.op_str, 16))
                if n and not n.startswith('UnityEngine.') and not n.startswith('System.') and n not in names: names.append(n)
        print(w, '->', ', '.join(names))
