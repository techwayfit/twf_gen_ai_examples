from pydantic import BaseModel, Field


class MenuAnalysisRequest(BaseModel):
    image_url: str = Field(default="")
    image_data_url: str = Field(default="")
    image_name: str = Field(default="")
    dietary_notes: str = Field(default="")

    def resolve_image_reference(self) -> str:
        image_data_url = self.image_data_url.strip()
        image_url = self.image_url.strip()

        if image_data_url:
            return image_data_url

        if image_url:
            return image_url

        raise ValueError("Provide a public image URL, upload an image file, or paste an image from the clipboard.")