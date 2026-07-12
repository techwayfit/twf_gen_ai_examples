from pathlib import Path

from openai import AzureOpenAI

from config import Settings, normalize_azure_openai_endpoint, require_env
from tools.persistence import load_sessions, save_sessions


class ConversationManager:
    def __init__(self, max_history: int = 6, persistence_path: str | None = None):
        self._sessions: dict[str, list[dict]] = {}
        self._summaries: dict[str, str] = {}
        self._max_history = max_history
        self._path = Path(persistence_path) if persistence_path else Path(__file__).resolve().parent.parent / "chat_sessions.json"
        self._sessions, self._summaries = load_sessions(self._path)

    def get_history(self, session_id: str) -> list[dict]:
        if session_id not in self._sessions:
            self._sessions[session_id] = []
            self._summaries[session_id] = ""
        return self._sessions[session_id]

    def add_turn(self, session_id: str, user_msg: str, assistant_msg: str):
        history = self.get_history(session_id)
        history.append({"role": "user", "content": user_msg})
        history.append({"role": "assistant", "content": assistant_msg})
        save_sessions(self._path, self._sessions, self._summaries)

    def build_context(self, session_id: str, system_prompt: str, user_message: str) -> list[dict]:
        history = self.get_history(session_id)
        summary = self._summaries.get(session_id, "")

        messages = [{"role": "system", "content": system_prompt}]
        if summary:
            messages.append({"role": "system", "content": f"[Previous conversation summary]: {summary}"})
        messages.extend(history)
        messages.append({"role": "user", "content": user_message})

        return messages

    def needs_compact(self, session_id: str) -> bool:
        return len(self._sessions.get(session_id, [])) >= self._max_history * 2

    def compact(self, session_id: str):
        history = self._sessions.get(session_id, [])
        if len(history) < self._max_history:
            return

        keep = (self._max_history // 2) * 2
        to_summarize = history[:-keep] if keep > 0 else history
        recent = history[-keep:] if keep > 0 else []

        summary_text = self._summaries.get(session_id, "")
        for msg in to_summarize:
            summary_text += f"{msg['role']}: {msg['content']}\n"

        try:
            client = AzureOpenAI(
                api_key=Settings.AZURE_OPENAI_API_KEY or require_env("AZURE_OPENAI_API_KEY"),
                azure_endpoint=normalize_azure_openai_endpoint(
                    Settings.AZURE_OPENAI_URL or require_env("AZURE_OPENAI_URL")
                ),
                api_version=Settings.AZURE_OPENAI_API_VERSION,
            )
            response = client.chat.completions.create(
                model=Settings.AZURE_OPENAI_CHAT_DEPLOYMENT,
                messages=[
                    {
                        "role": "system",
                        "content": "Summarize the conversation concisely, keeping important facts and context.",
                    },
                    {"role": "user", "content": summary_text},
                ],
                temperature=0.3,
            )
            self._summaries[session_id] = response.choices[0].message.content or ""
        except Exception:
            pass

        self._sessions[session_id] = recent
        save_sessions(self._path, self._sessions, self._summaries)
