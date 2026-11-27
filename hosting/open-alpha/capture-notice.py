#!/usr/bin/env python3
"""Generate the alpha collection notice while preparing an immutable Web release."""

import argparse
from pathlib import Path


MODES = {
    "metrics": "This alpha records gameplay activity and timing linked to a pseudonymous player ID.",
    "replays": "This alpha can save completed arena matches as private replays, including your in-game callsign.",
    "both": "This alpha records gameplay activity and timing and can save completed arena matches as private replays.",
}

LOADER_END = '      </section>\n      <p id="unity-build-title"'
NOTICE_NAME = "alpha-gameplay-data.html"


def publish(web_root: Path, mode: str):
    index = web_root / "index.html"
    notice = web_root / NOTICE_NAME
    html = index.read_text(encoding="utf-8")
    if html.count(LOADER_END) != 1 or NOTICE_NAME in html or notice.exists():
        raise ValueError("Expected an unmodified RogueHalla Web loader")

    summary = MODES[mode]
    details = []
    if mode in ("metrics", "both"):
        details.append(
            "Gameplay metrics include match joins and exits, movement and attack counts, "
            "combat timing, damage, deaths, and reconnects. A pseudonymous ID links "
            "activity across matches."
        )
    if mode in ("replays", "both"):
        details.append(
            "Private replays can include your in-game callsign and the actions processed "
            "by the match server. Authorized operators use them to review gameplay."
        )
    paragraph = "\n".join(f"  <p>{detail}</p>" for detail in details)
    footer = (
        f'        <p style="margin:0;color:var(--text);font:13px/1.45 Arial,sans-serif">{summary} '
        f'<a style="color:var(--gold)" href="/{NOTICE_NAME}" '
        'target="_blank" rel="noopener noreferrer">Gameplay data details</a></p>\n'
    )
    notice.write_text(
        f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="referrer" content="no-referrer">
  <title>RogueHalla alpha gameplay data</title>
  <style>
    body {{
      max-width: 42rem;
      margin: 3rem auto;
      padding: 0 1.25rem;
      background: #151411;
      color: #f2e6cb;
      font: 1rem/1.6 system-ui, sans-serif;
      color-scheme: dark;
    }}
    a {{ color: #f0cc81; }}
  </style>
</head>
<body>
<main>
  <h1>Alpha gameplay data</h1>
  <p>We collect the following data to understand how players learn and fight during the RogueHalla open alpha.</p>
{paragraph}
  <p>Authorized operators can review the encrypted capture files for 14 days.</p>
  <a href="/">Back to RogueHalla</a>
</main>
</body>
</html>
""",
        encoding="utf-8",
    )
    index.write_text(html.replace(LOADER_END, footer + LOADER_END), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("web_root", type=Path, help="Unpublished Web release directory")
    parser.add_argument("--mode", required=True, choices=MODES)
    args = parser.parse_args()
    publish(args.web_root, args.mode)


if __name__ == "__main__":
    main()
