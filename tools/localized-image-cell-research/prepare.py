"""Freeze model-free source; execution still needs exact root approval."""
import json
from pathlib import Path
from protocol import digest
ROOT=Path(__file__).resolve().parent

def main():
    paths=sorted(p for p in ROOT.rglob('*') if p.is_file() and '__pycache__' not in p.parts and p.name!='packet-freeze.json')
    pins=[{'path':str(p.relative_to(ROOT)),'bytes':p.stat().st_size,'sha256':digest(p)} for p in paths]
    worker_names={'worker.py','guard.py','network_guard.py','comparison.py','contract.py','source_contract.py','native_grammar.py','protocol.py','image_protocol.py','prompt.txt','runtime-identity.json','recipe.json','image-input.json','crops/t000466d2ba598e3e.png'}
    recipe=json.loads((ROOT/'recipe.json').read_text())
    freeze={'version':1,'status':'SOURCE_PREPARED_REQUIRES_EXACT_ROOT_REVIEW','sourceBase':'a3666ab690b5a522964335b433cd692329481a07',
            'derivedFromSourcePacketSHA256':digest(ROOT.parents[0]/'local-ai-single-field-research/packet-freeze.json'),
            'comparisonConfigurationSHA256':digest(ROOT/'comparison-config.json'),'comparisonCondition':recipe['comparisonCondition'],
            'maximumInferenceCalls':2,'maximumConcurrentEngines':1,'productionAdoption':False,'qualifiedGenAIModels':[],
            'workflowSHA256':digest(ROOT.parents[1]/'.github/workflows/localized-image-cell-research.yml'),
            'workerPinPaths':sorted(worker_names),'pins':pins,'newModelIdentities':0,'sourcePreparationEngineCalls':0,'sourcePreparationModelDownloads':0}
    (ROOT/'packet-freeze.json').write_text(json.dumps(freeze,indent=2)+'\n')
    print(json.dumps({'packetSHA256':digest(ROOT/'packet-freeze.json'),'recipeSHA256':digest(ROOT/'recipe.json'),
        'modelLoads':0,'modelDownloads':0,'engineCalls':0,'rootExecutionGo':False}))
if __name__=='__main__':main()
