from fastapi import APIRouter
from fastapi.responses import StreamingResponse

from api.schemas import ChangeDetectionRequest
from services.satellite_change_service import SatelliteChangeService
from sse import sse_event


router = APIRouter(prefix="/api/change", tags=["change"])
change_service = SatelliteChangeService()


@router.post("/stream")
def stream_change_detection(req: ChangeDetectionRequest) -> StreamingResponse:
    def event_generator():
        yield sse_event("status", {"message": "Images received. Preparing comparison..."})
        yield sse_event("status", {"message": "Inspecting before and after satellite scenes..."})

        try:
            analysis = change_service.analyze_change(
                req.before_image_url,
                req.after_image_url,
                req.location_context,
            )
            yield sse_event("result", analysis)
            yield sse_event("status", {"message": "Change detection completed."})
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