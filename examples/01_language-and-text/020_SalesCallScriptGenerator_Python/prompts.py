"""Prompt templates for text examples.

Most examples differ only by prompt message. Keep all prompt text here so the
workflow logic stays unchanged.
"""

from dataclasses import dataclass


@dataclass(frozen=True)
class PromptTemplate:
    name: str
    system_prompt: str
    user_prompt_template: str


SALES_CALL_SCRIPT_TEMPLATE = PromptTemplate(
    name="sales_call_script",
    system_prompt=(
        "You are an expert B2B sales enablement assistant. "
        "Generate practical, concise scripts that are easy to speak aloud."
    ),
    user_prompt_template=(
        "Create a personalized cold call script for this prospect:\n"
        "- Prospect name: {prospect_name}\n"
        "- Role: {role}\n"
        "- Industry: {industry}\n"
        "- Company size: {company_size}\n"
        "- Pain point: {pain_point}\n"
        "- Product: {product}\n\n"
        "Output format:\n"
        "1) Opening (2-3 lines)\n"
        "2) Discovery questions (3 bullets)\n"
        "3) Value proposition (3 bullets)\n"
        "4) Objection handling for:\n"
        "   - \"Too expensive\"\n"
        "   - \"No time this quarter\"\n"
        "   - \"We already use another tool\"\n"
        "5) Close with a clear CTA\n"
    ),
)
