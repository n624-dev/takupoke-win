"""Uniform LiteRT0.17.1 transport adaptation; semantic schema/decoder stays strict.

Actual run37377657259 reported LLGuidance uniqueItems unimplemented. Every
array's uniqueness remains mandatory in request_contract.decode; this adapter
only removes that unsupported native keyword, never source IDs or other rules.
"""
from copy import deepcopy
import hashlib,json
from contract import Refusal

def compact_sha(schema):return hashlib.sha256(json.dumps(schema,ensure_ascii=False,separators=(',',':')).encode()).hexdigest()
def adapt(schema):
    native=deepcopy(schema);removed=0
    def walk(node):
        nonlocal removed
        if type(node) is dict:
            if 'uniqueItems' in node:
                if node.get('type')!='array' or node['uniqueItems'] is not True:raise Refusal('UNSUPPORTED_NATIVE_UNIQUENESS_SHAPE')
                del node['uniqueItems'];removed+=1
            for value in node.values():walk(value)
        elif type(node) is list:
            for value in node:walk(value)
    walk(native)
    return native,{'nativeAdapter':'LITERT0171_UNIQUEITEMS_SEMANTIC_DECODER_ONLY_V1','semanticSchemaCompactSHA256':compact_sha(schema),'nativeSchemaCompactSHA256':compact_sha(native),'removedUniqueItemsCount':removed,'semanticUniquenessRequired':True,'sourceRoleMembershipOrderPartitionChanged':False}
