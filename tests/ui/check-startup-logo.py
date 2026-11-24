"""Offline signed-DEV pixel check (Python + Pillow); never captures a desktop.

Usage: py -3.13 tests/ui/check-startup-logo.py <startup-placement/run-id> <candidate.msix> [--check-fade]
0: recovery and applicable handoff checks passed; 1: a check failed; 2: inconclusive.
Ordinary launches can go straight to content. Pixel sampling cannot rule out a flash
between captures. Inspect the PNGs for other blank frames or visual defects.
"""
import csv
import hashlib
import io
import json
import sys
import zipfile
from pathlib import Path
from statistics import median

from PIL import Image, ImageChops, ImageStat


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


# The three opaque color regions of the private DEV artwork, including its purple badge.
# Sample their positions from the actual tested MSIX, not from an assumed vector silhouette.
COLORS = [(8,127,137), (38,189,171), (84,57,133)]


def templates(logo):
    result = {}
    for side in (27, 30, 33, 36, 38, 40):  # 152 DIPs on this 96-DPI guest, quarter-size capture + opening scale
        image = logo.resize((side, side), Image.Resampling.LANCZOS)
        groups = [[] for _ in COLORS]
        for y in range(0, side, 2):
            for x in range(0, side, 2):
                *rgb, alpha = image.getpixel((x, y))
                for group, color in zip(groups, COLORS):
                    if alpha >= 245 and max(abs(a-b) for a,b in zip(rgb, color)) < 26:
                        group.append((x-side//2, y-side//2))
                        break
        if min(map(len, groups)) >= 3:
            result[side] = groups
    if not result:
        raise ValueError("Package artwork has no recognizable three-color DEV template.")
    return result


def installed_window_center(desktop, ready):
    # First installation has no saved bounds. Infer the actual final window location;
    # it varies between fresh desktop sessions. Exclude the taskbar and narrow shadows.
    w,h = desktop.size
    mask = ImageChops.difference(desktop, ready).convert("L").point(lambda p: 255 if p > 22 else 0)
    xs = [x for x in range(w) if mask.crop((x,0,x+1,h-15)).histogram()[255] > h*.16]
    ys = [y for y in range(h-15) if mask.crop((0,y,w,y+1)).histogram()[255] > w*.16]
    if not xs or not ys:
        raise ValueError("Fresh-install desktop and final window could not be distinguished.")
    return (min(xs)+max(xs))//2, (min(ys)+max(ys))//2


def score(frame, desktop, marks):
    # 96-pixel crop centered on the expected window. Compare at offsets up to +/-15 pixels.
    # Score is the LOWEST matching fraction among all three colored regions, not the
    # count of teal pixels. This rejects wallpaper and near-neutral loading surfaces.
    best = 0
    for opacity in (1, .8, .6, .45):
        masks = []
        for color in COLORS:
            predicted = Image.blend(desktop, Image.new("RGB", frame.size, color), opacity)
            r,g,b = ImageChops.difference(frame, predicted).split()
            delta = ImageChops.lighter(ImageChops.lighter(r,g), b)
            masks.append(delta.point(lambda p: 1 if p <= round(12+16*opacity) else 0))
        for groups in marks.values():
            percentages = []
            for offsets, mask in zip(groups, masks):
                total = Image.new("L", (31,31))
                for x,y in offsets:
                    total = ImageChops.add(total, mask.crop((33+x,33+y,64+x,64+y)))
                percentages.append(total.point(lambda p: min(100, round(100*p/len(offsets)))))
            common = ImageChops.darker(ImageChops.darker(*percentages[:2]), percentages[2])
            best = max(best, common.getextrema()[1])
    return best


def surface_levels(image, bounds):
    # This fixture has one row. Quiet patches well below it sample the navigation
    # pane and list independently; text, badges, window borders and the logo are excluded.
    patches = []
    for fraction in (.10, .83):
        x = round((bounds["X"] + bounds["Width"] * fraction) / 4)
        y = round((bounds["Y"] + bounds["Height"] * .75) / 4)
        patches.append(ImageStat.Stat(image.crop((x-5,y-5,x+6,y+6)).convert("L")).median[0])
    return tuple(patches)


def saved_theme_handoff(name, samples, visible_ms, max_gap_ms):
    # Higher values mean closer to the requested end of the light/dark scale.
    # A flash moves both panes toward the opposite end, then returns.
    def oriented(levels):
        return tuple(255-v for v in levels) if name == "saved-dark" else levels
    final = oriented(samples[-1][3])
    if min(final) < 155:
        return "INCONCLUSIVE", f"settled fixture does not have two clear {name[6:]} surfaces"
    saw_logo = False
    for first, filename, detected, levels in samples:
        if first < visible_ms + 15:  # exclude the previous process and capture/observer startup skew
            continue
        current = oriented(levels)
        if not saw_logo:
            # A translucent opening window can still contain wallpaper. Start only
            # after the package logo AND two established expected-theme surfaces.
            saw_logo = detected and min(current) >= 170
        elif all(end-v >= 65 and v <= 150 for end,v in zip(final,current)):
            return "FAIL", f"{filename} at {first:.0f} ms: navigation/list luma {levels}, settled {samples[-1][3]}"
    if not saw_logo:
        return "NOT OBSERVED", "no sampled logo on the established theme; direct-to-content is allowed"
    if samples[-1][2]:
        return "INCONCLUSIVE", "the last sampled image still matches the logo"
    if max_gap_ms > 150:
        return "INCONCLUSIVE", f"capture gap reached {max_gap_ms:.0f} ms"
    return "PASS", "no sampled opposite-theme flash after the established logo"


def classify_fade(folder, frames, logo_hits, bounds, visible_ms):
    # Use two disjoint regions of the *same* frame. A dimming logo alone does
    # not establish a crossfade: the final list text/icons must also be emerging.
    def pixels(row):
        with Image.open(folder/row["image"]) as source:
            return source.convert("RGB").crop(rect)

    x,y,w,h = (bounds[k]/4 for k in ("X","Y","Width","Height"))
    rect = tuple(round(v) for v in (x,y,x+w,y+h))
    ready = pixels(frames[-1])
    w,h = ready.size
    for start, row in enumerate(frames):
        if row["image"] in logo_hits and float(row["lastMs"]) >= visible_ms:
            logo = pixels(row)
            # Require the unblended package colors at the center, not an image
            # still compositing over the desktop in the opening animation.
            mark = [(i,j) for j in range(h//2-24,h//2+24) for i in range(w//2-24,w//2+24)
                    if any(max(abs(a-b) for a,b in zip(logo.getpixel((i,j)), rgb)) < 6 for rgb in COLORS)]
            if len(mark) >= 35:
                break
    else:
        return "INCONCLUSIVE", "no established opaque logo"
    controls = [(i,j) for j in range(h//10,h*2//5) for i in range(6,w-6)]
    def contrasts(points):
        result = []
        for p in points:
            a,b = logo.getpixel(p), ready.getpixel(p)
            channel = max(range(3), key=lambda k: abs(b[k]-a[k]))
            delta = b[channel] - a[channel]
            if abs(delta) >= 55:
                result.append((p,channel,a[channel],delta))
        return result
    regions = [contrasts(points) for points in (mark,controls)]
    if min(map(len, regions)) < 35:
        return "INCONCLUSIVE", "too few independently distinguishable logo or list pixels"

    progress, last_opaque, cut_gap = [], None, None
    for row in frames[start:]:
        image = pixels(row)
        # For each high-contrast pixel, estimate how much of its settled color
        # is now present. Require agreement across the mark and real controls.
        levels = [median((image.getpixel(p)[k]-a)/delta for p,k,a,delta in region) for region in regions]
        first,last = float(row["firstMs"]), float(row["lastMs"])
        if max(abs(v) for v in levels) <= .05:
            last_opaque = last
        elif max(abs(1-v) for v in levels) <= .05:
            cut_gap = None if last_opaque is None else first-last_opaque
            break
        else:
            last_opaque = None
            if min(levels) >= .07 and max(levels) <= .93 and abs(levels[0]-levels[1]) <= .20:
                progress.append((first,*levels))
    else:
        return "INCONCLUSIVE", "no settled end of handoff"
    if progress:
        return "FADE", ", ".join(f"{t:.0f} ms: {a:.0%}/{b:.0%}" for t,a,b in progress) + " (logo/list progress)"
    if cut_gap is not None and 0 < cut_gap <= 100:
        return "CUT", f"opaque logo to settled list in adjacent samples {cut_gap:.0f} ms apart"
    return "INCONCLUSIVE", "missing or ambiguous intermediate samples"


def check(root, package, require_fade=False):
    summary = read_json(root/"summary.json")
    with package.open("rb") as handle:
        digest = hashlib.file_digest(handle, "sha256").hexdigest()
    if summary["Status"] != "PASS" or digest.lower() != summary["Sha256"].lower():
        raise ValueError("Signed placement gate failed or this is not the package it tested.")
    with zipfile.ZipFile(package) as archive:
        logo = Image.open(io.BytesIO(archive.read("Assets/Square150x150Logo.scale-200.png"))).convert("RGBA")
    marks = templates(logo)
    # Positive control: scaled and translucent logo on a dark client, translated three pixels.
    mark = logo.resize((30,30), Image.Resampling.LANCZOS)
    backdrop = Image.new("RGB", (96,96), (27,72,103))
    control = Image.new("RGB", (96,96), (32,32,32))
    control.paste(mark, (36,33), mark)
    if score(Image.blend(backdrop,control,.6), backdrop, marks) < 65:
        raise ValueError("Scaled template control failed; findings are not interpretable.")

    outcome, code = "INCONCLUSIVE: no separate recovery diagnostic", 2
    handoffs = []
    motion = {}
    recordings = ["first-install"] + [Path(n).stem for n in summary["Launches"]]
    recovery_name = Path(summary["SeparateRecoveryDiagnostic"]).stem if summary.get("SeparateRecoveryDiagnostic") else None
    if recovery_name:
        recordings.append(recovery_name)
    for name in recordings:
        folder = root/(name+"-screen")
        with (folder/"frames.csv").open(encoding="utf-8-sig", newline="") as stream:
            frames = list(csv.DictReader(stream))
        def pixels(row):
            with Image.open(folder/row["image"]) as source:
                return source.convert("RGB")
        # The previous window can still be animating closed at t=0.
        desktop = pixels(next((f for f in frames if float(f["lastMs"]) >= 260), frames[0]))
        record = {} if name == "first-install" else read_json(root/(name+".json"))
        if record:
            r = record["ExpectedWindow"]
            cx,cy = round((r["X"]+r["Width"]/2)/4), round((r["Y"]+r["Height"]/2)/4)
        else:
            cx,cy = installed_window_center(desktop, pixels(frames[-1]))
        box = (cx-48,cy-48,cx+48,cy+48)
        back = desktop.crop(box)
        hits, logo_hits, recovery_samples, recovery_hits = [], set(), [], []
        handoff_samples = []
        for frame in frames:
            first,last = float(frame["firstMs"]), float(frame["lastMs"])
            image = pixels(frame)
            detected = first > 0 and score(image.crop(box),back,marks) >= 65
            if detected:
                hits.append(f"{frame['image']}@{first:.0f}ms")
                logo_hits.add(frame["image"])
            if name in ("saved-dark", "saved-light"):
                levels = surface_levels(image, r)
                handoff_samples.append((first, frame["image"], detected, levels))
                if last != first:
                    handoff_samples.append((last, frame["image"], detected, levels))
            if name == recovery_name and record["Recovery"]["ObservedUncloakedBeforeCleanupEnded"]:
                # Compare only actual sample endpoints, not interpolated timestamps during a gap.
                start = record["FirstVisibleUncloakedMs"] + 15
                end = record["Recovery"]["LastStagingPresentMs"] - 15
                for timestamp in {first,last}:
                    if start < timestamp < end:
                        recovery_samples.append(timestamp)
                        if detected:
                            recovery_hits.append(frame["image"])
        print(f"{name}: {len(hits)} matching changed image(s); first {hits[0] if hits else 'none'}, last {hits[-1] if hits else 'none'}")
        if require_fade and name in ("normal-1","motion-on","motion-off"):
            kind, message = classify_fade(folder, frames, logo_hits, r, record["FirstVisibleUncloakedMs"])
            print(f"{name} transition: {kind}: {message}")
            motion[name] = (kind, record)
        if handoff_samples:
            status, message = saved_theme_handoff(name, handoff_samples,
                record.get("FirstVisibleUncloakedMs") or record["FirstVisibleMs"], read_json(folder/"sampling.json")["maxSampleGapMs"])
            handoffs.append(status)
            print(f"{name} handoff: {status}: {message}")
        if name == recovery_name:
            if recovery_hits:
                outcome, code = f"PASS: package logo sampled while recovery still had staged files ({recovery_hits[0]})", 0
            elif len(recovery_samples) >= 2 and max(recovery_samples)-min(recovery_samples) >= 100:
                outcome, code = "FAIL: sustained visible recovery was sampled without the package logo", 1
            else:
                outcome, code = "INCONCLUSIVE: no sufficiently sampled visible recovery interval", 2
    print("Recovery:", outcome)
    if require_fade:
        if not {"motion-on","motion-off"} <= motion.keys():
            # Older signed baseline captures have only the normal animation setting.
            print("Missing paired motion captures; normal-1 above is diagnostic only.")
            handoffs.append("INCONCLUSIVE")
        else:
            state = read_json(root/"motion.json")
            if state["Restored"] is not True or state["RestoredAnimationsEnabled"] != state["OriginalAnimationsEnabled"]:
                raise ValueError("Windows client-animation state was not restored.")
            if any(motion["motion-on"][1]["ExpectedWindow"][key] != motion["motion-off"][1]["ExpectedWindow"][key]
                   for key in ("X","Y","Width","Height","Maximized")):
                raise ValueError("Motion samples did not use the same window bounds.")
            for name, wanted in (("motion-on","FADE"),("motion-off","CUT")):
                kind, record = motion[name]
                if record["ClientAnimationsEnabled"] is not (wanted == "FADE"):
                    raise ValueError(f"{name}: Windows client-animation state is inconsistent.")
                status = "PASS" if kind == wanted else "INCONCLUSIVE" if kind == "INCONCLUSIVE" else "FAIL"
                handoffs.append(status)
                print(f"{name}: {status} (expected {wanted}, observed {kind})")
    return 1 if code == 1 or "FAIL" in handoffs else 2 if code == 2 or "INCONCLUSIVE" in handoffs else 0


if __name__ == "__main__":
    if len(sys.argv) not in (3,4) or len(sys.argv) == 4 and sys.argv[3] != "--check-fade":
        sys.exit(__doc__)
    try:
        sys.exit(check(Path(sys.argv[1]), Path(sys.argv[2]), len(sys.argv) == 4))
    except (OSError, ValueError, KeyError, IndexError, TypeError, zipfile.BadZipFile) as error:
        sys.exit(f"ERROR: {error}")
