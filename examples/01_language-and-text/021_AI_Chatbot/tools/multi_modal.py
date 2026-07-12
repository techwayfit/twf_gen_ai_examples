def build_user_content(text: str, image_data: str = "", image_type: str = "") -> str | list[dict]:
    if not image_data:
        return text
    parts = [{"type": "text", "text": text}]
    parts.append({
        "type": "image_url",
        "image_url": {"url": f"data:{image_type};base64,{image_data}"},
    })
    return parts
