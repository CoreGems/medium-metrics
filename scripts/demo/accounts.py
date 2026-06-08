#!/usr/bin/env python3
"""GET /v1/accounts -- known accounts (id, handle, isActive, lastRefresh, storyCount)."""
from mm_client import get, show, need_key

need_key()
show("GET /v1/accounts", *get("/accounts"))
