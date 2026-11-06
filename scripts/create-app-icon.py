"""Generate deterministic multi-size Windows ICO, no external imaging dependency."""
import pathlib, struct, zlib

def png(size):
    rows = []
    for y in range(size):
        line = bytearray()
        for x in range(size):
            u, v = x / size, y / size
            inside = .08 <= u <= .92 and .08 <= v <= .92 and u + v >= .28 and u + v <= 1.72
            color = (216, 255, 0, 255) if inside else (0, 0, 0, 0)
            # Two offset diagonal bands: abstract momentum mark, not a game logo.
            if inside and .25 <= v <= .75 and (abs(u - (.22 + .5 * v)) < .065 or abs(u - (.44 + .5 * v)) < .065):
                color = (7, 18, 28, 255)
            line.extend(color)
        rows.append(b'\0' + line)
    def chunk(t, data):
        return struct.pack('>I', len(data)) + t + data + struct.pack('>I', zlib.crc32(t + data) & 0xffffffff)
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(b''.join(rows), 9)) + chunk(b'IEND', b'')

sizes = [16, 32, 48, 64, 128, 256]
blobs = [png(n) for n in sizes]
offset = 6 + 16 * len(sizes)
header = struct.pack('<HHH', 0, 1, len(sizes))
for n, b in zip(sizes, blobs):
    header += struct.pack('<BBBBHHII', n if n < 256 else 0, n if n < 256 else 0, 0, 0, 1, 32, len(b), offset)
    offset += len(b)
path = pathlib.Path(__file__).resolve().parents[1] / 'src' / 'Whoosh.Gui' / 'app.ico'
path.write_bytes(header + b''.join(blobs))
print(path)
