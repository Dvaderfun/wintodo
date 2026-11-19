"""Render Store review artwork on Windows (Pillow, resvg-py and system Segoe UI)."""

from argparse import ArgumentParser
from io import BytesIO
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

from PIL import Image, ImageChops, ImageDraw, ImageFont
from resvg_py import svg_to_bytes


ART = Path(__file__).resolve().parent
REPO = ART.parents[3]
EN = ART.parent / "en-US"
DESIGNS = REPO / "docs/plans/assets"
RC = REPO / "artifacts/store-1.0.0-rc"
FONTS = Path("C:/Windows/Fonts")
LANCZOS = Image.Resampling.LANCZOS
GLIDE = DESIGNS / "store-icon-glide.svg"
FRAMES = (16, 32, 48, 64, 128, 256)
TILES = {
    "StoreLogo.png": (50, 50, 50), "Square44x44Logo.scale-200.png": (88, 88, 88),
    "Square44x44Logo.targetsize-24_altform-unplated.png": (24, 24, 24),
    "Square44x44Logo.targetsize-48_altform-lightunplated.png": (48, 48, 48),
    "Square150x150Logo.scale-200.png": (300, 300, 263),
    "Wide310x150Logo.scale-200.png": (620, 300, 246),
    "SplashScreen.scale-200.png": (1240, 600, 272),
    "LockScreenLogo.scale-200.png": (48, 48, 48),
}


def svg(path, width, height):
    data = svg_to_bytes(svg_path=str(path), width=width, height=height, skip_system_fonts=True)
    return Image.open(BytesIO(data)).convert("RGBA")


def label(canvas, xy, words, size, color="#222837", bold=False, anchor="lt"):
    font = ImageFont.truetype(str(FONTS / ("seguisb.ttf" if bold else "segoeui.ttf")), size)
    ImageDraw.Draw(canvas).text(xy, words, font=font, fill=color, anchor=anchor)


def archive(path, files):
    path.parent.mkdir(parents=True, exist_ok=True)
    with ZipFile(path, "w", ZIP_DEFLATED) as bundle:
        for source, name in files:
            bundle.write(source, name)


def export_icon(folder):
    """Optional: reproduce the approved nine Glide assets in a scratch directory."""
    folder.mkdir(parents=True, exist_ok=True)
    mark = svg(GLIDE, 1024, 1024)
    for filename, (w, h, extent) in TILES.items():
        logo = mark.resize((extent, extent), LANCZOS)
        if filename.startswith("LockScreen"):
            white = Image.new("RGBA", logo.size, "white")
            white.putalpha(logo.getchannel("A"))
            logo = white
        tile = Image.new("RGBA", (w, h))
        tile.alpha_composite(logo, ((w-extent)//2, (h-extent)//2))
        tile.save(folder / filename, optimize=True)
    sizes = [(n, n) for n in FRAMES]
    layers = [mark.resize(size, LANCZOS) for size in sizes]
    layers[-1].save(folder / "AppIcon.ico", sizes=sizes, append_images=layers[:-1])


# Filename, capture, matte, headline, supporting copy. The listing images
# have no added lettering; headlines are for the separate promotional set.
SCENES = (
    ("01-windows", "day-light", "#E1E8F2", "Your day, on Windows.",
     "My Day brings tasks from across your lists into one native WinUI 3 window."),
    ("02-task-details", "bag-light", "#D9E6E4", "One bag. Three small steps.",
     "Keep the checklist, reminder and packing note with the task."),
    ("03-keyboard", "keys", "#272A37", "Stay on the keys.",
     "Add with Ctrl+N, find with Ctrl+F and complete with Space."),
    ("04-import", "choose", "#E4EAE7", "Bring your lists along.",
     "Pick the Microsoft To Do lists you want to copy. Your originals stay where they are."),
    ("05-open-formats", "kitchen-export", "#F0E6D9", "Keep your Sunday routine.",
     "Open an iCalendar list with its dates and repeats. Export a copy whenever you like."),
)
CANVAS = (1920, 1080)


# The same two vertical guides carry a promo's type and both sides of its window.
PROMO_TOP, PROMO_BOTTOM = 184, 1024
TITLE_BASELINE, SUB_BASELINE = 103, 149


def curved_mask(size, radius):
    # Area averaging provides symmetric, fractional coverage at the four corners.
    scale = 4
    mask = Image.new("L", (size[0]*scale, size[1]*scale))
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, mask.width-1, mask.height-1),
                                           radius=radius*scale, fill=255)
    return mask.resize(size, Image.Resampling.BOX)


def frame(paper, shot, top, bottom, dark=False):
    """Replace only the captured square DWM edge with one smooth 1 px hairline."""
    scale = min(1784/shot.width, (bottom-top)/shot.height)
    w, h = round(shot.width*scale), round(shot.height*scale)
    x, y = (paper.width-w)//2, top+(bottom-top-h)//2
    radius = round(9*w/shot.width)
    paper.paste("#53606C" if dark else "#A8B6C1", (x, y, x+w, y+h), curved_mask((w,h), radius))
    # The VM's first and last rows/columns are the OS border, outside the UI.
    window = shot.crop((1, 1, shot.width-1, shot.height-1)).resize((w-2, h-2), LANCZOS)
    mask = ImageChops.multiply(curved_mask(window.size, radius-1), window.getchannel("A"))
    paper.paste(window.convert("RGB"), (x+1, y+1), mask)
    return x, x+w


def gallery():
    (EN/"screenshots").mkdir(parents=True, exist_ok=True)
    (EN/"promos").mkdir(parents=True, exist_ok=True)
    sources = ART/"captures"
    with Image.open(sources/"day-dark.png") as image:
        night = image.convert("RGBA")
    # Two actual captures of the same My Day view. The curved seam belongs
    # only to the cover promo; it never appears in a Partner Center screenshot.
    sweep = ('<svg xmlns="http://www.w3.org/2000/svg" width="1920" height="1080">'
             '<path d="M1620-48C1470 137 1065 218 795 436S417 816 175 1080H1920V0Z"/></svg>')
    seam = Image.open(BytesIO(svg_to_bytes(svg_string=sweep))).convert("RGBA").getchannel("A")
    for i,(stem, name, paper, title, sub) in enumerate(SCENES):
        with Image.open(sources/(name+".png")) as image:
            shot = image.convert("RGBA")
        dark = name == "keys"
        screenshot = Image.new("RGB", CANVAS, paper)
        frame(screenshot, shot, 48, CANVAS[1]-48, dark)
        screenshot.save(EN/"screenshots"/(stem+".png"), optimize=True)
        promo = Image.new("RGB", CANVAS, paper)
        left, right_edge = frame(promo, shot, PROMO_TOP, PROMO_BOTTOM, dark)
        ink = "#F3F5F9" if dark else "#1D2633"
        quiet = "#C8D3DF" if dark else "#485461"
        if i == 0:
            if night.size != shot.size:
                raise ValueError("A theme split needs two captures with matching window geometry")
            right = Image.new("RGB", CANVAS, "#26313F")
            frame(right, night, PROMO_TOP, PROMO_BOTTOM, True)
            promo.paste(right, (0,0), seam)
        label(promo, (left, TITLE_BASELINE), title, 58, ink, bold=True, anchor="ls")
        label(promo, (left, SUB_BASELINE), sub, 26, quiet, anchor="ls")
        label(promo, (right_edge, TITLE_BASELINE), "Wintodo", 28, "#EDF2F8" if i == 0 else ink, True, "rs")
        label(promo, (right_edge, SUB_BASELINE), f"{i+1:02d} / {len(SCENES):02d}", 18,
              "#C6D3E2" if i == 0 else quiet, anchor="rs")
        promo.save(EN/"promos"/(stem+".png"), optimize=True)
    hero = svg(ART/"hero.svg", *CANVAS).convert("RGB")
    hero.save(EN/"store-hero.png", optimize=True)
    overview = Image.new("RGB", (1464,1346), "#FAFAFC")
    mark = svg(GLIDE, 46, 46)
    overview.paste(mark, (34, 16), mark)
    label(overview, (90,23), "Wintodo", 33, bold=True)
    label(overview, (1428,39), "Windows 11    /    English gallery", 18, "#566071", anchor="rm")
    thumbs = [EN/"promos"/(s[0]+".png") for s in SCENES] + [EN/"store-hero.png"]
    for i,path in enumerate(thumbs):
        with Image.open(path) as asset:
            thumb = asset.resize((684,385), LANCZOS)
        overview.paste(thumb, (36+(i%2)*708, 88+(i//2)*416))
    overview.save(EN/"preview.jpg", quality=94, subsampling=0, optimize=True)
    listing = [EN/"README.md", EN/"preview.jpg", EN/"store-hero.png", ART/"hero.svg"] + [
        EN/folder/(scene[0]+".png") for folder in ("screenshots","promos") for scene in SCENES]
    archive(RC/"Wintodo-Store-en-US.zip", [(p,p.relative_to(ART.parent).as_posix()) for p in listing])


if __name__ == "__main__":
    parser = ArgumentParser(description=__doc__)
    parser.add_argument("--export-icon", type=Path, metavar="DIR", help="export Glide only")
    args = parser.parse_args()
    if args.export_icon:
        export_icon(args.export_icon)
    else:
        gallery()
