"""Reads the Kenney cake GLB (untrusted input, parsed by hand) and writes one merged mesh as a small binary file:
'BCK1', int32 vertex count, int32 index count, positions (3 float), normals (3 float), uvs (2 float), uint16 indices.
glTF is right-handed with the UV origin top left; Unity is left-handed with the UV origin bottom left, so z is
negated, v becomes 1 - v and each triangle's winding is reversed."""
import json, struct, sys

src, dst = sys.argv[1], sys.argv[2]
data = open(src, "rb").read()
assert data[:4] == b"glTF", "not a GLB"
off, chunks = 12, []
while off < len(data):
    clen, ctype = struct.unpack_from("<II", data, off)
    chunks.append(data[off + 8: off + 8 + clen])
    off += 8 + clen
gltf, binary = json.loads(chunks[0].decode("utf-8")), chunks[1]
SIZES = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4}
FORMATS = {5126: "f", 5123: "H", 5125: "I"}


def read(index):
    acc = gltf["accessors"][index]
    view = gltf["bufferViews"][acc["bufferView"]]
    n, fmt = SIZES[acc["type"]], FORMATS[acc["componentType"]]
    item = struct.calcsize("<" + fmt * n)
    stride = view.get("byteStride", item)
    base = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    return [struct.unpack_from("<" + fmt * n, binary, base + i * stride) for i in range(acc["count"])]


pos, nor, uv, idx = [], [], [], []
for node in gltf["nodes"]:
    t = node.get("translation", [0, 0, 0])
    assert "rotation" not in node and "scale" not in node, "node transform beyond translation"
    for prim in gltf["meshes"][node["mesh"]]["primitives"]:
        start = len(pos)
        a = prim["attributes"]
        pos += [(p[0] + t[0], p[1] + t[1], -(p[2] + t[2])) for p in read(a["POSITION"])]
        nor += [(q[0], q[1], -q[2]) for q in read(a["NORMAL"])]
        uv += [(w[0], 1.0 - w[1]) for w in read(a["TEXCOORD_0"])]
        tri = [i[0] + start for i in read(prim["indices"])]
        for k in range(0, len(tri), 3):
            idx += [tri[k], tri[k + 2], tri[k + 1]]
assert len(pos) < 65536
with open(dst, "wb") as f:
    f.write(b"BCK1" + struct.pack("<ii", len(pos), len(idx)))
    for p in pos: f.write(struct.pack("<3f", *p))
    for q in nor: f.write(struct.pack("<3f", *q))
    for w in uv: f.write(struct.pack("<2f", *w))
    f.write(struct.pack("<%dH" % len(idx), *idx))
lo = [min(p[i] for p in pos) for i in range(3)]
hi = [max(p[i] for p in pos) for i in range(3)]
print("vertices", len(pos), "indices", len(idx), "min", lo, "max", hi)
# Heights of the layers: how many vertices sit at each y, to find the top of the cake below the candles.
ys = {}
for p in pos: ys[round(p[1], 3)] = ys.get(round(p[1], 3), 0) + 1
print("y levels", sorted(ys.items()))
