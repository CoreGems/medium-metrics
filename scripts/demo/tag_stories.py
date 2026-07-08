#!/usr/bin/env python3
"""GET /v1/tags/{tag}/stories -- stories carrying a tag. Usage: python tag_stories.py <tag>"""
import sys
import urllib.parse
from mm_client import get, show, need_key

need_key()
if len(sys.argv) < 2:
    sys.exit("usage: python tag_stories.py <tag>   (e.g. python tag_stories.py Politics)")
tag = sys.argv[1]
show(f"GET /v1/tags/{tag}/stories", *get(f"/tags/{urllib.parse.quote(tag)}/stories"))
