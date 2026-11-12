"""
Dessine la scène de démonstration du README : un coucher de soleil « synthwave »
entièrement calculé (aucune image extérieure, donc libre de droits).

    python tools/demo_scene.py docs/scene.png 800 500

L'image sert ensuite d'entrée à l'app :

    RetroFilter.exe --image docs/scene.png docs palette=0
"""
import sys

import numpy as np
from PIL import Image


def lerp(a, b, t):
    return a + (b - a) * t


def gradient(stops, t):
    """Dégradé entre des couleurs (position, (r, g, b)) pour un tableau t entre 0 et 1."""
    out = np.zeros(t.shape + (3,))
    for (p0, c0), (p1, c1) in zip(stops, stops[1:]):
        k = np.clip((t - p0) / (p1 - p0), 0, 1)[..., None]
        seg = lerp(np.array(c0, float), np.array(c1, float), k)
        mask = ((t >= p0) & (t <= p1))[..., None]
        out = np.where(mask, seg, out)
    return out


def ridge(x, seed, roughness):
    """Ligne de crête : somme de sinus, toujours la même pour une graine donnée."""
    rng = np.random.default_rng(seed)
    y = np.zeros_like(x)
    for octave in range(1, 7):
        y += np.sin(x * octave * rng.uniform(2.0, 4.0) + rng.uniform(0, 6.28)) * roughness / octave
    return y


def draw(width, height, scale=3):
    w, h = width * scale, height * scale
    yy, xx = np.mgrid[0:h, 0:w]
    u = xx / w                      # 0 à gauche, 1 à droite
    v = yy / h                      # 0 en haut, 1 en bas
    horizon = 0.62

    # Ciel : bleu nuit, violet, magenta, orange près de l'horizon.
    sky = gradient([
        (0.00, (10, 8, 40)),
        (0.35, (48, 16, 96)),
        (0.70, (170, 30, 130)),
        (0.92, (255, 110, 80)),
        (1.00, (255, 190, 110)),
    ], np.clip(v / horizon, 0, 1))
    img = sky

    # Étoiles dans le haut du ciel.
    rng = np.random.default_rng(7)
    for _ in range(140):
        sx, sy = rng.uniform(0, 1), rng.uniform(0, horizon * 0.7)
        radius = rng.uniform(0.0012, 0.003)
        d = np.hypot((u - sx) * w / h, v - sy)
        glow = np.clip(1 - d / radius, 0, 1)[..., None] * rng.uniform(0.5, 1.0)
        img = img + glow * (np.array([255, 255, 255.0]) - img)

    # Soleil : disque en dégradé jaune vers rose, coupé de bandes horizontales en bas.
    cx, cy, r = 0.5, horizon - 0.13, 0.27
    d = np.hypot((u - cx) * w / h, v - cy)
    t = np.clip((v - (cy - r)) / (2 * r), 0, 1)
    sun = gradient([(0.0, (255, 240, 120)), (0.55, (255, 120, 90)), (1.0, (255, 40, 150))], t)
    band = (np.sin((v - cy) * 95) > lerp(1.1, -0.2, np.clip((v - cy) / r, 0, 1))) | (v < cy)
    disc = (np.clip((r - d) * h / 2, 0, 1) * band)[..., None]
    halo = np.clip(1 - d / (r * 2.2), 0, 1)[..., None] ** 2 * 0.35
    img = img + halo * (np.array([255, 120, 160.0]) - img)
    img = img + disc * (sun - img)

    # Deux chaînes de montagnes, la plus proche plus sombre.
    for seed, base, rough, color in [(3, 0.50, 0.035, (70, 30, 110)), (11, 0.56, 0.05, (26, 12, 60))]:
        crest = base + ridge(u * 3.0, seed, rough)
        inside = np.clip((v - crest) * h / 2, 0, 1)[..., None] * (v < horizon)[..., None]
        shade = lerp(np.array(color, float) * 1.5, np.array(color, float), np.clip((v - crest) / 0.1, 0, 1)[..., None])
        img = img + inside * (shade - img)

    # Sol : grille en perspective qui fuit vers l'horizon.
    ground = v >= horizon
    depth = np.where(ground, (v - horizon) / (1 - horizon), 1.0) + 1e-4
    floor = gradient([(0.0, (60, 10, 90)), (0.25, (20, 6, 50)), (1.0, (6, 4, 24))], np.clip(depth, 0, 1))
    gx = (u - 0.5) / depth * 6.0
    gz = 1.0 / depth * 1.6
    line_x = np.abs(gx - np.round(gx)) * depth
    line_z = np.abs(gz - np.round(gz)) * depth * depth * 2.2
    line = np.clip(1 - np.minimum(line_x / 0.006, line_z / 0.006), 0, 1)
    glow = np.clip(1 - np.minimum(line_x / 0.03, line_z / 0.03), 0, 1) * 0.35
    fade = np.clip(depth * 3.0, 0, 1)
    grid = (np.maximum(line, glow) * fade)[..., None]
    floor = floor + grid * (np.array([90, 255, 255.0]) - floor)
    # Reflet du soleil sur le sol.
    reflet = (np.clip(1 - np.abs(u - 0.5) / 0.22, 0, 1) * np.clip(1 - depth * 1.6, 0, 1))[..., None] * 0.5
    floor = floor + reflet * (np.array([255, 100, 170.0]) - floor)
    img = np.where(ground[..., None], floor, img)

    image = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8))
    return image.resize((width, height), Image.LANCZOS)


if __name__ == "__main__":
    output = sys.argv[1]
    width = int(sys.argv[2]) if len(sys.argv) > 2 else 800
    height = int(sys.argv[3]) if len(sys.argv) > 3 else 500
    draw(width, height).save(output)
    print(output)
