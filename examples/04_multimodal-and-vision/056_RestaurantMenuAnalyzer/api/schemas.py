from pydantic import BaseModel, Field


class MenuAnalysisRequest(BaseModel):
    image_url: str = Field(min_length=1)
    dietary_notes: str = Field(default="")