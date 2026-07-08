#!/usr/bin/env python3
"""GET /v1/health -- liveness, app version, active account, data freshness. No auth."""
from mm_client import get, show

show("GET /v1/health", *get("/health", auth=False))
