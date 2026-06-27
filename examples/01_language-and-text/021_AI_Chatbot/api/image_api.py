import asyncio
from typing import AsyncGenerator

from fastapi import APIRouter
from fastapi.responses import StreamingResponse

from api.schemas import ImageRequest
from services.ai_image_service import AIImageService
from sse import sse_event


router = APIRouter(prefix="/api/image", tags=["image"])
image_service = AIImageService()


@router.post("/stream")
async def stream_image(req: ImageRequest) -> StreamingResponse:
    async def event_generator() -> AsyncGenerator[str, None]:
        yield sse_event("status", {"message": "Prompt received."})
        await asyncio.sleep(0.05)
        yield sse_event("status", {"message": "Generating image..."})

        try:
            if req.progressive:
                yield sse_event("status", {"message": "Generating draft pass..."})
                async for stage, kind, value in image_service.stream_progressive_images(req.message, req.size):
                    yield sse_event("image", {"stage": stage, kind: value})
                    if stage != "final":
                        yield sse_event("status", {"message": f"{stage.capitalize()} pass ready. Continuing..."})
            else:
                kind, value = await image_service.generate_image(req.message, req.size)
                yield sse_event("image", {"stage": "final", kind: value})

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
