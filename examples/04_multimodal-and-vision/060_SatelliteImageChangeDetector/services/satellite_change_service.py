import json
from typing import Any

from openai import AzureOpenAI, NotFoundError

from config import Settings, normalize_azure_openai_endpoint, require_env


class SatelliteChangeService:
    def _get_client(self) -> AzureOpenAI:
        endpoint = normalize_azure_openai_endpoint(Settings.AZURE_OPENAI_URL or require_env("AZURE_OPENAI_URL"))
        api_key = Settings.AZURE_OPENAI_API_KEY or require_env("AZURE_OPENAI_API_KEY")
        return AzureOpenAI(
            api_key=api_key,
            azure_endpoint=endpoint,
            api_version=Settings.AZURE_OPENAI_API_VERSION,
        )

    def analyze_change(self, before_image_url: str, after_image_url: str, location_context: str) -> dict[str, Any]:
        client = self._get_client()
        system_prompt = (
            "You compare satellite images for environmental and infrastructure change detection. "
            "Return concise, structured JSON only. "
            "Be explicit when evidence is uncertain or obstructed."
        )
        user_prompt = self._build_user_prompt(location_context)

        try:
            response = client.chat.completions.create(
                model=Settings.AZURE_OPENAI_CHAT_DEPLOYMENT,
                messages=[
                    {"role": "system", "content": system_prompt},
                    {
                        "role": "user",
                        "content": [
                            {"type": "text", "text": user_prompt},
                            {"type": "image_url", "image_url": {"url": before_image_url}},
                            {"type": "text", "text": "This is the earlier image."},
                            {"type": "image_url", "image_url": {"url": after_image_url}},
                            {"type": "text", "text": "This is the later image."},
                        ],
                    },
                ],
                temperature=0.1,
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
        return self._normalize_output(parsed, before_image_url, after_image_url, location_context)

    @staticmethod
    def _build_user_prompt(location_context: str) -> str:
        context = location_context.strip() or "No additional location context provided."
        return (
            "Compare these two satellite images and return JSON with keys: summary, primary_change_type, changes, risk_level, recommended_actions, confidence, and caveats. "
            "Each entry in changes must include title, severity, evidence, impact, region_hint, and confidence. "
            f"Use this location or mission context if relevant: {context}"
        )

    @staticmethod
    def _normalize_output(
        parsed: dict[str, Any],
        before_image_url: str,
        after_image_url: str,
        location_context: str,
    ) -> dict[str, Any]:
        return {
            "before_image_url": before_image_url,
            "after_image_url": after_image_url,
            "location_context": location_context,
            "summary": parsed.get("summary", "No summary provided."),
            "primary_change_type": parsed.get("primary_change_type", "unknown"),
            "changes": parsed.get("changes", []),
            "risk_level": parsed.get("risk_level", "medium"),
            "recommended_actions": parsed.get("recommended_actions", []),
            "confidence": parsed.get("confidence", "medium"),
            "caveats": parsed.get("caveats", []),
        }