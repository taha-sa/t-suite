"""Prepare tiny HTML wrappers; rasterization is exclusively Microsoft Edge."""
import sys, json, re, math
from pathlib import Path
import xml.etree.ElementTree as ET
if len(sys.argv) > 2 and sys.argv[2]:
    sys.path.insert(0, sys.argv[2])  # Explicit existing Pillow location; no install.
from PIL import Image
root = Path(sys.argv[1]).resolve()
ns = '{http://www.w3.org/2000/svg}'
ET.register_namespace('', ns[1:-1])
jobs = []
sizes = (16, 20, 24, 32, 48, 64, 128, 256, 512, 1024)
densities = dict(mdpi=108, hdpi=162, xhdpi=216, xxhdpi=324, xxxhdpi=432)
def render(tree, app, name, size):
    out = root / 'out' / app
    png = out / name
    png.parent.mkdir(parents=True, exist_ok=True)
    html = out / '_render' / (name.replace('/', '-') + '.html')
    html.parent.mkdir(parents=True, exist_ok=True)
    tree.set('width', str(size)); tree.set('height', str(size))
    svg = ET.tostring(tree, encoding='unicode')
    html.write_text(f'<!doctype html><meta charset="utf-8"><style>html,body{{margin:0;width:100%;height:100%;background:transparent;overflow:hidden}}svg{{display:block;position:absolute;left:0;top:0;width:{size}px;height:{size}px}}</style>{svg}', encoding='utf-8')
    jobs.append(dict(html=str(html), png=str(png), size=size))
def clone(svg):
    return ET.fromstring(ET.tostring(svg))
for source in sorted(root.glob('*.svg')):
    svg = ET.parse(source).getroot()
    app = source.stem
    for size in sizes:
        tree = clone(svg)
        if size <= 24:
            for child in list(tree):
                if child.get('data-role') in ('notch', 'detail'):
                    tree.remove(child)
            for elem in tree.iter():
                if elem.get('stroke-width') == '64':
                    elem.set('stroke-width', str(2 * 1024 / size))
            if app == 't-desktree':
                # Shared dividers avoid overlapping independent cell outlines at 16px.
                for child in list(tree):
                    if child.tag != ns+'defs' and child.get('data-role') != 'tile':
                        tree.remove(child)
                attrs = {'stroke':'#FFFFFF','stroke-width':str(2*1024/size),
                         'stroke-linecap':'round','stroke-linejoin':'round','fill':'none'}
                ET.SubElement(tree,ns+'rect',dict(attrs,x='240',y='240',width='544',height='544',rx='32'))
                ET.SubElement(tree,ns+'path',dict(attrs,d='M 512 240 V 784 M 512 512 H 784'))
        render(tree, app, f'{size}.png', size)
    # Adaptive: no tile rounding, no notch; fit ALL painted foreground bounds
    # into a 72dp DIAMETER circle (not merely a 72dp square).
    bg = clone(svg)
    for child in list(bg):
        if child.tag != ns+'defs' and child.get('data-role') != 'tile':
            bg.remove(child)
        if child.get('data-role') == 'tile':
            child.set('rx','0'); child.set('ry','0')
    fg = clone(svg)
    for child in list(fg):
        if child.get('data-role') in ('tile', 'notch', 'detail'):
            fg.remove(child)
    group = ET.Element(ns+'g', transform='translate(512 512) scale(0.72) translate(-512 -512)')
    for child in list(fg):
        if child.tag != ns+'defs':
            fg.remove(child); group.append(child)
    fg.append(group)
    for density, size in densities.items():
        render(clone(bg), app, f'android/mipmap-{density}/ic_launcher_background.png', size)
        render(clone(fg), app, f'android/mipmap-{density}/ic_launcher_foreground.png', size)
    # Play listing is a full-bleed square; Play applies its own corner mask.
    play = clone(svg)
    for child in play:
        if child.get('data-role') == 'tile':
            child.set('rx','0'); child.set('ry','0')
    render(play, app, 'play-512.png', 512)
(root/'out'/'render-jobs.json').write_text(json.dumps(jobs, indent=2)+'\n', encoding='utf-8')
