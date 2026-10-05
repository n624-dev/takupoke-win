"""Exact frozen two-model condition; one engine per independent process."""
from copy import deepcopy
import hashlib
import json
from contract import CONDITIONS, PURPOSE

SCOPE_ACTION = 'ONE_LOCAL_NATIVE_ROW_CANDIDATE_COMPARISON'
MODELS = {'gemma-row-choice': ('gemma', 2588147712, '181938105e0eefd105961417e8da75903eacda102c4fce9ce90f50b97139a63c'),
          'qwen-row-choice': ('qwen', 344671744, '03e7da1eb1108b50dffaa9bb52cc7bcbad2eb0c66ca990267f480c1e545d2856')}


def validate_recipe(recipe):
    expected = MODELS.get(recipe.get('comparisonCondition'))
    if expected is None or tuple(recipe.get(k) for k in ('modelKey', 'modelBytes', 'modelSHA256')) != expected:
        raise RuntimeError('EXACT_EXISTING_MODEL_CONDITION_REQUIRED')
    required = {'maximumCalls': 1, 'maximumTasks': 1, 'maximumTotalComparisonCalls': 2,
                'executionStages': ['row-choice'], 'purpose': PURPOSE, 'visionSupported': False,
                'visionImagesPerCall': 0, 'modelLoadsMaximum': 1, 'maximumConcurrentEngines': 1,
                'modelDownloads': 2, 'newModelIdentities': 0, 'publicAssetFetchesMaximum': 3,
                'maximumHostedMemoryBytes': 16 * 1024**3,
                'maximumRSSBytes': 8 * 1024**3, 'diskReserveBytes': 2 * 1024**3,
                'minimumAvailableMemoryBytes': 10200547328, 'retries': 0,
                'newRecognizerCalls': 0, 'newImageCalls': 0, 'productionAdoption': False,
                'qualifiedGenAIModels': [], 'originalInkOwnershipProof': 'ABSENT',
                'roleAssignmentProof': 'ABSENT', 'contextTokens': 8192,
                'sampler': {'topK': 1, 'temperature': 0, 'seed': 17, 'thinking': False, 'maximumOutputTokens': 256}}
    if any(type(recipe.get(k)) is not type(v) or recipe[k] != v for k, v in required.items()):
        raise RuntimeError('EXACT_SINGLE_FIELD_SCOPE_REQUIRED')
    return ['row-choice']


def select_recipe(base, condition, configuration):
    if condition not in CONDITIONS or set(configuration) != {'conditions'} or set(configuration['conditions']) != set(CONDITIONS):
        raise RuntimeError('FROZEN_CONDITION_CONFIGURATION_REQUIRED')
    result = {**deepcopy(base), **deepcopy(configuration['conditions'][condition]), 'comparisonCondition': condition}
    validate_recipe(result)
    return result


def derive_packet(freeze, recipe, source_packet_sha, configuration_sha, recipe_bytes):
    validate_recipe(recipe)
    out = deepcopy(freeze)
    matches = [p for p in out['pins'] if p['path'] == 'recipe.json']
    if len(matches) != 1:
        raise RuntimeError('EXACT_RECIPE_PIN_REQUIRED')
    matches[0].update(bytes=len(recipe_bytes), sha256=hashlib.sha256(recipe_bytes).hexdigest())
    out.update(derivedFromSourcePacketSHA256=source_packet_sha, comparisonConfigurationSHA256=configuration_sha,
               comparisonCondition=recipe['comparisonCondition'], maximumInferenceCalls=1)
    return out
