import re
import html
import sys
import os

def compress_for_llm(input_html_path, output_txt_path, minify_names=True):
    """
    Compresses a heavily formatted HTML story into an LLM-optimized dense text format.
    """
    if not os.path.exists(input_html_path):
        print(f"Error: Could not find '{input_html_path}'.")
        sys.exit(1)

    with open(input_html_path, 'r', encoding='utf-8') as f:
        raw_html = f.read()

    print("1. Extracting core text and removing HTML/CSS bloat...")
    # Isolate the body content to ignore CSS and header metadata
    body_match = re.search(r'<body.*?>(.*?)</body>', raw_html, re.DOTALL | re.IGNORECASE)
    text = body_match.group(1) if body_match else raw_html

    # Replace block-level HTML tags with newlines before stripping
    text = re.sub(r'</p>|<br\s*/?>|</h1>|</h2>|</h3>', '\n', text, flags=re.IGNORECASE)
    
    # Strip all remaining HTML tags
    text = re.sub(r'<[^>]+>', '', text)
    
    # Unescape HTML entities (e.g., &nbsp;, &amp;)
    text = html.unescape(text)

    print("2. Normalizing typography to reduce token overhead...")
    # Unicode quotes and dashes often take 2-3 tokens per character in LLMs.
    # Replacing them with standard ASCII reduces token count instantly.
    text = text.replace('“', '"').replace('”', '"')
    text = text.replace('‘', "'").replace('’', "'")
    text = text.replace('—', '-')
    text = text.replace('…', '...')

    print("3. Minifying whitespace...")
    # Split into lines, strip leading/trailing spaces, remove double spaces, and drop empty lines
    lines = text.split('\n')
    cleaned_lines =[]
    for line in lines:
        line = line.strip()
        line = re.sub(r'\s+', ' ', line)  # Collapse multiple spaces into one
        if line:
            cleaned_lines.append(line)
    
    text = '\n'.join(cleaned_lines)

    # 4. Semantic Compression (Name Minification)
    legend_header = ""
    if minify_names:
        print("4. Applying semantic compression (abbreviating entities)...")
        # Abbreviating highly repeated names saves thousands of tokens over a long context.
        # LLMs effortlessly track abbreviations when a legend is provided at the very top.
        name_map = {
            r'\bLucas\b': 'L',
            r'\bMina\b': 'M',
            r'\bThomas\b': 'T',
            r'\bCandice\b': 'C',
            r'\bAphyrosia\b': 'Aph',
            r'\bAltheris\b': 'Alt',
            r'\bDira\b': 'D',
            r'\bVermicalis\b': 'V',
            r'\bTorgar\b': 'Tor',
            r'\bGaldor\b': 'G',
            r'\bCeylan\b': 'Cey',
            r'\bElias Thorne\b': 'Thorne',
            r'\bThe Researcher\b': 'Thorne',
            r'\bThe Disciple\b': 'Disciple',
            r'\bChildren of Hybris\b': 'CoH',
            r'\bCult of the Sent Ones\b': 'CotSO',
            r'\bCult of the Unbound\b': 'CotU',
            r'\bBrum\'korath\b': 'Brum',
        }
        
        for full_name, short_name in name_map.items():
            text = re.sub(full_name, short_name, text)
        
        # Inject the legend at the very beginning so the LLM understands the abbreviations
        legend_header = (
            "[SYSTEM LEGEND FOR LLM: L=Lucas, M=Mina, T=Thomas, C=Candice, Aph=Aphyrosia, "
            "Alt=Altheris, D=Dira, V=Vermicalis, Tor=Torgar, G=Galdor, Cey=Ceylan, "
            "CoH=Children of Hybris, CotSO=Cult of the Sent Ones, CotU=Cult of the Unbound, "
            "Brum=Brum'korath]\n\n"
        )

    # Write the highly-compressed text to the output file
    print(f"Writing compressed story to '{output_txt_path}'...")
    with open(output_txt_path, 'w', encoding='utf-8') as f:
        f.write(legend_header + text)
        
    print("Done! The story is now optimized for LLM context windows.")

if __name__ == "__main__":
    # Define input/output filenames
    INPUT_FILE = "Killtime.html"
    OUTPUT_FILE = "Killtime_compressed.txt"
    
    compress_for_llm(INPUT_FILE, OUTPUT_FILE, minify_names=True)