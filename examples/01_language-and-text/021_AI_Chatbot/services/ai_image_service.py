import httpx
from collections.abc import AsyncIterator

from config import Settings, require_env


class AIImageService:
    async def _call_image_api(self, prompt: str, size: str) -> tuple[str, str]:
        foundry_url = Settings.AZURE_FOUNDRY_IMAGE_URL or require_env("AZURE_FOUNDRY_IMAGE_URL")
        foundry_key = Settings.AZURE_FOUNDRY_API_KEY or require_env("AZURE_FOUNDRY_API_KEY")

        payload = {
            "prompt": prompt,
            "size": size,
            "model": Settings.AZURE_FOUNDRY_IMAGE_MODEL,
        }

        async with httpx.AsyncClient(timeout=120) as http_client:
            response = await http_client.post(
                foundry_url,
                headers={
                    "Content-Type": "application/json",
                    "api-key": foundry_key,
                },
                json=payload,
            )
            if response.status_code == 404:
                raise RuntimeError(
                    "Azure Foundry returned 404. Verify AZURE_FOUNDRY_IMAGE_URL path and api-version. "
                    f"URL used: {foundry_url}"
                )
            response.raise_for_status()
            response_json = response.json()

        return self._extract_image_reference(response_json)

    @staticmethod
    def _extract_image_reference(response_json: dict) -> tuple[str, str]:
        data = response_json.get("data")
        if isinstance(data, list) and data:
            first = data[0]
            if isinstance(first, dict):
                if first.get("b64_json"):
                    return "data_url", f"data:image/png;base64,{first['b64_json']}"
                if first.get("url"):
                    return "url", first["url"]

        output = response_json.get("output")
        if isinstance(output, list) and output:
            first = output[0]
            if isinstance(first, dict):
                if first.get("b64_json"):
                    return "data_url", f"data:image/png;base64,{first['b64_json']}"
                if first.get("image_url"):
                    return "url", first["image_url"]

        if response_json.get("b64_json"):
            return "data_url", f"data:image/png;base64,{response_json['b64_json']}"

        raise RuntimeError("Could not find image content in Azure Foundry response.")

    async def generate_image(self, prompt: str, size: str) -> tuple[str, str]:
        return await self._call_image_api(prompt, size)

    async def stream_progressive_images(self, prompt: str, size: str) -> AsyncIterator[tuple[str, str, str]]:
        stages = [
            ("draft", "Create a rough low-detail draft composition of this request:"),
            ("refined", "Create a refined and cleaner version of this request:"),
            ("final", "Create the final high-quality image for this request:"),
        ]

        for stage, prefix in stages:
            kind, value = await self._call_image_api(f"{prefix}\n{prompt}", size)
            yield stage, kind, value
