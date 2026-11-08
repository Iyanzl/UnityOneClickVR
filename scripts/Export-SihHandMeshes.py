"""Bake the vendored SiH glove into version-independent, grip-local mesh data.

Build-time only: UnityPy 1.20.26 and numpy. No Unity asset bundle loads in a game.
The output retains the upstream asset's GPL-3.0 licensing; see Assets notice.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

import numpy as np
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler


def trs(transform, override=None):
    p, s = transform.m_LocalPosition, transform.m_LocalScale
    q = transform.m_LocalRotation
    x, y, z, w = override or (q.x, q.y, q.z, q.w)
    norm = np.sqrt(x*x + y*y + z*z + w*w)
    x, y, z, w = np.array([x, y, z, w]) / norm
    matrix = np.eye(4)
    matrix[:3, :3] = np.array([
        [1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
        [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
        [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)],
    ]) @ np.diag([s.x, s.y, s.z])
    matrix[:3, 3] = [p.x, p.y, p.z]
    return matrix


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("bundle", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    env = UnityPy.load(str(args.bundle))
    transforms = {o.path_id: o.read() for o in env.objects if o.type.name == "Transform"}
    roots = {t.m_GameObject.read().m_Name: key for key, t in transforms.items()
             if t.m_GameObject.read().m_Name in ("LeftHand", "RightHand")}
    pose_text = next(o.read().m_Script for o in env.objects if o.type.name == "TextAsset"
                     and o.read().m_Name == "OpenXRHandPoses")
    poses = json.loads(pose_text.lstrip("\ufeff"))
    renderers = [o.read() for o in env.objects if o.type.name == "SkinnedMeshRenderer"]
    output = bytearray(struct.pack("<II", 0x31485648, 2))
    report = {"bundleSha256": hashlib.sha256(args.bundle.read_bytes()).hexdigest(), "hands": []}
    for side, root_name in (("left", "LeftHand"), ("right", "RightHand")):
        root = roots[root_name]

        def belongs(pointer):
            node = pointer.path_id
            while node:
                if node == root:
                    return True
                node = transforms[node].m_Father.path_id
            return False

        renderer = next(r for r in renderers if belongs(r.m_RootBone))
        mesh = renderer.m_Mesh.read()
        helper = MeshHandler(mesh)
        helper.process()
        positions = np.c_[np.asarray(helper.m_Vertices)[:, :3], np.ones(helper.m_VertexCount)]
        indices = np.array(helper.get_triangles()[0], dtype="<i4").flatten()
        weights = np.asarray(helper.m_BoneWeights)
        bones = np.asarray(helper.m_BoneIndices, dtype=int)
        bind = [np.array([[getattr(m, f"e{r}{c}") for c in range(4)] for r in range(4)])
                for m in mesh.m_BindPose]
        versions = []
        for grip, trigger in ((0, 0), (1, 0), (0, 1), (1, 1)):
            maps = {key: {b["bone"]: tuple(b[c] for c in "xyzw") for b in poses[side][key]}
                    for key in ("open", "fist", "indexCurl")}
            cache = {root: np.eye(4)}

            def world(node):
                if node in cache:
                    return cache[node]
                t = transforms[node]
                name = t.m_GameObject.read().m_Name
                key = "indexCurl" if trigger and ("index" in name or "thumb" in name) else (
                    "fist" if grip and "index" not in name else "open")
                cache[node] = world(t.m_Father.path_id) @ trs(t, maps[key].get(name))
                return cache[node]

            matrices = [world(p.path_id) @ b for p, b in zip(renderer.m_Bones, bind)]
            vertices = np.zeros((len(positions), 3))
            for column in range(weights.shape[1]):
                for bone, matrix in enumerate(matrices):
                    mask = (bones[:, column] == bone) & (weights[:, column] > 0)
                    vertices[mask] += (positions[mask] @ matrix.T)[:, :3] * weights[mask, column, None]
            assert np.isfinite(vertices).all()
            assert .06 < np.ptp(vertices, axis=0).max() < .5, "unexpected hand units"
            versions.append(vertices.astype("<f4"))
        assert indices.min() >= 0 and indices.max() < len(positions)
        output.extend(struct.pack("<II", len(positions), len(indices)))
        for vertices in versions:
            output.extend(vertices.tobytes())
        output.extend(indices.tobytes())
        report["hands"].append({"side": side, "vertices": len(positions), "triangles": len(indices)//3,
                                "min": versions[0].min(axis=0).tolist(), "max": versions[0].max(axis=0).tolist()})
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(output)
    report["meshSha256"] = hashlib.sha256(output).hexdigest()
    args.output.with_suffix(".json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
