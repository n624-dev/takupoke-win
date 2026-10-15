"""A tiny constant ONNX for route/confidence tests; never an accuracy model."""
from pathlib import Path
import onnx
from onnx import TensorProto, helper

root = Path(__file__).resolve().parents[2]
graph = helper.make_graph([
    helper.make_node('ConstantOfShape', ['shape'], ['empty']),
    helper.make_node('ScatterND', ['empty', 'indices', 'values'], ['probabilities']),
    helper.make_node('Identity', ['reject'], ['reject_probability']),
], 'manual-code-contract-only', [
    helper.make_tensor_value_info('x', TensorProto.FLOAT, ['B', 3, 48, 'W']),
    helper.make_tensor_value_info('valid_width', TensorProto.INT64, ['B']),
], [
    helper.make_tensor_value_info('probabilities', TensorProto.FLOAT, [1, 4, 438]),
    helper.make_tensor_value_info('reject_probability', TensorProto.FLOAT, [1]),
], [
    helper.make_tensor('shape', TensorProto.INT64, [3], [1, 4, 438]),
    helper.make_tensor('indices', TensorProto.INT64, [4, 3], [0, 0, 1, 0, 1, 2, 0, 2, 0, 0, 3, 3]),
    helper.make_tensor('values', TensorProto.FLOAT, [4], [.99, .7, 1, .99]),
    helper.make_tensor('reject', TensorProto.FLOAT, [1], [.01]),
])
model = helper.make_model(graph, opset_imports=[helper.make_opsetid('', 17)], ir_version=10)
onnx.checker.check_model(model)
onnx.save(model, root / 'tests/Takupoke.Integration.Tests/fixtures/ascii-manual-contract.onnx')
