"""Builds default-packs/ (shipped with the plugin) from the full-size PNG packs in ThemePacks/.

Each PNG becomes a WebP at quality 88 (about one ninth of the size, no visible loss on banners) and
theme.json is rewritten to point at the WebP files. The source folder is never changed.

Usage: python scripts/convert_packs_to_webp.py [source] [target]
Requires Pillow.
"""
import json
import shutil
import sys
from pathlib import Path

from PIL import Image

QUALITY = 88


def convert(source: Path, target: Path) -> None:
    if target.exists():
        shutil.rmtree(target)
    for pack in sorted(p for p in source.iterdir() if p.is_dir()):
        manifest = json.loads((pack / "theme.json").read_text(encoding="utf-8"))
        out = target / pack.name
        out.mkdir(parents=True)
        converted = {}
        for slot, name in manifest.get("assets", {}).items():
            if name not in converted:
                if Path(name).suffix.lower() == ".png":
                    webp = str(Path(name).with_suffix(".webp")).replace("\\", "/")
                    Image.open(pack / name).save(out / webp, "WEBP", quality=QUALITY, method=6)
                    converted[name] = webp
                else:
                    shutil.copy2(pack / name, out / name)
                    converted[name] = name
            manifest["assets"][slot] = converted[name]
        (out / "theme.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        print(f"{pack.name}: {len(converted)} image(s)")


if __name__ == "__main__":
    root = Path(__file__).resolve().parent.parent
    convert(Path(sys.argv[1]) if len(sys.argv) > 1 else root / "ThemePacks",
            Path(sys.argv[2]) if len(sys.argv) > 2 else root / "default-packs")
