#!/usr/bin/env python3
"""GET /v1/history -- account totals over time (latest 10 rows)."""
from mm_client import get, show, need_key

need_key()
show("GET /v1/history", *get("/history", limit=10))
