#!/usr/bin/env python3
"""GET /v1/stories/{id} -- one story's metadata. Usage: python story.py <storyId>"""
import sys
from mm_client import get, show, need_key

need_key()
if len(sys.argv) < 2:
    sys.exit("usage: python story.py <storyId>   (get an id from stories.py)")
sid = sys.argv[1]
show(f"GET /v1/stories/{sid}", *get(f"/stories/{sid}"))
