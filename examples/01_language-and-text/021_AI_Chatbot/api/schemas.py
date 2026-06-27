from pydantic import BaseModel, Field


class ChatRequest(BaseModel):
    message: str = Field(min_length=1)


class ImageRequest(BaseModel):
    message: str = Field(min_length=1)
    size: str = Field(default="1024x1024")
    progressive: bool = Field(default=True)
