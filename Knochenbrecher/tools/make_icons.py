"""Erzeugt die Symbole des Mods aus dem Blender-Bild docs/hammer_icon.png.

    python3 tools/make_icons.py      (im Ordner Knochenbrecher, benötigt Pillow)

package/icon.png                 Paket-Symbol (256 x 256, mit Hintergrund)
package/knochenbrecher_icon.png  Symbol im Inventar (128 x 128, freigestellt)
"""
from PIL import Image, ImageDraw

render = Image.open("docs/hammer_icon.png").convert("RGBA")
render = render.crop(render.getbbox())

# Auf ein Quadrat mit etwas Rand bringen
side = max(render.size)
square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
square.alpha_composite(render, ((side - render.width) // 2, (side - render.height) // 2))

square.resize((128, 128), Image.LANCZOS).save("package/knochenbrecher_icon.png")

S = 1024
background = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(background).ellipse((40, 40, S - 40, S - 40), fill=(46, 58, 38, 255),
                                   outline=(120, 140, 70, 255), width=24)
hammer = square.resize((760, 760), Image.LANCZOS)
background.alpha_composite(hammer, ((S - 760) // 2, (S - 760) // 2))
background.resize((256, 256), Image.LANCZOS).save("package/icon.png")
