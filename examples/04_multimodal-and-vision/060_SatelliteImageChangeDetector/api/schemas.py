from pydantic import BaseModel, Field


class ChangeDetectionRequest(BaseModel):
    before_image_url: str = Field(min_length=1)
    after_image_url: str = Field(min_length=1)
    location_context: str = Field(default="")