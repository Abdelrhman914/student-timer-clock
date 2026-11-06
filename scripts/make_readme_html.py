#!/usr/bin/env python3
"""Converts README.md into a single self-contained README.html (all pictures embedded) that opens in any browser."""
import base64, os, re, markdown

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
md = open(os.path.join(ROOT, "README.md"), encoding="utf-8").read()

def embed(m):
    path = os.path.join(ROOT, m.group(2))
    if not os.path.exists(path):
        return m.group(0)
    data = base64.b64encode(open(path, "rb").read()).decode()
    return f'{m.group(1)}data:image/png;base64,{data}{m.group(3)}'

body = markdown.markdown(md, extensions=["tables", "fenced_code", "toc", "md_in_html"], output_format="html5")
body = re.sub(r'(<img[^>]*?src=")(docs/images/[^"]+\.png)(")', embed, body)
# image followed by an italic caption -> figure
body = re.sub(r'<p>(<img [^>]+>)\s*<em>(.*?)</em></p>', r'<figure>\1<figcaption>\2</figcaption></figure>', body, flags=re.S)
body = re.sub(r'<p>(<img [^>]+>)</p>', r'<figure>\1</figure>', body)

css = """
:root { --navy:#14284B; --blue:#0078D4; --line:#dde3ea; --soft:#f4f7fb; }
* { box-sizing:border-box; }
body { margin:0; background:#eef2f7; font:15px/1.6 "Segoe UI", system-ui, -apple-system, "Helvetica Neue", Arial, sans-serif; color:#1d2530; }
main { max-width:1040px; margin:28px auto; background:#fff; padding:36px 56px 48px; border-radius:10px; box-shadow:0 2px 14px rgba(20,40,75,.10); }
h1 { font-size:38px; color:var(--navy); margin:0 0 4px; letter-spacing:-.5px; }
h1 + p { color:#5f6670; margin-top:0; }
h2 { color:var(--navy); font-size:24px; margin:38px 0 12px; padding-bottom:6px; border-bottom:2px solid var(--line); }
h3 { color:var(--navy); font-size:18px; margin:24px 0 8px; }
a { color:var(--blue); text-decoration:none; } a:hover { text-decoration:underline; }
hr { border:0; border-top:1px solid var(--line); margin:30px 0; }
img { max-width:100%; height:auto; border-radius:6px; }
figure { margin:16px 0 22px; text-align:center; }
figure img { box-shadow:0 2px 10px rgba(0,0,0,.18); }
figcaption { color:#5f6670; font-size:13.5px; font-style:italic; margin-top:7px; }
table { border-collapse:collapse; width:100%; margin:12px 0 18px; font-size:14px; }
th, td { border:1px solid var(--line); padding:7px 10px; vertical-align:top; text-align:left; }
th { background:var(--soft); color:var(--navy); }
td img { box-shadow:0 1px 6px rgba(0,0,0,.18); }
table:has(img) td { border:0; text-align:center; font-size:13.5px; color:#3b4350; }
code { background:#f1f3f6; padding:1px 5px; border-radius:4px; font:13px Consolas, "Cascadia Mono", monospace; }
pre { background:#1e2733; color:#e6edf3; padding:14px 16px; border-radius:8px; overflow:auto; }
pre code { background:none; color:inherit; padding:0; }
blockquote { margin:16px 0; padding:10px 16px; background:#fff6e5; border-left:4px solid #e0b252; border-radius:4px; }
blockquote p { margin:4px 0; }
ol, ul { padding-left:24px; } li { margin:3px 0; }
@media print { body { background:#fff; } main { box-shadow:none; margin:0; max-width:none; padding:0 8px; } h2 { page-break-after:avoid; } figure, table { page-break-inside:avoid; } }
"""
html = f"""<!DOCTYPE html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Student Timer Clock 0.3 - Read me</title><style>{css}</style></head>
<body><main>
{body}
</main></body></html>"""
out = os.path.join(ROOT, "README.html")
open(out, "w", encoding="utf-8").write(html)
print(f"README.html  {os.path.getsize(out)/1024:.0f} KB, {html.count('data:image/png')} pictures embedded")
