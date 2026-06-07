#!/usr/bin/env python3
"""
Demo client for the Medium Metrics local API.

Exercises a handful of the read-only v1 endpoints and prints the JSON. Doubles as a
quick smoke test. Pure standard library — no `pip install` needed.

Usage:
    python demo_api.py --key <API_KEY>
    python demo_api.py                 # reads the key from the MM_API_KEY env var
    python demo_api.py --port 8765 --account @alex

Get the key from the app: Settings -> Local API -> Copy key, and make sure the API is
enabled and the app is running. See OPENAI_CUSTOM_GPT.md.
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request


def call(base, path, key=None, **params):
    """GET base+path with optional bearer key and query params. Returns (status, body)."""
    url = base.rstrip("/") + path
    q = {k: v for k, v in params.items() if v is not None}
    if q:
        url += "?" + urllib.parse.urlencode(q)

    req = urllib.request.Request(url)
    if key:
        req.add_header("Authorization", f"Bearer {key}")

    try:
        with urllib.request.urlopen(req, timeout=10) as resp:
            return resp.status, json.load(resp)
    except urllib.error.HTTPError as e:
        body = e.read().decode("utf-8", "replace")
        try:
            body = json.loads(body)
        except ValueError:
            pass
        return e.code, body
    except urllib.error.URLError as e:
        print(
            f"\nERROR: could not reach {url}\n  {e.reason}\n"
            "Is the app running with Settings -> Local API enabled (and the port right)?",
            file=sys.stderr,
        )
        sys.exit(2)


def show(title, status, body):
    print(f"\n=== {title}  [HTTP {status}] ===")
    print(json.dumps(body, indent=2, ensure_ascii=False))


def main():
    ap = argparse.ArgumentParser(description="Demo the Medium Metrics local API.")
    ap.add_argument("--host", default="localhost")
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument(
        "--key",
        default=os.environ.get("MM_API_KEY"),
        help="Bearer key (Settings -> Local API -> Copy key), or set MM_API_KEY.",
    )
    ap.add_argument("--account", default=None, help="Account id or @handle (default: active).")
    args = ap.parse_args()

    base = f"http://{args.host}:{args.port}/v1"
    acct = args.account
    print(f"Medium Metrics API demo -> {base}")

    # 1) /health is unauthenticated — confirm the server is up before anything else.
    status, health = call(base, "/health")
    show("GET /health (no auth)", status, health)
    if status != 200:
        sys.exit(1)

    if not args.key:
        print(
            "\nNo API key given — only /health was exercised. Pass --key <key> or set "
            "MM_API_KEY (Settings -> Local API -> Copy key) to try the rest.",
            file=sys.stderr,
        )
        return

    # 2) the authenticated, cache-only endpoints
    show("GET /accounts", *call(base, "/accounts", args.key))

    status, _ = call(base, "/summary", args.key, account=acct)
    show("GET /summary", status, _)
    if status == 409:
        print("\nNo snapshot yet for this account — click Refresh in the app, then re-run.",
              file=sys.stderr)

    show("GET /stories?sort=views&limit=5",
         *call(base, "/stories", args.key, account=acct, sort="views", order="desc", limit=5))

    show("GET /tags?sort=earnings",
         *call(base, "/tags", args.key, account=acct, sort="earnings"))

    show("GET /reports/by-year", *call(base, "/reports/by-year", args.key, account=acct))

    show("GET /reports/earnings/daily",
         *call(base, "/reports/earnings/daily", args.key, account=acct))

    show("GET /history?limit=5", *call(base, "/history", args.key, account=acct, limit=5))

    # light smoke assertions
    assert health.get("status") == "ok", "health.status should be 'ok'"
    print("\nDone.")


if __name__ == "__main__":
    main()
