"""Explicit namespace import avoids older reproducibility module name collisions."""
import importlib.util
from pathlib import Path

_spec=importlib.util.spec_from_file_location('height_reproduction_reference',Path(__file__).with_name('reproduce.py'))
_reference=importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_reference)
ROOT,RESULTS,MODEL,MODEL_SHA,DICT,PREPOOL,FEATURE,CONTEXT,SHORTCUT=(_reference.ROOT,_reference.RESULTS,_reference.MODEL,_reference.MODEL_SHA,_reference.DICT,_reference.PREPOOL,_reference.FEATURE,_reference.CONTEXT,_reference.SHORTCUT)
ancestor,session,save=(_reference.ancestor,_reference.session,_reference.save)
