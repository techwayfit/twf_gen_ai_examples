import json
from pathlib import Path


def load_sessions(path: Path) -> tuple[dict[str, list[dict]], dict[str, str]]:
    try:
        if path.exists():
            data = json.loads(path.read_text(encoding="utf-8"))
            return data.get("sessions", {}), data.get("summaries", {})
    except (json.JSONDecodeError, OSError):
        pass
    return {}, {}


def save_sessions(path: Path, sessions: dict, summaries: dict):
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(
            json.dumps({"sessions": sessions, "summaries": summaries}, ensure_ascii=False),
            encoding="utf-8",
        )
    except OSError:
        pass
