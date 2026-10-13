"""Bounded text interpretation of the reader's trained HTML output protocol.

This never executes/renders HTML or treats a predicted bbox as original Evidence.
"""
from html.parser import HTMLParser
import html
import math
import re


class Refusal(ValueError): pass


class Blocks(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=False)
        self.stack = []; self.text = []; self.roots = 0; self.events = 0; self.boxes = []

    def charge(self):
        self.events += 1
        if self.events > 256: raise Refusal('HTML event budget exceeded')

    def handle_starttag(self, tag, attrs):
        self.charge()
        if tag not in {'div', 'p', 'span', 'b', 'strong', 'i', 'em', 'br'}:
            raise Refusal('Unsupported native HTML tag')
        keys = [k for k, _ in attrs]
        if len(keys) != len(set(keys)) or any(k not in {'data-bbox', 'data-label', 'class'} for k in keys):
            raise Refusal('Unsupported or duplicate native attributes')
        for key, value in attrs:
            if key == 'data-bbox':
                if value is None: raise Refusal('Missing bbox value')
                value = value.strip()
                if value.startswith('['):
                    if not value.endswith(']'): raise Refusal('Unbalanced bbox brackets')
                    value = value[1:-1]
                parts = re.fullmatch(r'\s*(-?\d+(?:\.\d+)?)\s*[, ]\s*(-?\d+(?:\.\d+)?)\s*[, ]\s*(-?\d+(?:\.\d+)?)\s*[, ]\s*(-?\d+(?:\.\d+)?)\s*', value)
                if not parts: raise Refusal('Unsupported bbox syntax')
                box = [float(x) for x in parts.groups()]
                if any(not math.isfinite(x) or x < 0 or x > 1000 for x in box) or box[2] <= box[0] or box[3] <= box[1]:
                    raise Refusal('Invalid predicted bbox')
                self.boxes.append(box)
        if tag == 'br':
            if not self.stack: raise Refusal('Line break outside block')
            self.text.append('\n'); return
        if not self.stack: self.roots += 1
        if len(self.stack) >= 16: raise Refusal('HTML nesting budget exceeded')
        self.stack.append(tag)

    def handle_endtag(self, tag):
        self.charge()
        if not self.stack or self.stack[-1] != tag: raise Refusal('Unbalanced native HTML')
        self.stack.pop()

    def handle_data(self, data):
        self.charge()
        if self.stack: self.text.append(data)
        elif data.strip(): raise Refusal('Text outside HTML block')

    def handle_entityref(self, name):
        self.charge()
        token = '&' + name + ';'; decoded = html.unescape(token)
        if decoded == token: raise Refusal('Unknown entity')
        self.handle_data(decoded)

    def handle_charref(self, name):
        self.charge()
        token = '&#' + name + ';'; decoded = html.unescape(token)
        if '\ufffd' in decoded or decoded == token: raise Refusal('Invalid scalar entity')
        self.handle_data(decoded)

    def handle_comment(self, data): raise Refusal('Unexpected native comment')
    def handle_decl(self, data): raise Refusal('Unexpected native declaration')
    def handle_pi(self, data): raise Refusal('Unexpected native processing instruction')
    def unknown_decl(self, data): raise Refusal('Unexpected native declaration')


def decode(text):
    if not isinstance(text, str) or len(text.encode('utf-8')) > 16384: raise Refusal('Native HTML length exceeded')
    parser = Blocks(); parser.feed(text); parser.close()
    if parser.stack or parser.roots != 1: raise Refusal('Expected one complete crop block')
    return {'text': ''.join(parser.text), 'predictedBoxes': parser.boxes, 'originalOwnershipProof': 'ABSENT'}
