import sys
from pathlib import Path
from PIL import Image, ImageDraw

def sheet(vdirs, tags, outname, cols=3):
    rows = (len(vdirs)*len(tags) + cols - 1)//cols
    W, H = 256, 192
    img = Image.new("RGB", (cols*W, rows*(H+14)), (20,20,20))
    d = ImageDraw.Draw(img)
    n = 0
    for tag in tags:
        for v in vdirs:
            p = Path(v)/f"{tag}.png"
            if not p.exists(): continue
            tile = Image.open(p).convert("RGB")
            x, y = (n%cols)*W, (n//cols)*(H+14)
            img.paste(tile, (x, y+14))
            d.text((x+2, y+1), f"{Path(v).name} {tag}", fill=(255,255,0))
            n += 1
    img.save(outname)
    print(outname, img.size, n, "tiles")

v = sys.argv[1]
sheet([v], ["d1","d2","d3","d4","d5","menu"], sys.argv[2])
