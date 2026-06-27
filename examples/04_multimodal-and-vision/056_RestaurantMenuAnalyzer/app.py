from fastapi import FastAPI
import uvicorn

from api.menu_api import router as menu_router
from config import Settings
from ui.web_ui import router as ui_router


app = FastAPI(title="056 Restaurant Menu Analyzer")


app.include_router(ui_router)
app.include_router(menu_router)


if __name__ == "__main__":
    uvicorn.run("app:app", host="0.0.0.0", port=Settings.PORT, reload=False)