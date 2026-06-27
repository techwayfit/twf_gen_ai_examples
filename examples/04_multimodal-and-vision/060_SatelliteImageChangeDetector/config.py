import os
from pathlib import Path
from urllib.parse import urlparse

from dotenv import load_dotenv


load_dotenv()

BASE_DIR = Path(__file__).resolve().parent
TEMPLATES_DIR = BASE_DIR / "templates"


class Settings:
    CHAT_MODEL = os.getenv("OPENAI_CHAT_MODEL", "gpt-4o-mini")

    AZURE_OPENAI_URL = os.getenv("AZURE_OPENAI_URL", "")
    AZURE_OPENAI_API_KEY = os.getenv("AZURE_OPENAI_API_KEY", "")
    AZURE_OPENAI_API_VERSION = os.getenv("AZURE_OPENAI_API_VERSION", "2024-10-21")
    AZURE_OPENAI_CHAT_DEPLOYMENT = os.getenv("AZURE_OPENAI_CHAT_DEPLOYMENT", CHAT_MODEL)

    PORT = int(os.getenv("PORT", "8060"))


def require_env(name: str) -> str:
    value = os.getenv(name)
    if not value:
        raise RuntimeError(f"Missing required environment variable: {name}")
    return value


def normalize_azure_openai_endpoint(raw_url: str) -> str:
    url = raw_url.strip().rstrip("/")
    parsed = urlparse(url)
    if not parsed.scheme or not parsed.netloc:
        raise RuntimeError("AZURE_OPENAI_URL must be a full URL like https://your-resource.openai.azure.com")

    return f"{parsed.scheme}://{parsed.netloc}"