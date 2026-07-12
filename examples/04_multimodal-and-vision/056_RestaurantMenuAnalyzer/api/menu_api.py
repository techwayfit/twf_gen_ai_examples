from fastapi import APIRouter
from fastapi.responses import StreamingResponse

from api.schemas import MenuAnalysisRequest
from services.menu_analysis_service import MenuAnalysisService
from sse import sse_event


router = APIRouter(prefix="/api/menu", tags=["menu"])
menu_service = MenuAnalysisService()


@router.post("/stream")
def stream_menu_analysis(req: MenuAnalysisRequest) -> StreamingResponse:
    def event_generator():
        try:
            image_reference = req.resolve_image_reference()
            yield sse_event("status", {"message": "Image received. Preparing analysis..."})
            yield sse_event("status", {"message": "Inspecting menu items and dietary signals..."})

            analysis = menu_service.analyze_menu(image_reference, req.dietary_notes, req.image_name)
            yield sse_event("result", analysis)
            yield sse_event("status", {"message": "Menu analysis completed."})
            yield sse_event("done", {"ok": True})
        except Exception as ex:
            yield sse_event("error", {"message": str(ex)})

    return StreamingResponse(
        event_generator(),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
            "X-Accel-Buffering": "no",
        },
    )