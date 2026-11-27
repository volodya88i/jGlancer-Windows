"""Stores an ONNX model's large float weights as float16 (half the download) while it still computes in float32: each
such weight gets a Cast back to float32 at the start of the graph (W-85). Constants that only feed a Cos or Sin (angle
tables) stay float32 (W-120).

    python scripts/stems/half_weights.py <model.onnx> <output.onnx>
"""

import hashlib
import sys
from pathlib import Path

import numpy as np
import onnx
from onnx import helper, numpy_helper


RESHAPING = {"Unsqueeze", "Squeeze", "Reshape", "Expand", "Tile", "Concat", "Flatten", "Transpose", "Identity"}


def angles(graph):
    """Initializers that reach a Cos or Sin through reshaping only: positions times frequencies (a RoFormer's rotary
    tables, up to 800 radians), which float16 would round by up to a quarter radian. They stay float32."""
    consumers = {}
    for node in graph.node:
        for name in node.input:
            consumers.setdefault(name, []).append(node)

    def reaches(name, seen):
        for node in consumers.get(name, []):
            if node.op_type in ("Cos", "Sin"):
                return True
            if node.op_type in RESHAPING and node.name not in seen:
                seen.add(node.name)
                if any(reaches(out, seen) for out in node.output):
                    return True
        return False

    return {weight.name for weight in graph.initializer if reaches(weight.name, set())}


def main(source, target):
    model = onnx.load(source)
    graph = model.graph
    keep = angles(graph)
    kept, casts = [], []
    for weight in graph.initializer:
        if weight.data_type == onnx.TensorProto.FLOAT and np.prod(weight.dims) >= 1024 and weight.name not in keep:
            values = numpy_helper.to_array(weight).astype(np.float16)
            half = numpy_helper.from_array(values, weight.name + "_half")
            kept.append(half)
            casts.append(helper.make_node("Cast", [half.name], [weight.name], to=onnx.TensorProto.FLOAT))
        else:
            kept.append(weight)
    del graph.initializer[:]
    graph.initializer.extend(kept)
    nodes = list(graph.node)
    del graph.node[:]
    graph.node.extend(casts + nodes)
    onnx.checker.check_model(model)
    onnx.save(model, target)
    data = Path(target).read_bytes()
    print(f"{target}: {len(data) / 1e6:.1f} MB, {len(casts)} weights halved, {len(keep)} angle tables kept, sha256 {hashlib.sha256(data).hexdigest()}, "
          f"{len(data)} bytes")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
