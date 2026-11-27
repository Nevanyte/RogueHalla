#!/usr/bin/env python3
"""Prepare the four reviewed loader pages for a single Unity Web build."""

import argparse
from pathlib import Path
import re


SITES = {
    "staging": ("alpha-staging.roguehalla.com", "latency-staging.roguehalla.com"),
    "live": ("play.roguehalla.com", "game.roguehalla.com"),
}
ROUTE_META = '<meta name="roguehalla-backend" content="same-origin">'


def replace_once(page, before, after):
    if page.count(before) != 1:
        raise ValueError(f"Expected exactly one reviewed loader marker {before!r}")
    return page.replace(before, after, 1)


def render(page, release, site, mode):
    frontend, backend = SITES[site]
    assets = f"/alpha-web/{release}"
    page = replace_once(page, 'const buildUrl = "Build";', f'const buildUrl = "{assets}/Build";')
    page = replace_once(
        page, 'streamingAssetsUrl: "StreamingAssets",',
        f'streamingAssetsUrl: "{assets}/StreamingAssets",',
    )
    page = re.sub(r'((?:href|src)=")TemplateData/', rf"\g<1>{assets}/TemplateData/", page)
    direct = f" https://{backend} wss://{backend}" if mode == "split" else ""
    policy = f"connect-src 'self' wss://{frontend}{direct}; object-src 'none'; base-uri 'none'"
    metadata = (
        f'<meta name="roguehalla-backend" content="{mode}">\n'
        f'    <meta http-equiv="Content-Security-Policy" content="{policy}">'
    )
    return replace_once(page, ROUTE_META, metadata)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("web_root", type=Path)
    parser.add_argument("output", type=Path, help="New private directory for loader variants")
    parser.add_argument("--release", required=True, help="Full Unity build commit")
    args = parser.parse_args()
    if not re.fullmatch("[0-9a-f]{40}", args.release):
        parser.error("release must be a full lowercase Git commit")
    page = (args.web_root / "index.html").read_text(encoding="utf-8")
    variants = {
        f"{site}-{mode}.html": render(page, args.release, site, mode)
        for site in SITES
        for mode in ("split", "same-origin")
    }
    args.output.mkdir()
    for name, variant in variants.items():
        (args.output / name).write_text(variant, encoding="utf-8")


if __name__ == "__main__":
    main()
