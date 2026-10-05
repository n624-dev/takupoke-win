"""Exact one-model, one-crop, two-call scope. No model imports."""
from copy import deepcopy
import hashlib
CONDITIONS=('gemma-localized-image',)
SCOPE_ACTION='ONE_LOCALIZED_IMAGE_CELL_COMPARISON'
def validate_recipe(r):
    required={'comparisonCondition':CONDITIONS[0],'modelKey':'gemma','modelBytes':2588147712,'modelSHA256':'181938105e0eefd105961417e8da75903eacda102c4fce9ce90f50b97139a63c',
    'maximumCalls':2,'maximumTasks':1,'maximumCrops':1,'maximumCropPixels':9024,'maximumCropEdgePixels':96,'executionStages':['blind-image','ocr-compare'],
    'visionSupported':True,'visionImagesPerCall':1,'bundleMaxVisionTokens':280,'modelLoadsMaximum':1,'maximumConcurrentEngines':1,
    'modelDownloads':1,'newModelIdentities':0,'publicAssetFetchesMaximum':2,'maximumHostedMemoryBytes':17179869184,
    'maximumRSSBytes':8589934592,'minimumAvailableMemoryBytes':10200547328,'diskReserveBytes':2147483648,
    'processDeadlineSeconds':360,'loadDeadlineSeconds':180,'callDeadlineSeconds':60,'retries':0,'newRecognizerCalls':0,'newImageCalls':1,
    'productionAdoption':False,'qualifiedGenAIModels':[],'contextTokens':8192,'maximumManualBodyCorrectionsDocumentWide':3,
    'headerClassStructureManualCorrectionAllowed':False,'confidenceThresholdChanged':False,'coreValidatorActuallyInvoked':False,
    'sampler':{'topK':1,'temperature':0,'seed':17,'thinking':False,'maximumOutputTokens':256}}
    if any(type(r.get(k)) is not type(v) or r[k]!=v for k,v in required.items()):raise RuntimeError('EXACT_LOCALIZED_IMAGE_SCOPE_REQUIRED')
    return r['executionStages']
def select_recipe(base,condition,config):
    if condition not in CONDITIONS or config!={'conditions':{CONDITIONS[0]:{}}}:raise RuntimeError('EXACT_CONDITION_REQUIRED')
    validate_recipe(base);return deepcopy(base)
def derive_packet(freeze,r,source_sha,config_sha,recipe_bytes):
    validate_recipe(r);out=deepcopy(freeze)
    for pin in out['pins']:
        if pin['path']=='recipe.json':pin.update(bytes=len(recipe_bytes),sha256=hashlib.sha256(recipe_bytes).hexdigest())
    out.update(derivedFromSourcePacketSHA256=source_sha,comparisonConfigurationSHA256=config_sha,comparisonCondition=r['comparisonCondition'],maximumInferenceCalls=2)
    return out
