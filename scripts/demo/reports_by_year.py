#!/usr/bin/env python3
"""GET /v1/reports/by-year -- totals grouped by publish year."""
from mm_client import get, show, need_key

need_key()
show("GET /v1/reports/by-year", *get("/reports/by-year"))
