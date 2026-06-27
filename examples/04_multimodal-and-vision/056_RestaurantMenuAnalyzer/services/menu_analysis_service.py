import json
from typing import Any

from openai import AzureOpenAI, NotFoundError

from config import Settings, normalize_azure_openai_endpoint, require_env


class MenuAnalysisService:
    def _get_client(self) -> AzureOpenAI:
        endpoint = normalize_azure_openai_endpoint(Settings.AZURE_OPENAI_URL or require_env("AZURE_OPENAI_URL"))
        api_key = Settings.AZURE_OPENAI_API_KEY or require_env("AZURE_OPENAI_API_KEY")
        return AzureOpenAI(
            api_key=api_key,
            azure_endpoint=endpoint,
            api_version=Settings.AZURE_OPENAI_API_VERSION,
        )

    def analyze_menu(self, image_url: str, dietary_notes: str) -> dict[str, Any]:
        client = self._get_client()
        system_prompt = (
            "You analyze restaurant menu photos for diners. "
            "Return concise, practical JSON only. "
            "Never invent certainty where the menu photo is unclear."
        )
        user_prompt = self._build_user_prompt(dietary_notes)

        try:
            response = client.chat.completions.create(
                model=Settings.AZURE_OPENAI_CHAT_DEPLOYMENT,
                messages=[
                    {"role": "system", "content": system_prompt},
                    {
                        "role": "user",
                        "content": [
                            {"type": "text", "text": user_prompt},
                            {"type": "image_url", "image_url": {"url": image_url}},
                        ],
                    },
                ],
                temperature=0.2,
                response_format={"type": "json_object"},
            )
        except NotFoundError as ex:
            endpoint = normalize_azure_openai_endpoint(Settings.AZURE_OPENAI_URL or require_env("AZURE_OPENAI_URL"))
            deployment = Settings.AZURE_OPENAI_CHAT_DEPLOYMENT
            raise RuntimeError(
                "Azure OpenAI returned 404. Check AZURE_OPENAI_URL and AZURE_OPENAI_CHAT_DEPLOYMENT. "
                f"Using endpoint={endpoint}, deployment={deployment}."
            ) from ex

        content = response.choices[0].message.content or "{}"
        if not isinstance(content, str):
            raise RuntimeError("Expected a JSON string response from the model.")

        parsed = json.loads(content)
        return self._normalize_output(parsed, image_url, dietary_notes)

    @staticmethod
    def _build_user_prompt(dietary_notes: str) -> str:
        notes = dietary_notes.strip() or "No dietary notes provided."
        return (
            "Analyze this restaurant menu photo and return JSON with these keys: "
            "restaurant_name, summary, dishes, recommendations, cautions, confidence. "
            "Each item in dishes must include name, section, description, estimated_calories, allergens, dietary_labels, and notes. "
            "Each recommendation must include audience, dishes, and rationale. "
            f"Use these diner preferences when useful: {notes}"
        )

    @staticmethod
    def _normalize_output(parsed: dict[str, Any], image_url: str, dietary_notes: str) -> dict[str, Any]:
        return {
            "image_url": image_url,
            "dietary_notes": dietary_notes,
            "restaurant_name": parsed.get("restaurant_name", "Unknown menu"),
            "summary": parsed.get("summary", "No summary provided."),
            "dishes": parsed.get("dishes", []),
            "recommendations": parsed.get("recommendations", []),
            "cautions": parsed.get("cautions", []),
            "confidence": parsed.get("confidence", "medium"),
        }