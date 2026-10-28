"""Lab screenshots (LabShots.cs .rgb files) to PNG. Usage: shots_to_png.py <dir> [<dir> ...] -> writes <name>.png beside each."""
import struct
import sys
import zlib
from pathlib import Path


def png(path, w, h, rgb):
    rows = b''.join(b'\x00' + rgb[(h - 1 - y) * w * 3:(h - y) * w * 3] for y in range(h))   # bottom-up to top-down

    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xffffffff)
    path.write_bytes(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0))
                     + chunk(b'IDAT', zlib.compress(rows, 6)) + chunk(b'IEND', b''))


for d in sys.argv[1:]:
    for f in sorted(Path(d).glob('*.rgb')):
        data = f.read_bytes()
        w, h = struct.unpack('<ii', data[:8])
        png(f.with_suffix('.png'), w, h, data[8:8 + w * h * 3])
        print(f.with_suffix('.png'))
