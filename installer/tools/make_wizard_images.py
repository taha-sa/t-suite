"""Inno wizard gorselleri uretir (T-Suite stili: koyu zemin, vurgu rengi, ikon). Kullanim: python make_wizard_images.py"""
from PIL import Image, ImageDraw
import os
HERE = os.path.dirname(os.path.abspath(__file__))
ICONS = os.path.abspath(os.path.join(HERE, "..", "..", "design", "icons", "out"))
OUT = os.path.abspath(os.path.join(HERE, "..", "assets"))
BG, SURF = (0x0D, 0x0F, 0x12), (0x16, 0x19, 0x1E)
APPS = {  # ad: (ikon klasoru, vurgu)
    "t-ramcash": ("t-ramcash", "#C084FC"), "t-downloader": ("t-downloader", "#38BDF8"),
    "t-desktree": ("t-desktree", "#2DD4BF"), "t-suite": ("t-suite", "#E3A92B"),
}
def hexrgb(h): return tuple(int(h[i:i+2], 16) for i in (1, 3, 5))
def make(name, icon_dir, accent, scale):
    acc = hexrgb(accent)
    w, h = 164 * scale, 314 * scale
    img = Image.new("RGB", (w, h), BG)
    d = ImageDraw.Draw(img)
    for y in range(h):  # alttan vurgu rengine hafif gecis
        t = max(0.0, (y - h * 0.45) / (h * 0.55)) ** 2 * 0.28
        d.line([(0, y), (w, y)], fill=tuple(int(BG[i] * (1 - t) + acc[i] * t) for i in range(3)))
    d.rectangle([0, 0, 4 * scale - 1, h], fill=acc)  # sol vurgu seridi
    ico = Image.open(os.path.join(ICONS, icon_dir, "256.png")).convert("RGBA")
    s = 112 * scale
    ico = ico.resize((s, s), Image.LANCZOS)
    img.paste(ico, ((w - s) // 2 + 2 * scale, 60 * scale), ico)
    d.rounded_rectangle([40 * scale, 200 * scale, w - 28 * scale, 203 * scale], radius=scale, fill=acc)
    img.save(os.path.join(OUT, f"{name}-large{'' if scale == 1 else '@2x'}.bmp"))
    # kucuk (55x55 / 110x110)
    sw = 55 * scale
    sm = Image.new("RGB", (sw, sw), SURF)
    i2 = Image.open(os.path.join(ICONS, icon_dir, "64.png")).convert("RGBA").resize((44 * scale, 44 * scale), Image.LANCZOS)
    sm.paste(i2, (5 * scale + scale // 2, 5 * scale + scale // 2), i2)
    sm.save(os.path.join(OUT, f"{name}-small{'' if scale == 1 else '@2x'}.bmp"))
os.makedirs(OUT, exist_ok=True)
for n, (idir, acc) in APPS.items():
    for sc in (1, 2): make(n, idir, acc, sc)
# Viewer QR (Telefon icin sayfasi)
try:
    import qrcode, sys
    url = sys.argv[1] if len(sys.argv) > 1 else "https://github.com/taha-sa/tdesk-viewer-releases/releases/latest"
    q = qrcode.QRCode(border=2, box_size=6); q.add_data(url); q.make()
    q.make_image(fill_color="black", back_color="white").convert("RGB").save(os.path.join(OUT, "viewer-qr.bmp"))
except ImportError:
    print("qrcode yok: viewer-qr.bmp uretilmedi (pip install qrcode)")
print("ok")
