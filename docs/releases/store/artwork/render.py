"""Render Store review artwork on Windows (Pillow, resvg-py and system Segoe UI)."""

from io import BytesIO
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

from PIL import Image, ImageChops, ImageDraw, ImageFont, ImageOps
from resvg_py import svg_to_bytes


ART = Path(__file__).resolve().parent
REPO = ART.parents[3]
EN = ART.parent / "en-US"
DESIGNS = REPO / "docs/plans/assets"
RC = REPO / "artifacts/store-1.0.0-rc"
FONTS = Path("C:/Windows/Fonts")
LANCZOS = Image.Resampling.LANCZOS
MARKS = {"Lift": "store-icon-proposal.svg", "Glide": "store-icon-glide.svg"}
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


def icons():
    marks = {name: svg(DESIGNS / filename, 1024, 1024) for name, filename in MARKS.items()}
    sheet = Image.new("RGB", (1168, 580), "#FBFBFD")
    draw = ImageDraw.Draw(sheet)
    label(sheet, (42, 28), "Wintodo", 36, bold=True)
    label(sheet, (1126, 48), "Windows app icon", 21, "#576070", anchor="rm")
    draw.line((42, 91, 1126, 91), fill="#CFD3DB")
    for i, (name, mark) in enumerate(marks.items()):
        x = 42 + i * 557
        label(sheet, (x, 113), name, 29, bold=True)
        label(sheet, (x, 158), ("A W that folds into a check.", "A check with a split wingtip.")[i], 19, "#566071")
        for offset, paper in ((0, "#EDF0F5"), (262, "#222631")):
            draw.rectangle((x+offset, 207, x+offset+261, 407), fill=paper)
            icon = mark.resize((157, 157), LANCZOS)
            sheet.paste(icon, (x+offset+52, 229), icon)
        label(sheet, (x, 433), "At Windows sizes", 16, "#566071")
        for j, n in enumerate((16, 24, 32, 48)):
            slot = x+j*134
            icon = mark.resize((n, n), LANCZOS)
            for offset, bg in ((0,"#EDF0F5"), (58,"#222631")):
                draw.rectangle((slot+offset,472,slot+offset+57,529), fill=bg)
                sheet.paste(icon, (slot+offset+29-n//2,501-n//2), icon)
            label(sheet, (slot+58, 545), f"{n} px", 14, "#566071", anchor="mt")
    preview = DESIGNS / "store-icon-proposal.png"
    sheet.save(preview, optimize=True)
    for name, mark in marks.items():
        folder = RC / ("icon-proposal" if name == "Lift" else "icon-glide") / "Assets"
        folder.mkdir(parents=True, exist_ok=True)
        files = []
        for filename, (w, h, extent) in TILES.items():
            logo = mark.resize((extent, extent), LANCZOS)
            if filename.startswith("LockScreen"):
                monochrome = Image.new("RGBA", logo.size, "white")
                monochrome.putalpha(logo.getchannel("A"))
                logo = monochrome
            tile = Image.new("RGBA", (w, h))
            tile.alpha_composite(logo, ((w-extent)//2, (h-extent)//2))
            tile.save(folder / filename, optimize=True)
            files.append(filename)
        sizes = [(n, n) for n in FRAMES]
        layers = [mark.resize(size, LANCZOS) for size in sizes]
        layers[-1].save(folder / "AppIcon.ico", sizes=sizes, append_images=layers[:-1])
        files.append("AppIcon.ico")
        assert set(files) == {p.name for p in (REPO/"src/Wintodo.App/Assets").iterdir()
                              if p.suffix.lower() in {".png", ".ico"}}
        for filename in files:
            with Image.open(folder/filename) as icon, Image.open(REPO/"src/Wintodo.App/Assets"/filename) as shipped:
                assert icon.size == shipped.size
                if filename.endswith(".ico"):
                    assert icon.ico.sizes() == shipped.ico.sizes() == set(sizes)
        bundle = "Wintodo-package-icon-proposal.zip" if name == "Lift" else "Wintodo-package-icon-Glide.zip"
        archive(RC/bundle, [(folder/f, "Assets/"+f) for f in files] +
                [(DESIGNS/MARKS[name], name+".svg"), (preview, "preview.png")])


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


def frame(paper, shot, center_y, max_h, dark=False):
    """Fit the whole native window; only the outer frame receives an edge."""
    window = ImageOps.contain(shot, (1784, max_h), LANCZOS)
    w, h = window.size
    x, y = (CANVAS[0]-w)//2, center_y-h//2
    draw = ImageDraw.Draw(paper)
    draw.rounded_rectangle((x-1, y+3, x+w, y+h+3), radius=10, fill="#1B202A" if dark else "#C3CAD0")
    draw.rounded_rectangle((x-1, y-1, x+w, y+h), radius=10, fill="#505660" if dark else "#B7BFC8")
    mask = Image.new("L", (w, h))
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, w-1, h-1), radius=9, fill=255)
    paper.paste(window.convert("RGB"), (x,y), ImageChops.darker(mask, window.getchannel("A")))


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
        frame(screenshot, shot, 536, 990, dark)
        screenshot.save(EN/"screenshots"/(stem+".png"), optimize=True)
        promo = Image.new("RGB", CANVAS, paper)
        frame(promo, shot, 593, 898, dark)
        ink = "#F3F5F9" if dark else "#1D2633"
        quiet = "#C8D3DF" if dark else "#485461"
        if i == 0:
            if night.size != shot.size:
                raise ValueError("A theme split needs two captures with matching window geometry")
            right = Image.new("RGB", CANVAS, "#26313F")
            frame(right, night, 593, 898, True)
            promo.paste(right, (0,0), seam)
        label(promo, (88, 32), title, 54, ink, bold=True)
        label(promo, (91, 99), sub, 23, quiet)
        label(promo, (1830, 51), "Wintodo", 25, "#EDF2F8" if i == 0 else ink, True, "rt")
        label(promo, (1830, 92), f"0{i+1} / 05", 17, "#C6D3E2" if i == 0 else quiet, anchor="rt")
        promo.save(EN/"promos"/(stem+".png"), optimize=True)
    hero = svg(ART/"hero.svg", *CANVAS).convert("RGB")
    hero.save(EN/"store-hero.png", optimize=True)
    overview = Image.new("RGB", (1464,1346), "#FAFAFC")
    label(overview, (36,20), "Wintodo", 33, bold=True)
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
    icons()
    gallery()
