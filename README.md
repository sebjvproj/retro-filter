# retro-filter

Shader qui transforme n'importe quel jeu PC en rendu CGA : gros pixels, palette 4 couleurs, tramage. Deux philosophies :

- **Moderne** : l'esprit CGA avec un tramage fin qui se fond en dégradés.
- **Authentique** : les contraintes réelles d'une carte CGA de 1984, moche et fidèle.

Format RetroArch `.slang`, utilisable dans [ShaderGlass](https://github.com/mausimus/ShaderGlass) (overlay Windows, sans injection dans le jeu).

## Utilisation avec ShaderGlass

1. Installer ShaderGlass (GitHub ou Steam).
2. Lancer le jeu en mode **fenêtré** ou **fenêtré sans bordure**.
3. Dans ShaderGlass, choisir la fenêtre du jeu comme entrée.
4. Menu *Shader* : importer un shader personnalisé et choisir un preset dans `shaders/`.
5. Régler la taille de pixel d'entrée de ShaderGlass sur **1x** (le shader pixelise déjà lui-même), puis passer en plein écran.

Les paramètres se règlent en direct dans la fenêtre des paramètres de ShaderGlass.

## Presets

| Fichier | Résolution | Pour quoi |
|---|---|---|
| `cga-moderne.slangp` | 320×200 | Le rendu recommandé : le tramage le plus fin. Basé sur la v2 (deux passes), sans contours ni anti-scintillement |
| `cga-lisible.slangp` | 320×216 | Pour les jeux où l'on ne distingue plus rien : contours, motifs plus gros, moins de bruit, plus de relief, anti-scintillement |
| `cga-authentique.slangp` | 320×200 | Fidèle à l'époque : format 4:3 avec bandes noires, tramage en damier uniquement, couleurs brutes |
| `cga.slangp` | 320×200 | Base neutre, tous les réglages à leur valeur par défaut |
| `cga-640x200.slangp` | 640×200 | Mode haute résolution CGA, plus lisible pour les textes |
| `cga-v2.slangp` | 320×200 | Deuxième version (deux passes, tramage de Yliluoma), identique au preset moderne |
| `cga-v1.slangp` | réglable | Première version (une seule passe, tramage simple). Seule à proposer la résolution en curseurs (« Largeur / Hauteur virtuelle », jusqu'à 1280×800) |

## Paramètres

| Paramètre | Défaut | Rôle |
|---|---|---|
| Palette | 0 | voir tableau ci-dessous |
| Couleur de fond | 0 | Remplace le noir par n'importe laquelle des 16 couleurs CGA, comme sur une vraie carte (bleu foncé = 1). Sans effet en palette 16 couleurs |
| Couleur ↔ luminosité | 0 | 0 = respecte les teintes, 1 = ne regarde que la clarté. Entre 0,3 et 0,5, on garde le relief sans perdre les couleurs |
| Niveaux de tramage | 16 | 1 = aucun (aplats), 2 = damier seul, 4 ou 8 = motifs rétro, 16 = dégradés les plus fins |
| Douceur du tramage | 0.1 | Plus haut = moins de mélanges de couleurs éloignées (moins de bruit, plus d'aplats) |
| Contours | 0 | Trace un trait de la couleur la plus sombre du côté sombre des bords. Rend personnages et objets plus lisibles |
| Stabilité | 0 | Anti-scintillement : un gros pixel qui change très peu garde sa couleur. 0,03 supprime le scintillement dû au bruit d'image |
| Format 4:3 | 0 | Bandes noires sur les côtés, comme un moniteur d'époque |
| Luminosité, contraste, saturation | 0 / 1 / 1 | Ajustement avant réduction de palette |

| Palette | Couleurs |
|---|---|
| 0 | fond, cyan, magenta, blanc (la palette CGA « classique ») |
| 1 | même palette, basse intensité |
| 2 | fond, vert, rouge, jaune |
| 3 | même palette, basse intensité (vert, rouge, marron) |
| 4 | fond, cyan, rouge, blanc (mode 5) |
| 5 | les 16 couleurs CGA |
| 6 | fond et 3 gris |
| 7 | fond et blanc (mode 640×200, 2 couleurs) |

## Conseils de réglage

- **Jeu sombre** : monter la luminosité (+0,1 à +0,2), ou choisir un fond bleu foncé (couleur de fond = 1) pour que les zones noires gardent une teinte.
- **Palette cyan/magenta trop bruitée** : monter la douceur du tramage vers 0,2 et le curseur couleur ↔ luminosité vers 0,35 (c'est ce que fait le preset lisible).
- **Juger un réglage** : toujours regarder en plein écran. En vignette réduite, le tramage fin ressemble à du bruit alors qu'il se fond en dégradés à taille réelle.
- **Vert/rouge/jaune** : une douceur trop haute transforme les surfaces en aplats rouges, rester vers 0,1.
- **Contours** : très efficaces sur les palettes contrastées (vert/rouge/jaune, 16 couleurs). Sur cyan/magenta, le noir étant déjà partout, l'effet est plus discret.
- **Autre résolution** : copier un preset et changer `scale_x0` / `scale_y0`. Pour des pixels réguliers, choisir une division exacte de l'écran (1920×1080 : 320×216, 384×216, 480×270).
- Pour un effet écran cathodique, empiler un shader CRT de la bibliothèque de ShaderGlass après celui-ci.

## Aperçu sans lancer de jeu

`tools/preview.py` applique le même algorithme à une capture d'écran :

```bash
pip install numpy pillow
python3 tools/preview.py capture.png sortie.png --preset moderne
python3 tools/preview.py capture.png sortie.png --preset lisible --palette 2 --bg 1
python3 tools/preview.py capture.png sortie.png --palette 0 --luma-weight 0.5 --outline 0.6
```

Options : `--preset`, `--res`, `--palette`, `--bg`, `--luma-weight`, `--levels`, `--penalty`, `--outline`, `--brightness`, `--contrast`, `--saturation`, `--aspect43`. Pratique pour comparer les réglages avant de les reporter dans ShaderGlass.

## Comment ça marche

Trois passes :

1. **Préparation** (`cga-prepare.slang`, à la résolution virtuelle) : chaque gros pixel est la moyenne de 8×8 échantillons de l'image du jeu, calculée en lumière linéaire. Puis luminosité, contraste, saturation, et stabilité (comparaison avec l'image précédente).
2. **Palette** (`cga.slang`, à la résolution virtuelle) :
   - **Contours** : détection des bords par filtre de Sobel sur les gros pixels voisins.
   - **Tramage de Yliluoma** : pour chaque paire de couleurs de la palette, on calcule la proportion de mélange qui imite le mieux le pixel, et on garde la meilleure paire. Les couleurs sont comparées dans l'espace OKLab, proche de la perception humaine. Le mélange se fait en sRGB : avec des pixels aussi gros, l'œil voit les points au lieu de les fusionner.
   - **Bayer 4×4** : la matrice décide, pixel par pixel, laquelle des deux couleurs afficher.
3. **Sortie** (`cga-output.slang`) : agrandissement sans lissage à la taille de l'écran, avec format 4:3 en option.

Les deux premières passes ne travaillent que sur 64 000 à 70 000 pixels : le coût GPU est négligeable.
