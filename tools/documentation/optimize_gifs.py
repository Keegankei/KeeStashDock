"""Compress generated README GIFs without changing their RGB playback timeline.

Requires Pillow. Usage: python optimize_gifs.py INPUT_DIRECTORY OUTPUT_DIRECTORY
Source images stay untouched; existing destination files are never replaced.
"""

import argparse
import hashlib
from pathlib import Path
import shutil

from PIL import Image


def timeline(path):
    result = []
    with Image.open(path) as image:
        if image.info.get("loop") != 0:
            raise ValueError(f"Expected an infinitely looping GIF: {path.name}")
        for index in range(image.n_frames):
            image.seek(index)
            digest = hashlib.sha256(image.convert("RGB").tobytes()).hexdigest()
            duration = image.info["duration"]
            if result and result[-1][0] == digest:
                result[-1][1] += duration
            else:
                result.append([digest, duration])
    return result


def compress(source, target):
    if target.exists():
        raise FileExistsError(f"Refusing to replace {target}")
    frames, durations = [], []
    with Image.open(source) as image:
        palette = Image.new("P", (1, 1))
        palette.putpalette(image.getpalette())
        for index in range(image.n_frames):
            image.seek(index)
            rgb = image.convert("RGB")
            frame = rgb.quantize(palette=palette, dither=Image.Dither.NONE)
            if frame.convert("RGB").tobytes() != rgb.tobytes():
                raise ValueError("The shared palette would alter a frame's pixels")
            frames.append(frame)
            durations.append(image.info["duration"])
    frames[0].save(target, save_all=True, append_images=frames[1:],
                   duration=durations, loop=0, disposal=1, optimize=True)
    if timeline(source) != timeline(target):
        target.unlink()
        raise ValueError("Compression changed the playback pixels or timing")
    if target.stat().st_size >= source.stat().st_size:
        shutil.copyfile(source, target)
    with Image.open(target) as image:
        print(f"{source.name}: {source.stat().st_size} -> {target.stat().st_size} bytes; "
              f"{image.n_frames} encoded frames; identical RGB timeline")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    source = args.source.resolve(strict=True)
    destination = args.destination.resolve()
    if source == destination:
        parser.error("Use a separate output directory to preserve original captures")
    destination.mkdir(parents=True, exist_ok=True)
    for name in ("standard-panel-workflow.gif", "floating-panel-motion.gif"):
        compress(source / name, destination / name)


if __name__ == "__main__":
    main()
