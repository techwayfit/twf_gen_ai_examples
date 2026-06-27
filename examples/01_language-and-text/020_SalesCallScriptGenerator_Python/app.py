import argparse
import json
import os
from dataclasses import asdict, dataclass

from dotenv import load_dotenv
from openai import OpenAI

from prompts import SALES_CALL_SCRIPT_TEMPLATE, PromptTemplate


@dataclass
class SalesCallInput:
    prospect_name: str
    role: str
    industry: str
    company_size: str
    pain_point: str
    product: str


class PromptDrivenGenerator:
    """Reusable generation engine.

    To create a new example, keep this class unchanged and only swap
    PromptTemplate values in prompts.py.
    """

    def __init__(self, model: str) -> None:
        api_key = os.getenv("OPENAI_API_KEY")
        if not api_key:
            raise ValueError("OPENAI_API_KEY is not set. Add it to your environment or .env file.")
        self._client = OpenAI(api_key=api_key)
        self._model = model

    def generate(self, template: PromptTemplate, payload: dict[str, str]) -> str:
        user_prompt = template.user_prompt_template.format(**payload)

        response = self._client.responses.create(
            model=self._model,
            input=[
                {"role": "system", "content": template.system_prompt},
                {"role": "user", "content": user_prompt},
            ],
            temperature=0.4,
        )

        return response.output_text.strip()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate a sales call script with a prompt-driven workflow.")
    parser.add_argument("--prospect-name", required=True)
    parser.add_argument("--role", required=True)
    parser.add_argument("--industry", required=True)
    parser.add_argument("--company-size", required=True)
    parser.add_argument("--pain-point", required=True)
    parser.add_argument("--product", required=True)
    parser.add_argument("--json", action="store_true", help="Print structured output as JSON")
    return parser.parse_args()


def main() -> None:
    load_dotenv()
    model = os.getenv("OPENAI_MODEL", "gpt-4o-mini")

    args = parse_args()
    input_data = SalesCallInput(
        prospect_name=args.prospect_name,
        role=args.role,
        industry=args.industry,
        company_size=args.company_size,
        pain_point=args.pain_point,
        product=args.product,
    )

    generator = PromptDrivenGenerator(model=model)
    script = generator.generate(SALES_CALL_SCRIPT_TEMPLATE, asdict(input_data))

    if args.json:
        print(
            json.dumps(
                {
                    "template": SALES_CALL_SCRIPT_TEMPLATE.name,
                    "model": model,
                    "input": asdict(input_data),
                    "output": script,
                },
                indent=2,
            )
        )
        return

    print("\n=== Sales Call Script ===\n")
    print(script)


if __name__ == "__main__":
    main()
