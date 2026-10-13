"""Maakt mde.ico in de stijl van de favicon (wit vlak, zwarte MDE, rode balk). Vereist Pillow."""
from PIL import Image, ImageDraw, ImageFont

beelden = []
for s in (16, 32, 48, 64, 128, 256):
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=s // 6, fill=(255, 255, 255, 255))
    d.rectangle([s * 0.12, s * 0.72, s * 0.88, s * 0.84], fill=(200, 16, 46, 255))
    try:
        f = ImageFont.truetype("arialbd.ttf", int(s * 0.42))
    except OSError:
        f = ImageFont.load_default()
    bb = d.textbbox((0, 0), "MDE", font=f)
    w, h = bb[2] - bb[0], bb[3] - bb[1]
    d.text(((s - w) / 2 - bb[0], s * 0.40 - h / 2 - bb[1]), "MDE", font=f, fill=(0, 0, 0, 255))
    beelden.append(im)

beelden[-1].save("mde.ico", sizes=[(b.width, b.height) for b in beelden], append_images=beelden[:-1])
print("mde.ico gemaakt")
