"""Builds the fir branch atlas for Oxfall's pines from the CC0 fir twig photos of Poly Haven's
fir_tree_01 (Assets/Oxfall/Art/Textures/fir_tree_01/fir_tree_01_twig.png).

The twig atlas holds a few separate sprays, too sparse to use one per branch. This lays many of
them along a stem into two dense branch cards, side by side in one 512 x 512 image: the left half
and the right half are each one branch, stem at the bottom, tip at the top.

Run: python3 -I tools/art/make_fir_branch.py <twig atlas png> <output png>
"""
import math
import random
import sys

from PIL import Image, ImageChops, ImageDraw

# Spray boxes in the twig atlas (u0, v0, u1, v1), stems at the bottom; measured from its alpha.
SPRAYS = [
    (0.305, 0.221, 0.639, 0.598),
    (0.639, 0.166, 0.957, 0.547),
    (0.652, 0.627, 0.940, 0.959),
    (0.210, 0.685, 0.432, 0.951),
]


def crop_sprays(atlas):
    w, h = atlas.size
    crops = []
    for u0, v0, u1, v1 in SPRAYS:
        box = (int(u0 * w), int((1 - v1) * h), int(u1 * w), int((1 - v0) * h))
        crops.append(atlas.crop(box))
    return crops


def place(canvas, spray, base, angle_deg, length):
    """Pastes a spray with its stem end at base, pointing angle_deg from straight up, length px long."""
    scale = length / spray.size[1]
    s = spray.resize((max(1, int(spray.size[0] * scale)), max(1, int(spray.size[1] * scale))), Image.LANCZOS)
    # Put the stem end (bottom centre) at the centre of a square, so rotating keeps it fixed.
    size = 2 * max(s.size) + 4
    square = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    square.paste(s, (size // 2 - s.size[0] // 2, size // 2 - s.size[1]), s)
    square = square.rotate(-angle_deg, resample=Image.BICUBIC)
    canvas.alpha_composite(square, (int(base[0] - size // 2), int(base[1] - size // 2)))


def branch(crops, rnd, width, height):
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)
    cx = width / 2
    top = 18
    # The stem, tapering towards the tip, with a slight curve.
    bow = rnd.uniform(-6, 6)
    points = [(cx + bow * math.sin(math.pi * i / 20), height - (height - top) * i / 20) for i in range(21)]
    for i in range(20):
        w = 5 - 3.5 * i / 20
        draw.line([points[i], points[i + 1]], fill=(74, 56, 40, 255), width=max(1, int(round(w))))

    def stem_at(t):
        f = t * 20
        i = min(19, int(f))
        a, b = points[i], points[i + 1]
        k = f - i
        return (a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k)

    # Side sprays, alternating, shorter towards the tip; then a few along the stem to fill the middle.
    n = 20
    for i in range(n):
        t = 0.04 + 0.9 * i / (n - 1)
        side = -1 if i % 2 else 1
        angle = side * rnd.uniform(36, 52)
        length = (height * 0.31) * (1.0 - 0.5 * t) * rnd.uniform(0.85, 1.1)
        place(canvas, rnd.choice(crops), stem_at(t), angle, length)
    for i in range(6):
        t = 0.1 + 0.8 * i / 5
        place(canvas, rnd.choice(crops), stem_at(t), rnd.uniform(-14, 14),
              (height * 0.3) * (1.0 - 0.5 * t))
    # The tip.
    place(canvas, crops[0], stem_at(0.86), rnd.uniform(-6, 6), height * 0.17)

    # Shade towards the trunk end: the inside of a crown gets less light. The gradient is 1.0 at
    # the tip (top) and 0.72 at the trunk (bottom).
    r, g, b, a = canvas.split()
    shade = Image.linear_gradient("L").resize((width, height)).point(lambda v: 255 - int(v * 0.28))
    rgb = ImageChops.multiply(Image.merge("RGB", (r, g, b)), Image.merge("RGB", (shade, shade, shade)))
    canvas = Image.merge("RGBA", (*rgb.split(), a))
    return canvas


def main():
    atlas = Image.open(sys.argv[1]).convert("RGBA")
    crops = crop_sprays(atlas)
    out = Image.new("RGBA", (512, 512), (0, 0, 0, 0))
    for k in range(2):
        out.alpha_composite(branch(crops, random.Random(11 + k), 256, 512), (256 * k, 0))
    # Transparent pixels take the average needle colour, so filtering never bleeds a halo.
    px = [p for p in out.get_flattened_data() if p[3] > 200] if hasattr(out, "get_flattened_data") \
        else [p for p in out.getdata() if p[3] > 200]
    avg = tuple(sum(p[i] for p in px) // len(px) for i in range(3))
    bg = Image.new("RGBA", out.size, avg + (0,))
    bg.alpha_composite(out)
    alpha = out.split()[3]
    bg.putalpha(alpha)
    bg.save(sys.argv[2])
    hist = alpha.histogram()
    print("coverage %.2f" % (sum(hist[128:]) / (512 * 512)), "average", avg)


if __name__ == "__main__":
    main()
