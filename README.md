# retro-filter

Une app Windows qui affiche n'importe quelle fenêtre (un jeu, une vidéo, une page web) avec les couleurs des PC des années 80 : palette CGA à 4 couleurs ou EGA à 16 couleurs, avec un tramage fin calculé sur la carte graphique.

Le filtre se pose par-dessus la fenêtre, sans rien modifier ni injecter dans le programme filtré. La fenêtre garde le clavier et la souris : on joue ou on regarde normalement.

Le projet est un prototype qui fonctionne, encore en cours de développement.

## Ce qu'il faut

- Windows 10 version 2004 ou plus récent. Windows 11 24H2 est conseillé : avant cette version, Windows limite la capture à environ 60 images par seconde.
- Une carte graphique compatible Direct3D 11.
- Le [kit .NET 8 SDK](https://dotnet.microsoft.com/download) pour compiler (`winget install Microsoft.DotNet.SDK.8`).

## Compiler et lancer

```
cd src/RetroFilter
dotnet build
bin\Debug\net8.0-windows10.0.26100.0\RetroFilter.exe
```

## Utilisation

1. Ouvrez ce que vous voulez filtrer. Pour un jeu, choisissez le mode **fenêtré** ou **fenêtré sans bordure** dans ses options d'affichage.
2. Dans retro-filter, choisissez la fenêtre dans la liste.
3. Choisissez le style :
   - **Carte** : CGA (4 couleurs, 1981) ou EGA (16 couleurs, 1984).
   - **Mode** : Moderne (tramage fin, à la résolution de l'écran) ou Historique (320×200, gros pixels comme à l'époque).
   - **Palette** : les pastilles montrent les couleurs de chaque palette.
4. Cliquez sur **Poser le filtre**.

La croix de la fenêtre la cache seulement : l'app continue dans la zone de notification. Un clic sur son icône la rouvre, un clic droit donne les mêmes choix et permet de quitter.

### Raccourcis

| Raccourci | Effet |
|---|---|
| Ctrl+Alt+R | Pose le filtre sur la fenêtre active, ou le retire |
| Ctrl+Alt+S | Pendant le filtre : enregistre l'image filtrée et l'image d'origine en PNG dans `Images\retro-filter` |

Ctrl+Alt+R est le seul raccourci réservé en permanence. D'autres raccourcis (carte, mode et palette suivants, barre d'informations) s'activent dans « Avancé ».

## Limites connues

- **Plein écran exclusif** : le filtre ne peut pas s'afficher par-dessus. Passez le jeu en fenêtré sans bordure.
- **Vidéos protégées** (la plupart des services de vidéo payants) : Windows renvoie une image noire à la capture.
- **Retard** : l'image filtrée arrive au moins une image après celle du programme filtré.
- **HDR** : la capture se fait en 8 bits par couleur, le rendu d'un jeu en HDR n'a pas été testé.

## Vie privée et système

- L'app ne fait aucun accès réseau. Les images capturées restent sur votre machine.
- Les réglages sont enregistrés dans `%APPDATA%\retro-filter\settings.json`.
- Pour rester fluide quand un jeu occupe toute la carte graphique, l'app demande à Windows la priorité GPU « temps réel ». Aucun droit administrateur n'est nécessaire.

## Comment ça marche

1. **Capture** de la fenêtre par Windows Graphics Capture, la même technique qu'OBS.
2. **Tramage** dans un shader HLSL (`src/RetroFilter/Shaders/dither.hlsl`), pour chaque point de l'image :
   - moyenne de la zone en lumière linéaire ;
   - recherche de la paire de couleurs de la palette et de la proportion de mélange qui imitent le mieux la couleur (tramage de Yliluoma), l'écart étant mesuré dans l'espace OKLab, proche de la perception humaine ;
   - une matrice de Bayer 4×4 décide laquelle des deux couleurs afficher.
3. **Affichage** dans une fenêtre transparente aux clics, maintenue juste au-dessus de la fenêtre filtrée.

Les mélanges de chaque paire de couleurs sont calculés une fois par le programme (`DitherTable.cs`) et non pour chaque point : le traitement prend de l'ordre de 1 à 4 millisecondes par image en 1920×1080 sur une carte graphique de 2018.

## Contenu du dépôt

| Dossier | Contenu |
|---|---|
| `src/RetroFilter/` | L'app (C#, .NET 8, WinForms, Direct3D 11 avec [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows)) |
| `shaders/` | Les shaders d'origine au format RetroArch `.slang`, qui ont servi à mettre l'algorithme au point. Voir [shaders/README.md](shaders/README.md) |
| `tools/` | `preview.py` applique l'algorithme à une capture d'écran ; `compare_gpu.py` compare la sortie de l'app à ce simulateur |

### Modes de test en ligne de commande

`RetroFilter.exe` accepte des modes sans interface, utiles pour vérifier un changement :

| Commande | Effet |
|---|---|
| `--snapshot "<titre>" <dossier>` | Capture une fenêtre et enregistre l'image d'origine et l'image filtrée |
| `--verify "<titre>"` | Compare le shader rapide au shader de référence, pour toutes les palettes |
| `--bench "<titre>"` | Mesure le temps de traitement par image |
| `--fps "<titre>" <0\|1\|2>` | Mesure le débit de capture selon la priorité GPU |
| `--overlay "<titre>" <dossier>` | Vérifie que le filtre reste collé à la fenêtre et que les clics le traversent |
| `--ui-test <dossier>` | Enregistre des captures de l'interface |

`<titre>` est une partie du titre de la fenêtre visée.

## Licence

[MIT](LICENSE).
