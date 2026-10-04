#!/usr/bin/env python3
"""Apercu du filtre CGA sur une capture d'ecran, sans lancer de jeu.

Reproduit les trois passes des shaders (cga-prepare, cga, cga-output).
Le preset moderne utilise cga-v2.slang (deux passes) : meme calcul, avec
les reglages par defaut.
La moyenne par cellule est faite avec un redimensionnement BOX en lumiere
lineaire, equivalent a l'echantillonnage 8x8 du shader.

    python3 tools/preview.py capture.png sortie.png --palette 0
    python3 tools/preview.py capture.png sortie.png --preset moderne
"""
import argparse

import numpy as np
from PIL import Image

L0, L1, L2, L3 = 0.0, 1 / 3, 2 / 3, 1.0
CGA16 = np.array([
    [L0, L0, L0], [L0, L0, L2], [L0, L2, L0], [L0, L2, L2],
    [L2, L0, L0], [L2, L0, L2], [L2, L1, L0], [L2, L2, L2],
    [L1, L1, L1], [L1, L1, L3], [L1, L3, L1], [L1, L3, L3],
    [L3, L1, L1], [L3, L1, L3], [L3, L3, L1], [L3, L3, L3],
], dtype=np.float64)

PAL4 = {
    0: [0, 11, 13, 15],
    1: [0, 3, 5, 7],
    2: [0, 10, 12, 14],
    3: [0, 2, 4, 6],
    4: [0, 11, 12, 15],
    6: [0, 8, 7, 15],
}

BAYER4 = np.array([
    [0, 8, 2, 10],
    [12, 4, 14, 6],
    [3, 11, 1, 9],
    [15, 7, 13, 5],
], dtype=np.float64)

LUMA = np.array([0.299, 0.587, 0.114])

M1 = np.array([
    [0.4122214708, 0.5363325363, 0.0514459929],
    [0.2119034982, 0.6806995451, 0.1073969566],
    [0.0883024619, 0.2817188376, 0.6299787005],
])
M2 = np.array([
    [0.2104542553, 0.7936177850, -0.0040720468],
    [1.9779984951, -2.4285922050, 0.4505937099],
    [0.0259040371, 0.7827717662, -0.8086757660],
])

DEFAULTS = dict(res="320x200", palette=0, bg=0, luma_weight=0.0, levels=16,
                penalty=0.1, outline=0.0, brightness=0.0, contrast=1.0,
                saturation=1.0, stability=0.0, aspect43=0)

# Memes valeurs que les fichiers shaders/cga-*.slangp
PRESETS = {
    "moderne": dict(res="320x200"),
    "lisible": dict(res="320x216", luma_weight=0.35, penalty=0.2, levels=8,
                    outline=0.5, brightness=0.04, saturation=1.3,
                    stability=0.03),
    "authentique": dict(res="320x200", levels=2, penalty=0.0, aspect43=1),
}


def to_linear(c):
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def to_srgb(c):
    c = np.maximum(c, 0)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def oklab(srgb):
    return np.cbrt(np.maximum(to_linear(srgb) @ M1.T, 0)) @ M2.T


def lab_dist(a, b, chroma):
    d = a - b
    return d[..., 0] ** 2 + chroma * (d[..., 1] ** 2 + d[..., 2] ** 2)


def proj_space(c, chroma):
    return np.concatenate([c * np.sqrt(chroma), (c @ LUMA)[..., None]], -1)


def prepare(img, size, brightness, contrast, saturation, stability, prev=None):
    """Passe 1 : moyenne en lumiere lineaire, reglages, stabilite."""
    lin = to_linear(np.asarray(img.convert("RGB"), dtype=np.float64) / 255)
    chans = [np.asarray(Image.fromarray(lin[..., k].astype(np.float32), "F")
                        .resize(size, Image.BOX)) for k in range(3)]
    c = to_srgb(np.stack(chans, -1).astype(np.float64))

    c = (c - 0.5) * contrast + 0.5 + brightness
    y = (c @ LUMA)[..., None]
    c = np.clip(y + (c - y) * saturation, 0, 1)

    if stability > 0 and prev is not None:
        keep = np.abs(c - prev).max(-1) < stability
        c = np.where(keep[..., None], prev, c)
    # le shader ecrit dans une texture 8 bits
    return np.round(c * 255) / 255


def palette_colors(palette, bg):
    if palette == 5:
        return CGA16.copy()
    idx = [0, 15] if palette == 7 else list(PAL4[palette])
    colors = CGA16[idx].copy()
    colors[0] = CGA16[bg]
    return colors


def quantize(c, palette, bg, luma_weight, levels, penalty, outline):
    """Passe 2 : contours, tramage de Yliluoma, Bayer."""
    h, w = c.shape[:2]
    chroma = 1.0 - luma_weight
    colors = palette_colors(palette, bg)
    labs = oklab(colors)
    darkest = colors[labs[:, 0].argmin()]

    target = oklab(c)
    x = proj_space(c, chroma)
    best_err = np.full((h, w), np.inf)
    best_a = np.zeros_like(c)
    best_b = np.zeros_like(c)
    best_r = np.zeros((h, w))

    for i in range(len(colors)):
        pa = proj_space(colors[i], chroma)
        for j in range(i, len(colors)):
            d = proj_space(colors[j], chroma) - pa
            dd = d @ d
            r = np.clip((x - pa) @ d / dd, 0, 1) if dd > 1e-8 else np.zeros((h, w))
            r = np.floor(r * levels + 0.5) / levels

            mixed = colors[i] + r[..., None] * (colors[j] - colors[i])
            err = lab_dist(target, oklab(mixed), chroma)
            pen = penalty * lab_dist(labs[i], labs[j], chroma) * (np.abs(r - 0.5) + 0.5)
            err = err + np.where((r > 0) & (r < 1), pen, 0)

            better = err < best_err
            best_err = np.where(better, err, best_err)
            best_a[better] = colors[i]
            best_b[better] = colors[j]
            best_r = np.where(better, r, best_r)

    yy, xx = np.mgrid[0:h, 0:w]
    threshold = (BAYER4[yy % 4, xx % 4] + 0.5) / 16
    out = np.where((best_r > threshold)[..., None], best_b, best_a)

    if outline > 0:
        lum = np.pad(c @ LUMA, 1, mode="edge")
        n = lambda dy, dx: lum[1 + dy:1 + dy + h, 1 + dx:1 + dx + w]
        gx = (n(-1, 1) + 2 * n(0, 1) + n(1, 1)) - (n(-1, -1) + 2 * n(0, -1) + n(1, -1))
        gy = (n(1, -1) + 2 * n(1, 0) + n(1, 1)) - (n(-1, -1) + 2 * n(-1, 0) + n(-1, 1))
        edge = np.hypot(gx, gy) * 0.25
        mean = sum(n(dy, dx) for dy in (-1, 0, 1) for dx in (-1, 0, 1)
                   if dy or dx) / 8
        mask = (edge > 0.4 - 0.35 * outline) & (c @ LUMA < mean)
        out[mask] = darkest
    return out


def output(small, out_size, aspect43):
    """Passe 3 : agrandissement sans lissage, 4:3 en option."""
    img = Image.fromarray((small * 255).round().astype(np.uint8))
    if not aspect43:
        return img.resize(out_size, Image.NEAREST)
    w, h = out_size
    area = (round(h * 4 / 3), h) if w / h > 4 / 3 else (w, round(w * 3 / 4))
    canvas = Image.new("RGB", out_size)
    canvas.paste(img.resize(area, Image.NEAREST),
                 ((w - area[0]) // 2, (h - area[1]) // 2))
    return canvas


def cga(img, prev=None, **opts):
    o = {**DEFAULTS, **opts}
    size = tuple(int(v) for v in o["res"].lower().split("x"))
    c = prepare(img, size, o["brightness"], o["contrast"], o["saturation"],
                o["stability"], prev)
    small = quantize(c, o["palette"], o["bg"], o["luma_weight"], o["levels"],
                     o["penalty"], o["outline"])
    return output(small, img.size, o["aspect43"]), c


def main():
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("input")
    p.add_argument("output")
    p.add_argument("--preset", choices=PRESETS, help="part des valeurs d'un preset")
    p.add_argument("--res", help="ex. 320x200")
    p.add_argument("--palette", type=int, choices=range(8))
    p.add_argument("--bg", type=int, choices=range(16), help="couleur de fond CGA")
    p.add_argument("--luma-weight", type=float, help="0 = couleur, 1 = luminosite")
    p.add_argument("--levels", type=int, help="niveaux de tramage, 1 = aucun")
    p.add_argument("--penalty", type=float, help="douceur du tramage")
    p.add_argument("--outline", type=float, help="contours, 0 = aucun")
    p.add_argument("--brightness", type=float)
    p.add_argument("--contrast", type=float)
    p.add_argument("--saturation", type=float)
    p.add_argument("--aspect43", type=int, choices=(0, 1))
    a = vars(p.parse_args())

    opts = dict(PRESETS.get(a.pop("preset"), {}))
    src, dst = a.pop("input"), a.pop("output")
    opts.update({k: v for k, v in a.items() if v is not None})
    cga(Image.open(src), **opts)[0].save(dst)


if __name__ == "__main__":
    main()
