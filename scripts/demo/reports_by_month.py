#!/usr/bin/env python3
"""GET /v1/reports/by-month -- totals grouped by publish month (yyyy-MM)."""
from mm_client import get, show, need_key

need_key()
show("GET /v1/reports/by-month", *get("/reports/by-month"))
