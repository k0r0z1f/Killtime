#!/usr/bin/env python3
"""
===============================================================================
KILLTIME UNIVERSE - DYNAMIC PROGRESSION & METRICS SYNCHRONIZATION ENGINE
===============================================================================
Parses ground-truth manuscripts in /Killtime/manuscripts:
  1. Volume I (RC1): Killtime - Volume I - The Awakening Storm.html
  2. Volume I (Draft 1): Killtime.html
  3. Volume II (Draft): Killtime - Volume II.html

Calculates:
  - Exact word counts for each manuscript
  - Progress percentages against target volume word counts (180,000 words default)
  - Weighted totals:
      Vol Total = (Draft * 0.40) + (RC1 * 0.20) + (RC2 * 0.20) + (Gold * 0.20)
  - Global Project Progress = (Vol 1 + Vol 2 + Vol 3) / 3
  - Dynamic Volume I Chapter & Subchapter tree

Outputs:
  - Killtime/scripts_and_data/metrics.json
  - Killtime/scripts_and_data/metrics.js (window.KILLTIME_METRICS & window.KILLTIME_VOL1_CHAPTERS)
  - Updates fallback values in index.html (optional, default: True)
===============================================================================
"""

import os
import re
import json
import argparse
from html.parser import HTMLParser

BASE_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "../.."))
MANUSCRIPTS_DIR = os.path.join(BASE_DIR, "Killtime/manuscripts")
SCRIPTS_DATA_DIR = os.path.join(BASE_DIR, "Killtime/scripts_and_data")
INDEX_HTML_PATH = os.path.join(BASE_DIR, "index.html")

TARGET_WORDS_PER_VOL = 180000

class TagTextExtractor(HTMLParser):
    def __init__(self, target_class=None, target_tag=None):
        super().__init__()
        self.target_class = target_class
        self.target_tag = target_tag
        self.in_target = False
        self.depth = 0
        self.in_script = False
        self.in_style = False
        self.text_parts = []

    def handle_starttag(self, tag, attrs):
        if tag in ('script', 'style'):
            if tag == 'script': self.in_script = True
            if tag == 'style': self.in_style = True
            return

        attrs_dict = dict(attrs)
        classes = attrs_dict.get('class', '').split()

        match = False
        if self.target_class and self.target_class in classes:
            match = True
        elif self.target_tag and tag.lower() == self.target_tag.lower():
            match = True

        if match and not self.in_target:
            self.in_target = True
            self.depth = 1
        elif self.in_target:
            self.depth += 1

    def handle_endtag(self, tag):
        if tag == 'script':
            self.in_script = False
            return
        if tag == 'style':
            self.in_style = False
            return

        if self.in_target:
            self.depth -= 1
            if self.depth <= 0:
                self.in_target = False

    def handle_data(self, data):
        if (self.in_target or (not self.target_class and not self.target_tag)) and not self.in_script and not self.in_style:
            self.text_parts.append(data)

def count_words(text):
    return len([w for w in text.split() if len(w) > 0])

def parse_manuscript_words(file_path, target_class=None, target_tag=None):
    if not os.path.exists(file_path):
        print(f"[WARN] File not found: {file_path}")
        return 0

    with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
        html = f.read()

    extractor = TagTextExtractor(target_class=target_class, target_tag=target_tag)
    extractor.feed(html)
    full_text = ' '.join(extractor.text_parts)
    return count_words(full_text)

def extract_rc1_chapters(rc1_path):
    if not os.path.exists(rc1_path):
        return []

    with open(rc1_path, 'r', encoding='utf-8', errors='ignore') as f:
        html = f.read()

    ch_matches = list(re.finditer(r'<h2 class=\"chapter-title\" id=\"chapter-(\d+)\">Chapter \d+:\s*([^<]+)</h2>', html))
    manifest = [
        {"num": 0, "label": "Haven't started yet", "title": "Haven't started yet", "parts": []}
    ]

    for i, match in enumerate(ch_matches):
        ch_num = int(match.group(1))
        ch_title = match.group(2).strip()

        start_pos = match.end()
        end_pos = ch_matches[i+1].start() if i + 1 < len(ch_matches) else html.find('</article>', start_pos)
        if end_pos == -1:
            end_pos = len(html)
        ch_slice = html[start_pos:end_pos]

        part_matches = re.findall(r'<h3 id=\"chapter-' + str(ch_num) + r'-part-(\d+)\"[^>]*>([^<]+)</h3>', ch_slice)
        parts = []
        for p_num_str, p_header in part_matches:
            p_num = int(p_num_str)
            parts.append({
                "part": p_num,
                "id": f"chapter-{ch_num}-part-{p_num}",
                "label": p_header.strip()
            })

        manifest.append({
            "num": ch_num,
            "label": f"Ch {ch_num}: {ch_title}",
            "title": ch_title,
            "parts": parts
        })

    return manifest

def compute_all_metrics():
    vol1_rc1_file = os.path.join(MANUSCRIPTS_DIR, "Killtime - Volume I - The Awakening Storm.html")
    vol1_draft_file = os.path.join(MANUSCRIPTS_DIR, "Killtime.html")
    vol2_file = os.path.join(MANUSCRIPTS_DIR, "Killtime - Volume II.html")

    vol1_rc1_words = parse_manuscript_words(vol1_rc1_file, target_class="chapter-content")
    vol1_draft_words = parse_manuscript_words(vol1_draft_file)
    vol2_words = parse_manuscript_words(vol2_file, target_class="book-container")
    if vol2_words == 0:
        vol2_words = parse_manuscript_words(vol2_file, target_tag="body")

    vol1_draft_pct = 100.0  # Completed first draft
    vol1_rc1_pct = min(100.0, (vol1_rc1_words / TARGET_WORDS_PER_VOL) * 100.0)
    vol1_rc2_pct = 0.0
    vol1_gold_pct = 0.0
    vol1_total = (vol1_draft_pct * 0.40) + (vol1_rc1_pct * 0.20) + (vol1_rc2_pct * 0.20) + (vol1_gold_pct * 0.20)

    vol2_draft_pct = (vol2_words / TARGET_WORDS_PER_VOL) * 100.0
    vol2_rc1_pct = 0.0
    vol2_rc2_pct = 0.0
    vol2_gold_pct = 0.0
    vol2_total = (vol2_draft_pct * 0.40) + (vol2_rc1_pct * 0.20) + (vol2_rc2_pct * 0.20) + (vol2_gold_pct * 0.20)

    vol3_draft_pct = 0.0
    vol3_rc1_pct = 0.0
    vol3_rc2_pct = 0.0
    vol3_gold_pct = 0.0
    vol3_total = 0.0

    global_progress = (vol1_total + vol2_total + vol3_total) / 3.0

    chapters_manifest = extract_rc1_chapters(vol1_rc1_file)

    metrics = {
        "targetWordsPerVolume": TARGET_WORDS_PER_VOL,
        "volume1": {
            "title": "Volume I — The Awakening Storm",
            "draftWords": vol1_draft_words,
            "draftPct": round(vol1_draft_pct, 2),
            "rc1Words": vol1_rc1_words,
            "rc1Pct": round(vol1_rc1_pct, 2),
            "rc2Pct": round(vol1_rc2_pct, 2),
            "goldPct": round(vol1_gold_pct, 2),
            "totalPct": round(vol1_total, 2),
            "chapterCount": len([c for c in chapters_manifest if c["num"] > 0])
        },
        "volume2": {
            "title": "Volume II — The Living Current",
            "draftWords": vol2_words,
            "draftPct": round(vol2_draft_pct, 2),
            "rc1Pct": round(vol2_rc1_pct, 2),
            "rc2Pct": round(vol2_rc2_pct, 2),
            "goldPct": round(vol2_gold_pct, 2),
            "totalPct": round(vol2_total, 2)
        },
        "volume3": {
            "title": "Volume III — The River of Time",
            "draftWords": 0,
            "draftPct": 0.0,
            "rc1Pct": 0.0,
            "rc2Pct": 0.0,
            "goldPct": 0.0,
            "totalPct": 0.0
        },
        "globalProgress": round(global_progress, 2)
    }

    return metrics, chapters_manifest

def update_index_html(metrics):
    if not os.path.exists(INDEX_HTML_PATH):
        print(f"[WARN] index.html not found at: {INDEX_HTML_PATH}")
        return False

    with open(INDEX_HTML_PATH, 'r', encoding='utf-8') as f:
        content = f.read()

    v1_rc1 = f"{metrics['volume1']['rc1Pct']:.2f}%"
    v1_tot = f"{metrics['volume1']['totalPct']:.2f}%"
    v2_drf = f"{metrics['volume2']['draftPct']:.1f}%"
    v2_tot = f"{metrics['volume2']['totalPct']:.2f}%"
    glob = f"{metrics['globalProgress']:.2f}%"

    # 1. Update static SPANs in index.html
    content = re.sub(
        r'(<span id="sidebar-global-progress"[^>]*>)[^<]*(</span>)',
        r'\g<1>' + glob + r'\g<2>',
        content
    )
    content = re.sub(
        r'(<span id="global-project-progress-val"[^>]*>)[^<]*(</span>)',
        r'\g<1>' + glob + r'\g<2>',
        content
    )
    content = re.sub(
        r'(<span id="sidebar-rc1-progress"[^>]*>)[^<]*(</span>)',
        r'\g<1>' + v1_rc1 + r'\g<2>',
        content
    )
    content = re.sub(
        r'(<span id="rc1-progress-val"[^>]*>)[^<]*(</span>)',
        r'\g<1>' + v1_rc1 + r'\g<2>',
        content
    )
    content = re.sub(
        r'(<span id="sidebar-vol1-total"[^>]*>)[^<]*(</span>)',
        r'\g<1>' + v1_tot + r'\g<2>',
        content
    )
    content = re.sub(
        r'(<span id="vol1-total-val"[^>]*>)[^<]*(</span>)',
        r'\g<1>' + v1_tot + r'\g<2>',
        content
    )
    content = re.sub(
        r'(<span id="sidebar-vol2-draft"[^>]*>)[^<]*(</span>)',
        r'\g<1>' + v2_drf + r'\g<2>',
        content
    )
    content = re.sub(
        r'(<span id="vol2-draft-progress-val"[^>]*>)[^<]*(</span>)',
        r'\g<1>' + v2_drf + r'\g<2>',
        content
    )
    content = re.sub(
        r'(<span id="sidebar-vol2-total"[^>]*>)[^<]*(</span>)',
        r'\g<1>' + v2_tot + r'\g<2>',
        content
    )
    content = re.sub(
        r'(<span id="vol2-total-val"[^>]*>)[^<]*(</span>)',
        r'\g<1>' + v2_tot + r'\g<2>',
        content
    )

    # 2. Update initial JS fallback variables
    content = re.sub(
        r'let currentVol1Rc1 = [0-9.]+;',
        f'let currentVol1Rc1 = {metrics["volume1"]["rc1Pct"]};',
        content
    )
    content = re.sub(
        r'let currentVol2Draft = [0-9.]+;',
        f'let currentVol2Draft = {metrics["volume2"]["draftPct"]};',
        content
    )

    with open(INDEX_HTML_PATH, 'w', encoding='utf-8') as f:
        f.write(content)

    print("[SUCCESS] index.html progression spans and fallback variables updated.")
    return True

def main():
    parser = argparse.ArgumentParser(description="Synchronize Killtime progression metrics and chapter manifest.")
    parser.add_argument("--no-html-update", action="store_true", help="Do not modify index.html fallback numbers.")
    args = parser.parse_args()

    metrics, chapters = compute_all_metrics()

    # Save JSON
    json_path = os.path.join(SCRIPTS_DATA_DIR, "metrics.json")
    with open(json_path, 'w', encoding='utf-8') as f:
        json.dump({"metrics": metrics, "chapters": chapters}, f, indent=2)
    print(f"[SUCCESS] Wrote {json_path}")

    # Save JS (allows zero-CORS script inclusion across local file:// and HTTP)
    js_path = os.path.join(SCRIPTS_DATA_DIR, "metrics.js")
    js_content = f"""/**
 * Autogenerated by sync_metrics.py — Real-time Killtime Progression & Manifest
 */
window.KILLTIME_METRICS = {json.dumps(metrics, indent=2)};
window.KILLTIME_VOL1_CHAPTERS = {json.dumps(chapters, indent=2)};
"""
    with open(js_path, 'w', encoding='utf-8') as f:
        f.write(js_content)
    print(f"[SUCCESS] Wrote {js_path}")

    if not args.no_html_update:
        update_index_html(metrics)

    print("\n--- SYNCHRONIZATION SUMMARY ---")
    print(f"Volume I RC1 Words:    {metrics['volume1']['rc1Words']:,} ({metrics['volume1']['rc1Pct']}%)")
    print(f"Volume I Total:        {metrics['volume1']['totalPct']}%")
    print(f"Volume II Draft Words: {metrics['volume2']['draftWords']:,} ({metrics['volume2']['draftPct']}%)")
    print(f"Volume II Total:       {metrics['volume2']['totalPct']}%")
    print(f"Global Progress:       {metrics['globalProgress']}%")
    print(f"RC1 Chapter Count:     {metrics['volume1']['chapterCount']} chapters")

if __name__ == "__main__":
    main()
