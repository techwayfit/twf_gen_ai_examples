from collections.abc import Iterator

from openai import AzureOpenAI, NotFoundError

from config import Settings, normalize_azure_openai_endpoint, require_env


class AIMessageService:
    def _get_client(self) -> AzureOpenAI:
        endpoint = normalize_azure_openai_endpoint(Settings.AZURE_OPENAI_URL or require_env("AZURE_OPENAI_URL"))
        api_key = Settings.AZURE_OPENAI_API_KEY or require_env("AZURE_OPENAI_API_KEY")
        return AzureOpenAI(
            api_key=api_key,
            azure_endpoint=endpoint,
            api_version=Settings.AZURE_OPENAI_API_VERSION,
        )

    def stream_message(self, user_message: str) -> Iterator[str]:
        client = self._get_client()
        try:
            stream = client.chat.completions.create(
                model=Settings.AZURE_OPENAI_CHAT_DEPLOYMENT,
                messages=[
                    {"role": "system", "content": "You are a concise and helpful AI assistant."},
                    {"role": "user", "content": user_message},
                ],
                temperature=0.5,
                stream=True,
            )

            for chunk in stream:
                if not chunk.choices:
                    continue
                delta = chunk.choices[0].delta
                if delta and delta.content:
                    yield delta.content
        except NotFoundError as ex:
            endpoint = normalize_azure_openai_endpoint(Settings.AZURE_OPENAI_URL or require_env("AZURE_OPENAI_URL"))
            deployment = Settings.AZURE_OPENAI_CHAT_DEPLOYMENT
            raise RuntimeError(
                "Azure OpenAI returned 404. Check AZURE_OPENAI_URL and AZURE_OPENAI_CHAT_DEPLOYMENT. "
                f"Using endpoint={endpoint}, deployment={deployment}."
            ) from ex
