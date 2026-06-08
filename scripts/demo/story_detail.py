#!/usr/bin/env python3
"""GET /v1/stories/{id}/detail -- cached funnel/impact/referrers.
Returns 404 detail_not_cached until you open that story in the app.
Usage: python story_detail.py <storyId>"""
import sys
from mm_client import get, show, need_key

need_key()
if len(sys.argv) < 2:
    sys.exit("usage: python story_detail.py <storyId>")
sid = sys.argv[1]
show(f"GET /v1/stories/{sid}/detail", *get(f"/stories/{sid}/detail"))
