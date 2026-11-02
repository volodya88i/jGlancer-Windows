"""Stores an ONNX model's large float weights as float16 (half the download) while it still computes in float32: each
such weight gets a Cast back to float32 at the start of the graph (W-85).

    python scripts/stems/half_weights.py <model.onnx> <output.onnx>
"""

import hashlib
import sys
from pathlib import Path

import numpy as np
import onnx
from onnx import helper, numpy_helper


def main(source, target):
    model = onnx.load(source)
    graph = model.graph
    kept, casts = [], []
    for weight in graph.initializer:
        if weight.data_type == onnx.TensorProto.FLOAT and np.prod(weight.dims) >= 1024:
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
    print(f"{target}: {len(data) / 1e6:.1f} MB, {len(casts)} weights halved, sha256 {hashlib.sha256(data).hexdigest()}, "
          f"{len(data)} bytes")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
