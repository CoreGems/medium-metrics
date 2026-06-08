#!/usr/bin/env python3
"""GET /v1/stories -- top stories by views (limit 10). Optional: python stories.py <search>"""
import sys
from mm_client import get, show, need_key

need_key()
search = sys.argv[1] if len(sys.argv) > 1 else None
show("GET /v1/stories", *get("/stories", sort="views", order="desc", limit=10, search=search))
