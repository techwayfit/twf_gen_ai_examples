from fastapi import FastAPI
import uvicorn

from api.change_api import router as change_router
from config import Settings
from ui.web_ui import router as ui_router


app = FastAPI(title="060 Satellite Image Change Detector")


app.include_router(ui_router)
app.include_router(change_router)


if __name__ == "__main__":
    uvicorn.run("app:app", host="0.0.0.0", port=Settings.PORT, reload=False)