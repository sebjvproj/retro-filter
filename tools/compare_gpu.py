#!/usr/bin/env python3
"""Verifie la sortie GPU de l'app contre le simulateur, reglages par defaut (v2).

Prend un dossier produit par `RetroFilter.exe --snapshot "<titre>" <dossier>`
(source.png et cga-small.png) et refait le calcul en Python avec le meme
echantillonnage que le shader (8x8 lectures bilineaires par gros pixel).
Attendu : quelques pixels differents au plus (arrondis du GPU).

    python tools/compare_gpu.py <dossier>
"""
import sys
import numpy as np
from PIL import Image
sys.path.insert(0, __import__("os").path.dirname(__file__))
import preview as pv

d = sys.argv[1]
srcimg = np.asarray(Image.open(f"{d}/source.png").convert("RGB")).astype(np.float64) / 255
gpu = np.asarray(Image.open(f"{d}/cga-small.png").convert("RGB")).astype(int)
h, w = gpu.shape[:2]
H, W = srcimg.shape[:2]

def bilinear(u, v):
    x = u * W - 0.5; y = v * H - 0.5
    x0 = np.floor(x).astype(int); y0 = np.floor(y).astype(int)
    fx = (x - x0)[..., None]; fy = (y - y0)[..., None]
    cl = lambda a, n: np.clip(a, 0, n - 1)
    p = lambda yy, xx: srcimg[cl(yy, H), cl(xx, W)]
    return (p(y0, x0) * (1 - fx) * (1 - fy) + p(y0, x0 + 1) * fx * (1 - fy)
            + p(y0 + 1, x0) * (1 - fx) * fy + p(y0 + 1, x0 + 1) * fx * fy)

yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
acc = np.zeros((h, w, 3))
for sy in range(8):
    for sx in range(8):
        u = (xx + (sx + 0.5) / 8) / w
        v = (yy + (sy + 0.5) / 8) / h
        acc += pv.to_linear(bilinear(u, v))
c = pv.to_srgb(acc / 64)
c = np.clip(c, 0, 1)
sim = pv.quantize(c, 0, 0, 0.0, 16, 0.1, 0.0)
sim = np.round(sim * 255).astype(int)
diff = np.abs(gpu - sim).max(-1) > 2
print(f"{w}x{h} : {diff.sum()} pixels differents sur {w*h} ({100*diff.mean():.3f} %)")
