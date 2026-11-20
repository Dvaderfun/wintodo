"""Private MSIX artwork and readback. Never rewrite the app in a checkout."""
import argparse
from hashlib import sha256
import json
import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET
from xml.dom import minidom
from zipfile import ZipFile

from PIL import Image, ImageDraw, ImageFont

NAME = "Wintodo.Hybrid.Dev"
PUBLISHER = "CN=AppPublisher"
TOAST = "911CE0E9-0B73-4C41-BAD4-93F4EBE990E3"
TITLE = "Wintodo DEV"
LABELS = {"AppDisplayName": TITLE, "AppTitleBar.Title": TITLE, "MainWindowTitle": TITLE,
          "AppDescription": "Wintodo DEV — private design preview"}


def require(value, message):
    if not value:
        raise ValueError(message)


def one(items):
    seq = list(items)
    require(len(seq) == 1, "Unexpected number of elements")
    return seq[0]


def mark(source, *, monochrome=False):
    """Add a right-pointing DEV ribbon to the approved artwork without redrawing Glide."""
    base = source.convert("RGBA")
    x0, y0, x1, y1 = base.getchannel("A").getbbox()
    size = x1 - x0
    pixel_face = size <= 25
    width, height = (15, 7) if pixel_face else (round(size * .67), max(9, round(size * .27)))
    unit = max(1, round(size * .025))
    # At 16 px every letter still has a full 3-by-5 pixel face.
    x = max(0, min(base.width - width, x1 - width + unit))
    y = max(0, min(base.height - height, y1 - height + 2 * unit))
    scale = 1 if pixel_face else 4
    badge = Image.new("RGBA", (width * scale, height * scale))
    draw = ImageDraw.Draw(badge)
    w, h = badge.size
    notch = max(2 * scale, round(h * .34))
    draw.polygon([(0, 0), (w - notch - 1, 0), (w - 1, h // 2),
                  (w - notch - 1, h - 1), (0, h - 1)],
                 fill="white" if monochrome else "#422378",
                 outline="white" if monochrome else "#CBB5F2", width=max(1, round(h * .055)))
    ink = (0, 0, 0, 0) if monochrome else "white"
    if pixel_face:
        glyphs = ("110 101 101 101 110", "111 100 110 100 111", "101 101 101 101 010")
        for n, glyph in enumerate(glyphs):
            for row, bits in enumerate(glyph.split()):
                for col, bit in enumerate(bits):
                    if bit == "1":
                        draw.point((1 + 4 * n + col, 1 + row), fill=ink)
    else:
        # Rasterize the system typeface at each target size; no font ships in the MSIX.
        font_path = Path(os.environ["WINDIR"]) / "Fonts" / "segoeuib.ttf"
        font = ImageFont.truetype(str(font_path), h)
        left, top, right, bottom = font.getbbox("DEV")
        factor = min((w - notch - h * .13) / (right - left), (h * .69) / (bottom - top))
        font = ImageFont.truetype(str(font_path), max(1, round(h * factor)))
        left, top, right, bottom = font.getbbox("DEV")
        draw.text(((w - notch - right - left) / 2, (h - bottom - top) / 2),
                  "DEV", font=font, fill=ink)
        badge = badge.resize((width, height), Image.Resampling.LANCZOS)
    if monochrome:
        # Keep the shell silhouette single-colour with transparent DEV cut-outs.
        base.paste(badge, (x, y))
    else:
        base.alpha_composite(badge, (x, y))
    return base


def artworks(directory):
    for path in sorted(directory.glob("*.png")):
        with Image.open(path) as original:
            icon = mark(original, monochrome=path.name.startswith("LockScreen"))
        icon.save(path, optimize=True)
    path = directory / "AppIcon.ico"
    with Image.open(path) as original:
        sizes = sorted(original.ico.sizes())
        frames = [mark(original.ico.getimage(size)) for size in sizes]
    frames[-1].save(path, format="ICO", sizes=sizes, append_images=frames[:-1])


def save_xml(doc, path):
    path.write_bytes(doc.toxml(encoding="utf-8"))


def prepare(stage, version):
    # The caller creates a fresh source archive. Refuse a working checkout.
    require((stage / ".wintodo-preview-stage").is_file() and not (stage / ".git").exists(),
            "Not a private build staging directory")
    app = stage / "src" / "Wintodo.App"
    path = app / "Package.appxmanifest"
    doc = minidom.parse(str(path))
    ids = one(doc.getElementsByTagName("Identity"))
    require(ids.getAttribute("Name") == "47371DermenzhyStudio.Wintodo",
            "Unrecognized source package identity")
    for key, value in (("Name", NAME), ("Publisher", PUBLISHER), ("Version", version)):
        ids.setAttribute(key, value)
    one(doc.getElementsByTagName("PublisherDisplayName")).firstChild.data = "Wintodo local development"
    toast = one(doc.getElementsByTagName("desktop:ToastNotificationActivation"))
    com = one(doc.getElementsByTagName("com:Class"))
    require(toast.getAttribute("ToastActivatorCLSID") == com.getAttribute("Id") != TOAST,
            "Unexpected source activation declarations")
    toast.setAttribute("ToastActivatorCLSID", TOAST)
    com.setAttribute("Id", TOAST)
    one(doc.getElementsByTagName("desktop:StartupTask")).setAttribute("TaskId", "WintodoDevStartup")
    save_xml(doc, path)
    path = app / "Strings" / "en-US" / "Resources.resw"
    doc = minidom.parse(str(path))
    for name, label in LABELS.items():
        entry = one(e for e in doc.getElementsByTagName("data") if e.getAttribute("name") == name)
        one(entry.getElementsByTagName("value")).firstChild.data = label
    save_xml(doc, path)
    path = app / "Wintodo.csproj"
    doc = minidom.parse(str(path))
    one(doc.getElementsByTagName("DefineConstants")).firstChild.data += ";WINTODO_PRIVATE_PREVIEW"
    save_xml(doc, path)
    artworks(app / "Assets")


def verify(stage, msix, makepri, version):
    app = stage / "src" / "Wintodo.App"
    with ZipFile(msix) as package:
        files = {item.filename.replace("\\", "/").lower(): item for item in package.infolist()}
        manifest = ET.fromstring(package.read(files["appxmanifest.xml"]))
        identity = one(manifest.findall("{*}Identity"))
        require(tuple(identity.get(k) for k in ("Name", "Publisher", "Version")) ==
                (NAME, PUBLISHER, version), "Wrong DEV identity")
        require(one(manifest.findall(".//{*}ToastNotificationActivation")).get("ToastActivatorCLSID") == TOAST and
                one(manifest.findall(".//{*}Class")).get("Id") == TOAST and
                one(manifest.findall(".//{*}StartupTask")).get("TaskId") == "WintodoDevStartup", "Wrong activation")
        require("appxsignature.p7x" in files, "Missing package signature")
        icons = [*sorted((app / "Assets").glob("*.png")), app / "Assets" / "AppIcon.ico"]
        for icon in icons:
            require(icon.read_bytes() == package.read(files["assets/" + icon.name.lower()]),
                    f"Missing or changed artwork: {icon.name}")
        with Image.open(app / "Assets" / "AppIcon.ico") as ico:
            require({(16,16), (24,24), (32,32), (48,48), (64,64), (128,128), (256,256)} == ico.ico.sizes(),
                    "Missing window icon frames")
            ico.ico.getimage((256,256)).save(stage / "dev-icon.png")
        pri = stage / "packaged.pri"
        pri.write_bytes(package.read(files["resources.pri"]))
    dump = stage / "packaged-pri.xml"
    subprocess.run([str(makepri), "dump", "/if", str(pri), "/of", str(dump), "/dt", "Detailed", "/o"],
                   check=True, stdout=subprocess.DEVNULL)
    resources = ET.parse(dump)
    for name, label in LABELS.items():
        uri = f"ms-resource://{NAME}/Resources/" + name.replace(".", "/")
        node = one(n for n in resources.iter("NamedResource") if n.get("uri", "").casefold() == uri.casefold())
        require(label in [v.text for v in node.iter("Value")], "Wrong packaged label: " + name)
    print(json.dumps({"identity": NAME, "version": version, "labels": len(LABELS),
                      "matchedBadgeAssets": len(icons), "msixSha256": sha256(msix.read_bytes()).hexdigest()}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("stage", type=Path)
    parser.add_argument("version")
    parser.add_argument("--verify", type=Path)
    parser.add_argument("--makepri", type=Path)
    args = parser.parse_args()
    if args.verify:
        verify(args.stage, args.verify, args.makepri, args.version)
    else:
        prepare(args.stage, args.version)
