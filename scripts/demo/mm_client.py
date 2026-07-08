"""
Shared helper for the per-endpoint Medium Metrics API demo scripts.

Config via environment variables:
  MM_API_KEY   bearer key (Settings -> Local API -> Copy key)   [required except health]
  MM_API_PORT  loopback port (default 8780)
  MM_API_BASE  full base URL (default http://localhost:<port>/v1)

Pure standard library; no `pip install` needed.
"""
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

# Emit UTF-8 even when stdout is redirected to a file on Windows. The default console
# encoding (cp1252) can't encode non-Latin-1 characters that show up in titles/tags
# (e.g. accented letters), which otherwise crashes `python script.py > out.log`.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8")
    except Exception:
        pass

_PORT = os.environ.get("MM_API_PORT", "8780")
URL="https://dell717.tail23295d.ts.net/v1"
#URL=f"http://localhost:{_PORT}/v1"
BASE = os.environ.get("MM_API_BASE", URL)
KEY = os.environ.get("MM_API_KEY")


def get(path, auth=True, **params):
    """GET BASE+path with optional query params. Returns (status, parsed_body)."""
    url = BASE.rstrip("/") + path
    q = {k: v for k, v in params.items() if v is not None}
    if q:
        url += "?" + urllib.parse.urlencode(q)

    req = urllib.request.Request(url)
    if auth and KEY:
        req.add_header("Authorization", f"Bearer {KEY}")

    try:
        with urllib.request.urlopen(req, timeout=10) as r:
            return r.status, json.load(r)
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", "replace")
        try:
            body = json.loads(body)
        except ValueError:
            pass
        return e.code, body
    except urllib.error.URLError as e:
        sys.exit(f"ERROR: can't reach {url}: {e.reason}\n"
                 "Is the app running with Settings -> Local API enabled? "
                 "If the port isn't 8780, set MM_API_PORT or MM_API_BASE.")


def show(title, status, body):
    print(f"# {title}  [HTTP {status}]")
    print(json.dumps(body, indent=2, ensure_ascii=False))


def need_key():
    if not KEY:
        sys.exit("Set MM_API_KEY first (Settings -> Local API -> Copy key):\n"
                 '  PowerShell:  $env:MM_API_KEY = "<key>"\n'
                 "  bash:        export MM_API_KEY=<key>")
