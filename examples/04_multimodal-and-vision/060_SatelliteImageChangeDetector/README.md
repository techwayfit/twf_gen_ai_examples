# 060 - Satellite Image Change Detector (Python Web App)

A Python FastAPI app that compares two satellite images from public URLs and returns:

1. A concise change summary.
2. Structured change detections with severity and evidence.
3. Risk level and recommended follow-up actions.
4. Location-aware analysis notes when context is provided.

## Stack

- FastAPI
- Azure OpenAI vision-capable chat model
- Jinja2 + vanilla HTML/JS frontend
- SSE over fetch streaming for status updates and final structured output

## Project files

- app.py: composition root
- config.py: settings and environment helpers
- sse.py: SSE event formatter helper
- api/change_api.py: streaming image comparison route
- api/schemas.py: request model
- services/satellite_change_service.py: change detection service
- ui/web_ui.py: web page route handler
- templates/index.html: browser UI
- requirements.txt: dependencies
- .env.example: environment variables

## Setup

1. Create a Python virtual environment.
2. Install dependencies.

```bash
pip install -r requirements.txt
```

3. Create `.env` from `.env.example` and set your Azure OpenAI values.

## Run

```bash
uvicorn app:app --reload --host 0.0.0.0 --port 8060
```

Open http://localhost:8060

## API endpoint

- POST /api/change/stream
  - body: `{ "before_image_url": "...", "after_image_url": "...", "location_context": "coastal floodplain" }`
  - stream events: `status`, `result`, `error`, `done`

## Notes

- Both images must be reachable by the model.
- The detector is intended for rapid triage, not authoritative geospatial measurement.