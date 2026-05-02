#!/usr/bin/env python3
import argparse
from pathlib import Path


def get_gif_dimensions(path: Path):
    """
    Reads GIF width/height from the GIF header.

    GIF format:
    - Bytes 0-5: Signature/version, usually GIF87a or GIF89a
    - Bytes 6-7: Width, little-endian
    - Bytes 8-9: Height, little-endian
    """
    with path.open("rb") as f:
        header = f.read(10)

    if len(header) < 10 or header[:3] != b"GIF":
        raise ValueError("Not a valid GIF file")

    width = int.from_bytes(header[6:8], byteorder="little")
    height = int.from_bytes(header[8:10], byteorder="little")

    return width, height


def main():
    parser = argparse.ArgumentParser(
        description="List dimensions of all GIF files in a directory and subdirectories."
    )

    parser.add_argument(
        "directory",
        nargs="?",
        default=".",
        help="Directory to scan. Defaults to current directory."
    )

    args = parser.parse_args()
    root = Path(args.directory).resolve()

    if not root.exists():
        print(f"Directory does not exist: {root}")
        return

    if not root.is_dir():
        print(f"Path is not a directory: {root}")
        return

    gifs = sorted(root.rglob("*.gif"))

    if not gifs:
        print(f"No GIF files found in: {root}")
        return

    print(f"Scanning: {root}")
    print()
    print(f"{'Width':>8} {'Height':>8}  File")
    print("-" * 80)

    for gif in gifs:
        try:
            width, height = get_gif_dimensions(gif)
            relative_path = gif.relative_to(root)
            print(f"{width:>8} {height:>8}  {relative_path}")
        except Exception as ex:
            relative_path = gif.relative_to(root)
            print(f"{'ERROR':>8} {'ERROR':>8}  {relative_path}  ({ex})")


if __name__ == "__main__":
    main()
