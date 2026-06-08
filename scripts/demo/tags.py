#!/usr/bin/env python3
"""GET /v1/tags -- per-tag metrics, sorted by earnings."""
from mm_client import get, show, need_key

need_key()
show("GET /v1/tags", *get("/tags", sort="earnings", order="desc"))
