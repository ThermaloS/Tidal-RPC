# Renders the TIDAL RPC app icon (teal rounded square with three bars, from src/TidalRpc/Resources/App.svg) as AppIcon.icns.
import struct, subprocess, sys, tempfile, os, zlib

SUB = 3
BG = (0x4d, 0xda, 0xbc)
FG = (0x11, 0x14, 0x1a)
BARS = [(64, 84.5, 92, 171.5), (113, 54, 141, 202), (162, 84.5, 190, 171.5)]  # x0, y0, x1, y1 in 256-unit space

def rr_inside(px, py, x0, y0, x1, y1, r):
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    hw, hh = (x1 - x0) / 2, (y1 - y0) / 2
    qx, qy = abs(px - cx) - (hw - r), abs(py - cy) - (hh - r)
    d = (max(qx, 0) ** 2 + max(qy, 0) ** 2) ** 0.5 + min(max(qx, qy), 0) - r
    return d <= 0

def render(size):
    s = 256 / size
    rows = bytearray()
    for y in range(size):
        rows.append(0)
        for x in range(size):
            bg_hits = fg_hits = 0
            for sy in range(SUB):
                for sx in range(SUB):
                    px = (x + (sx + 0.5) / SUB) * s
                    py = (y + (sy + 0.5) / SUB) * s
                    if rr_inside(px, py, 4, 4, 252, 252, 62):
                        bg_hits += 1
                        if any(rr_inside(px, py, *b, 14) for b in BARS):
                            fg_hits += 1
            a = round(255 * bg_hits / (SUB * SUB))
            if bg_hits == 0:
                rows += bytes([0, 0, 0, 0])
                continue
            t = fg_hits / bg_hits
            color = [round(BG[i] * (1 - t) + FG[i] * t) for i in range(3)]
            rows += bytes(color + [a])
    return bytes(rows)

def png(size, data):
    def chunk(tag, body):
        return struct.pack(">I", len(body)) + tag + body + struct.pack(">I", zlib.crc32(tag + body) & 0xffffffff)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(data, 9)) + chunk(b"IEND", b"")

out = sys.argv[1]
with tempfile.TemporaryDirectory() as tmp:
    iconset = os.path.join(tmp, "AppIcon.iconset")
    os.makedirs(iconset)
    base = os.path.join(tmp, "base.png")
    open(base, "wb").write(png(1024, render(1024)))
    for name, size in [("icon_16x16", 16), ("icon_16x16@2x", 32), ("icon_32x32", 32), ("icon_32x32@2x", 64),
                       ("icon_128x128", 128), ("icon_128x128@2x", 256), ("icon_256x256", 256), ("icon_256x256@2x", 512),
                       ("icon_512x512", 512), ("icon_512x512@2x", 1024)]:
        subprocess.run(["sips", "-z", str(size), str(size), base, "--out", os.path.join(iconset, name + ".png")], check=True, capture_output=True)
    subprocess.run(["iconutil", "-c", "icns", iconset, "-o", out], check=True)
print("wrote", out)
