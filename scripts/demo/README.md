# API demo scripts (one per endpoint)

Tiny standard-library-only Python scripts — each calls **one** Medium Metrics local API
endpoint and prints the JSON. No `pip install`. They share `mm_client.py` for the base
URL / key / pretty-print.

## Setup
1. In the app: **Settings → Local API** → enable it, then **Copy key**.
2. Put the key (and port, if not 8780) in your shell:

   PowerShell:
   ```powershell
   $env:MM_API_KEY = "<paste key>"
   # $env:MM_API_PORT = "8783"     # only if the app fell back to another port
   ```
   bash:
   ```bash
   export MM_API_KEY=<paste key>
   ```

## Run (from this folder)
```
python health.py                  # no key needed
python accounts.py
python summary.py
python stories.py                 # top 10 by views
python stories.py python          # filter by "python"
python story.py <storyId>         # ids come from stories.py
python story_detail.py <storyId>  # 404 until that story is opened in the app
python tags.py
python tag_stories.py Politics
python followers_by_tag.py
python reports_by_year.py
python reports_by_month.py
python earnings_daily.py          # optional: earnings_daily.py 2026-05-25 2026-06-07
python history.py
```

Env vars: `MM_API_KEY` (required except `health`), `MM_API_PORT` (default `8780`),
`MM_API_BASE` (default `http://localhost:<port>/v1`).
