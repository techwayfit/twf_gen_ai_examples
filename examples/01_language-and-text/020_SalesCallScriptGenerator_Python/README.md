# 020 - Sales Call Script Generator (Python)

This Python example demonstrates the reusable pattern used across most GenAI text apps in this repo:

- Keep workflow logic the same
- Change only the prompt template for each new use case

## What this example does

Given prospect and product details, it generates:

- an opening
- discovery questions
- value proposition bullets
- objection handling
- a close with clear CTA

## Why this is reusable

The engine is in app.py as PromptDrivenGenerator.

To build a different example (email drafting, FAQ bot, product description, etc.), you can:

1. keep PromptDrivenGenerator unchanged
2. add a new PromptTemplate in prompts.py
3. pass that template to generator.generate(...)

## Setup

1. Create and activate a virtual environment
2. Install dependencies:

```bash
pip install -r requirements.txt
```

3. Copy .env.example to .env and set your key:

```bash
OPENAI_API_KEY=...
OPENAI_MODEL=gpt-4o-mini
```

## Run

```bash
python app.py \
  --prospect-name "Asha Raman" \
  --role "Head of Operations" \
  --industry "Logistics" \
  --company-size "200-500" \
  --pain-point "Manual dispatch updates and delayed status visibility" \
  --product "FleetFlow Dispatch AI"
```

Optional JSON output:

```bash
python app.py ... --json
```

## Files

- app.py: reusable prompt-driven engine and CLI
- prompts.py: prompt templates (only place you usually change)
- requirements.txt: Python dependencies
- .env.example: environment variable template
