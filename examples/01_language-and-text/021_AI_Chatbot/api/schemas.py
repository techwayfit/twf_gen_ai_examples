from pydantic import BaseModel, Field


class ChatRequest(BaseModel):
    message: str = Field(min_length=1)
    session_id: str = Field(default="")
    image_data: str = Field(default="")
    image_type: str = Field(default="")
    system_prompt: str = Field(default="")


class ImageRequest(BaseModel):
    message: str = Field(min_length=1)
    size: str = Field(default="1024x1024")
    progressive: bool = Field(default=True)
