#!/usr/bin/env python3
import os
import re

# Base directory: Webserver root (/home/wa/Webserver)
BASE_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), "../.."))

EXCLUDE_DIRS = {
    'Temporary', '.git', '.agents', '.gemini', 'node_modules', 'venv', '__pycache__'
}

def should_skip(path):
    parts = path.split(os.sep)
    for p in parts:
        if p in EXCLUDE_DIRS:
            return True
    return False

def get_relative_theme_paths(html_file_path):
    rel_from_base = os.path.relpath(html_file_path, BASE_DIR)
    dir_of_file = os.path.dirname(rel_from_base)
    
    if not dir_of_file or dir_of_file == '.':
        # In Webserver root (e.g., index.html)
        css_path = "./Killtime/css/theme.css"
        js_path = "./Killtime/scripts_and_data/theme.js"
    else:
        # In subdirectories: compute path back to Killtime/
        depth = len(dir_of_file.split(os.sep))
        up_to_base = "../" * depth
        css_path = up_to_base + "Killtime/css/theme.css"
        js_path = up_to_base + "Killtime/scripts_and_data/theme.js"
        
        # Simplify if inside Killtime directory
        if rel_from_base.startswith("Killtime" + os.sep):
            rel_inside_killtime = os.path.relpath(html_file_path, os.path.join(BASE_DIR, "Killtime"))
            killtime_dir = os.path.dirname(rel_inside_killtime)
            if not killtime_dir or killtime_dir == '.':
                css_path = "./theme.css"
                js_path = "./theme.js"
            else:
                depth_k = len(killtime_dir.split(os.sep))
                css_path = ("../" * depth_k) + "theme.css"
                js_path = ("../" * depth_k) + "theme.js"

    return css_path, js_path

def process_html_file(file_path):
    try:
        with open(file_path, 'r', encoding='utf-8', errors='ignore') as f:
            content = f.read()
    except Exception as e:
        print(f"[ERROR] Could not read {file_path}: {e}")
        return False

    # Check if theme.js is already included
    if 'theme.js' in content:
        return False # already injected

    css_path, js_path = get_relative_theme_paths(file_path)
    injection = f'\n    <!-- Universal Dark/Light Mode Theme System -->\n    <link rel="stylesheet" href="{css_path}">\n    <script src="{js_path}"></script>\n'

    # Try inserting inside <head>
    if '<head>' in content:
        new_content = content.replace('<head>', '<head>' + injection, 1)
    elif '<HEAD>' in content:
        new_content = content.replace('<HEAD>', '<HEAD>' + injection, 1)
    elif '<head ' in content:
        # e.g. <head lang="...">
        match = re.search(r'(<head[^>]*>)', content, re.IGNORECASE)
        if match:
            head_tag = match.group(1)
            new_content = content.replace(head_tag, head_tag + injection, 1)
        else:
            new_content = injection + content
    elif '<html>' in content or '<html ' in content:
        match = re.search(r'(<html[^>]*>)', content, re.IGNORECASE)
        if match:
            html_tag = match.group(1)
            new_content = content.replace(html_tag, html_tag + '\n<head>' + injection + '</head>', 1)
        else:
            new_content = injection + content
    else:
        new_content = injection + content

    with open(file_path, 'w', encoding='utf-8') as f:
        f.write(new_content)
    
    return True

def main():
    print(f"Scanning from: {BASE_DIR}")
    html_files = []
    for root, dirs, files in os.walk(BASE_DIR):
        dirs[:] = [d for d in dirs if d not in EXCLUDE_DIRS]
        for file in files:
            if file.lower().endswith('.html'):
                full_path = os.path.join(root, file)
                if not should_skip(full_path):
                    html_files.append(full_path)

    print(f"Found {len(html_files)} HTML files to inspect.")
    injected_count = 0
    for hf in html_files:
        if process_html_file(hf):
            injected_count += 1

    print(f"Successfully processed and injected theme engine into {injected_count} HTML files!")

if __name__ == '__main__':
    main()
