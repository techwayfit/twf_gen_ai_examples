from fastapi import APIRouter, Request
from fastapi.responses import HTMLResponse
from fastapi.templating import Jinja2Templates

from config import Settings, TEMPLATES_DIR


router = APIRouter(tags=["ui"])
templates = Jinja2Templates(directory=str(TEMPLATES_DIR))


@router.get("/", response_class=HTMLResponse)
async def index(request: Request) -> HTMLResponse:
    return templates.TemplateResponse(
        request=request,
        name="index.html",
        context={
            "chat_model": Settings.AZURE_OPENAI_CHAT_DEPLOYMENT,
            "image_model": Settings.AZURE_FOUNDRY_IMAGE_MODEL,
        },
    )
