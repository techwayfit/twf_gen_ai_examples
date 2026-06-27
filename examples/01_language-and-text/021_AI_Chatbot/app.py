from fastapi import FastAPI
import uvicorn

from api.chat_api import router as chat_router
from api.image_api import router as image_router
from config import Settings
from ui.web_ui import router as ui_router


app = FastAPI(title="021 AI Chatbot")


app.include_router(ui_router)
app.include_router(chat_router)
app.include_router(image_router)


if __name__ == "__main__":
    uvicorn.run("app:app", host="0.0.0.0", port=Settings.PORT, reload=False)
