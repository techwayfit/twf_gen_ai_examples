# 021 - AI Chatbot (Python Web App)

A simple Python web app with two streaming features:

1. Chat streaming: send text, receive token-by-token AI response.
2. Image streaming: send image prompt, receive streamed status events and final generated image.

## Stack

- FastAPI
- Azure OpenAI API (chat)
- Azure Foundry API (image generation)
- Jinja2 + vanilla HTML/JS frontend
- SSE (Server-Sent Events) over fetch streaming

## Project files

- app.py: composition root (registers routers and starts server)
- config.py: settings and environment helpers
- sse.py: SSE event formatter helper
- api/chat_api.py: chat streaming API route
- api/image_api.py: image streaming API route
- api/schemas.py: request models
- services/ai_message_service.py: Azure OpenAI chat service
- services/ai_image_service.py: Azure Foundry image service
- ui/web_ui.py: web page route handler
- templates/index.html: browser UI for chat + image modes
- requirements.txt: dependencies
- .env.example: environment variables

## Setup

1. Create a Python virtual environment
2. Install dependencies

```bash
pip install -r requirements.txt
```

3. Create .env from .env.example and set your Azure URLs and keys:

```bash
AZURE_OPENAI_URL=https://your-openai-resource.openai.azure.com
AZURE_OPENAI_API_KEY=...
AZURE_OPENAI_API_VERSION=2024-10-21
AZURE_OPENAI_CHAT_DEPLOYMENT=gpt-4o-mini

AZURE_FOUNDRY_IMAGE_URL=https://your-foundry-endpoint/path/to/image/generation?api-version=2024-05-01-preview
AZURE_FOUNDRY_API_KEY=...
AZURE_FOUNDRY_IMAGE_MODEL=gpt-image-1

PORT=8000
```

## Run

```bash
uvicorn app:app --reload --host 0.0.0.0 --port 8000
```

Open http://localhost:8000

## API endpoints

- POST /api/chat/stream
  - body: { "message": "..." }
  - stream events: status, token, error, done

- POST /api/image/stream
  - body: { "message": "...", "size": "1024x1024" }
  - stream events: status, image, error, done

## Notes

- Chat is token streaming from Azure OpenAI using `chat.completions` stream mode.
- Image APIs generally return a completed image. To provide gradual rendering, this app performs progressive multi-pass generation (`draft` → `refined` → `final`) and streams each pass as an `image` event.
