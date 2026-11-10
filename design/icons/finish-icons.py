"""Validate rendered pixels and pack exact Edge PNGs in a multi-frame ICO."""
import sys, json, struct, math
from pathlib import Path
if len(sys.argv) > 2 and sys.argv[2]:
    sys.path.insert(0, sys.argv[2])
from PIL import Image
root = Path(sys.argv[1]).resolve()
jobs = json.loads((root/'out'/'render-jobs.json').read_text())
for job in jobs:
    with Image.open(job['png']) as im:
        assert im.size == (job['size'], job['size']), job['png']
        im = im.convert('RGBA')
        alpha = im.getchannel('A')
        assert alpha.getbbox(), f"Empty render: {job['png']}"
        if 'foreground' in job['png']:
            n = im.width
            for y in range(n):
                for x in range(n):
                    if alpha.getpixel((x,y)):
                        assert math.hypot(x+.5-n/2,y+.5-n/2) <= n/3+1, f"Outside Android safe circle: {job['png']}"
        elif 'background' in job['png'] or 'play-512' in job['png']:
            assert alpha.getextrema() == (255,255), job['png']
        else:
            # At 16px the rounded corner can retain one alpha unit of antialiasing.
            assert alpha.getpixel((0,0)) <= 2, job['png']
            assert alpha.getpixel((im.width//2,im.height//2)) == 255, job['png']
            n=im.width
            assert all(alpha.getpixel(p) >= 240 for p in ((n//2,0),(n//2,n-1),(0,n//2),(n-1,n//2))), f"Clipped tile: {job['png']}"
for app in sorted(p for p in (root/'out').iterdir() if p.is_dir()):
    sizes = (16,20,24,32,48,64,128,256)
    payloads = [(app/f'{n}.png').read_bytes() for n in sizes]
    offset = 6+16*len(sizes)
    header = struct.pack('<HHH',0,1,len(sizes))
    for n, data in zip(sizes,payloads):
        header += struct.pack('<BBBBHHII', n % 256,n % 256,0,0,1,32,len(data),offset)
        offset += len(data)
    (app/'app.ico').write_bytes(header+b''.join(payloads))
    with Image.open(app/'app.ico') as ico:
        assert ico.ico.sizes() == {(n,n) for n in sizes}
        for n in sizes:
            assert ico.ico.getimage((n,n)).size == (n,n)
    xml = '<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android"><background android:drawable="@mipmap/ic_launcher_background"/><foreground android:drawable="@mipmap/ic_launcher_foreground"/></adaptive-icon>\n'
    target=app/'android/mipmap-anydpi-v26/ic_launcher.xml'
    target.parent.mkdir(parents=True,exist_ok=True); target.write_text(xml,encoding='utf-8')
print(f'Validated {len(jobs)} PNGs and 6 ICOs.')
