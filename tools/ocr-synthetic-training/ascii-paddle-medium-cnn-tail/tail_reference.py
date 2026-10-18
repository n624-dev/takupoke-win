"""Explicit local reconstruction reference, avoiding older basename collisions."""
from pathlib import Path
import importlib.util
_spec=importlib.util.spec_from_file_location('cnn_tail_reproduction',Path(__file__).with_name('reproduce.py'))
_ref=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(_ref)
MODEL,MODEL_SHA,DICT,TAILINPUT,FEATURE,ancestor,session=(_ref.MODEL,_ref.MODEL_SHA,_ref.DICT,_ref.TAILINPUT,_ref.FEATURE,_ref.ancestor,_ref.session)
