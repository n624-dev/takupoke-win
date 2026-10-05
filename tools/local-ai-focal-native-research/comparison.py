"""One fixed caller-purpose bundle; no oracle or runtime imports."""
import copy
import hashlib
import json
CONDITIONS=('gemma-focal',)
SCOPE_ACTION='ONE_LOCAL_FOCAL_REQUEST_COMPONENT_COMPARISON'
GEMMA_SHA='181938105e0eefd105961417e8da75903eacda102c4fce9ce90f50b97139a63c'
def validate_recipe(recipe):
    if recipe.get('comparisonCondition')!='gemma-focal' or recipe.get('modelSHA256')!=GEMMA_SHA or recipe.get('modelBytes')!=2588147712:
        raise RuntimeError('EXACT_FOCAL_MODEL_CONDITION_REQUIRED')
    if recipe.get('maximumCalls')!=16 or recipe.get('maximumTasks')!=4 or recipe.get('executionStages')!=['text','compare'] or recipe.get('visionSupported') is not False or recipe.get('purpose')!='BODY_ASSIGNMENT_PROPOSAL':
        raise RuntimeError('EXACT_FOCAL_SCOPE_REQUIRED')
    return ['text','compare']
def select_recipe(base,condition,configuration):
    if condition!='gemma-focal' or configuration!={'conditions':{'gemma-focal':{}}}:raise RuntimeError('FROZEN_FOCAL_CONFIGURATION_REQUIRED')
    validate_recipe(base);return copy.deepcopy(base)
def derive_packet(freeze,selected_recipe,source_packet_sha,configuration_sha,recipe_bytes):
    out=copy.deepcopy(freeze)
    matches=[p for p in out['pins'] if p['path']=='recipe.json']
    if len(matches)!=1:raise RuntimeError('EXACT_RECIPE_PIN_REQUIRED')
    matches[0].update(bytes=len(recipe_bytes),sha256=hashlib.sha256(recipe_bytes).hexdigest())
    out.update(derivedFromSourcePacketSHA256=source_packet_sha,comparisonConfigurationSHA256=configuration_sha,comparisonCondition='gemma-focal',maximumInferenceCalls=16)
    return out
