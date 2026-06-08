#!/usr/bin/env python3
"""GET /v1/summary -- account headline totals (followers, views, reads, earnings)."""
from mm_client import get, show, need_key

need_key()
show("GET /v1/summary", *get("/summary"))
