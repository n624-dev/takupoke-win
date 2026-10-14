#!/usr/bin/env python3
"""Generate a tiny constant ONNX contract fixture, never an accuracy model."""
from pathlib import Path
import onnx
from onnx import TensorProto, helper

root = Path(__file__).resolve().parents[2]
shape = helper.make_tensor('shape', TensorProto.INT64, [3], [1, 4, 438])
indices = helper.make_tensor('indices', TensorProto.INT64, [4, 3],
                             [0, 0, 1, 0, 1, 2, 0, 2, 0, 0, 3, 437])
values = helper.make_tensor('values', TensorProto.FLOAT, [4], [.98, .751, 1, .96])
reject = helper.make_tensor('reject', TensorProto.FLOAT, [1], [.0123])
graph = helper.make_graph([
    helper.make_node('ConstantOfShape', ['shape'], ['empty']),
    helper.make_node('ScatterND', ['empty', 'indices', 'values'], ['probabilities']),
    helper.make_node('Identity', ['reject'], ['reject_probability']),
], 'constant-ascii-contract-only', [
    helper.make_tensor_value_info('x', TensorProto.FLOAT, ['B', 3, 48, 'W']),
    helper.make_tensor_value_info('valid_width', TensorProto.INT64, ['B']),
], [
    helper.make_tensor_value_info('probabilities', TensorProto.FLOAT, [1, 4, 438]),
    helper.make_tensor_value_info('reject_probability', TensorProto.FLOAT, [1]),
], [shape, indices, values, reject])
model = helper.make_model(graph, opset_imports=[helper.make_opsetid('', 17)], ir_version=10)
onnx.checker.check_model(model)
onnx.save(model, root / 'tests/Takupoke.Integration.Tests/fixtures/ascii-runtime-contract.onnx')
