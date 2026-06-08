#!/usr/bin/env python3
"""GET /v1/tags/followers -- per-tag follower/subscriber gains from cached detail (+ coverage)."""
from mm_client import get, show, need_key

need_key()
show("GET /v1/tags/followers", *get("/tags/followers"))
