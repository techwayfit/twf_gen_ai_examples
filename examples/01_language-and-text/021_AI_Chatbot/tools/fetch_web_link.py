import re

import httpx


URL_PATTERN = re.compile(r"https?://[^\s)]+")


def extract_urls(text: str) -> list[str]:
    return URL_PATTERN.findall(text)


def fetch_content(url: str) -> str:
    try:
        with httpx.Client(timeout=15, follow_redirects=True) as client:
            resp = client.get(url, headers={"User-Agent": "Mozilla/5.0"})
            resp.raise_for_status()
            ctype = resp.headers.get("content-type", "")
            if "text/html" in ctype or "text/plain" in ctype:
                text = re.sub(r"<script[^>]*>.*?</script>", "", resp.text, flags=re.DOTALL)
                text = re.sub(r"<style[^>]*>.*?</style>", "", text, flags=re.DOTALL)
                text = re.sub(r"<[^>]+>", " ", text)
                text = re.sub(r"\s+", " ", text).strip()
                return text[:5000]
            return ""
    except Exception as ex:
        return f"[Error fetching {url}: {ex}]"
