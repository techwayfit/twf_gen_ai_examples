from fastapi import APIRouter
from fastapi.responses import StreamingResponse

from api.schemas import ChatRequest
from services.ai_message_service import AIMessageService
from sse import sse_event


router = APIRouter(prefix="/api/chat", tags=["chat"])
message_service = AIMessageService()


@router.post("/stream")
def stream_chat(req: ChatRequest) -> StreamingResponse:
    def event_generator():
        yield sse_event("status", {"message": "Starting chat stream..."})
        try:
            for token in message_service.stream_message(req.message):
                yield sse_event("token", {"text": token})

            yield sse_event("status", {"message": "Chat stream completed."})
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
