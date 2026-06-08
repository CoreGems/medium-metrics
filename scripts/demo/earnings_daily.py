#!/usr/bin/env python3
"""GET /v1/reports/earnings/daily -- per-day earned + baseline.
Optional date range: python earnings_daily.py [from] [to]   (YYYY-MM-DD)"""
import sys
from mm_client import get, show, need_key

need_key()
frm = sys.argv[1] if len(sys.argv) > 1 else None
to = sys.argv[2] if len(sys.argv) > 2 else None
show("GET /v1/reports/earnings/daily", *get("/reports/earnings/daily", **{"from": frm, "to": to}))
