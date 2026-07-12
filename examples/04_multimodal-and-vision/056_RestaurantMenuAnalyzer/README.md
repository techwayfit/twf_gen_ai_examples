# 056 - Restaurant Menu Analyzer (Python Web App)

A Python FastAPI app that analyzes a restaurant menu photo from a public image URL and returns:

1. Dish extraction with short descriptions.
2. Estimated calories and likely allergens.
3. Dietary labels such as vegetarian, vegan, halal-friendly, and gluten-aware.
4. Personalized recommendations based on dietary notes.

You can submit the image as a public URL, upload a local image file, or paste an image from the clipboard in the browser UI.

## Stack

- FastAPI
- Azure OpenAI vision-capable chat model
- Jinja2 + vanilla HTML/JS frontend
- SSE over fetch streaming for status updates and final structured output

## Project files

- app.py: composition root
- config.py: settings and environment helpers
- sse.py: SSE event formatter helper
- api/menu_api.py: streaming menu analysis route
- api/schemas.py: request model
- services/menu_analysis_service.py: vision analysis service
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
uvicorn app:app --reload --host 0.0.0.0 --port 8000
```

Open http://localhost:8000

The default `PORT` in `.env.example` is `8056`, so `uvicorn` can also be started with that value.

## API endpoint

- POST /api/menu/stream
  - body: `{ "image_url": "...", "image_data_url": "data:image/...", "dietary_notes": "nut allergy, high protein" }`
  - stream events: `status`, `result`, `error`, `done`

## Notes

- The image URL must be reachable by the model.
- Nutrition and allergen results are estimates, not medical advice.