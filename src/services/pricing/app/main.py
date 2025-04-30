import asyncio
import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.responses import JSONResponse
from sqlalchemy import text

from . import telemetry
from .api import router
from .config import Settings, get_settings
from .db import Base, Database
from .handlers import PricingHandlers
from .messaging import ConsumerLoop, EventPublisher

log = logging.getLogger("pricing")


def run_migrations(settings: Settings, db: Database) -> None:
    if settings.database_url.startswith("sqlite"):
        Base.metadata.create_all(db.engine)
        return
    from alembic import command
    from alembic.config import Config

    cfg = Config("alembic.ini")
    cfg.set_main_option("sqlalchemy.url", settings.database_url.replace("%", "%%"))
    command.upgrade(cfg, "head")


def create_app(settings: Settings | None = None) -> FastAPI:
    settings = settings or get_settings()

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        db = Database(settings.database_url)
        if settings.auto_migrate:
            run_migrations(settings, db)
        publisher = EventPublisher(settings.kafka_bootstrap_servers if settings.kafka_enabled else None)
        handlers = PricingHandlers(db, publisher, settings.min_change_ratio)
        app.state.db, app.state.publisher, app.state.handlers = db, publisher, handlers

        consumer = None
        if settings.kafka_enabled:
            consumer = ConsumerLoop(settings.kafka_bootstrap_servers, settings.kafka_group_id, handlers.topics(), settings.kafka_max_attempts)
            consumer.start()

        async def periodic():
            while True:
                await asyncio.sleep(settings.recompute_interval_seconds)
                try:
                    await asyncio.to_thread(handlers.recompute_all)
                except Exception:  # noqa: BLE001
                    log.exception("periodic recompute failed")

        task = asyncio.create_task(periodic())
        yield
        task.cancel()
        if consumer:
            consumer.stop()
        publisher.flush()

    app = FastAPI(title="ticket-hub pricing", version="1.0.0", lifespan=lifespan)
    telemetry.configure(settings.service_name, settings.otel_exporter_otlp_endpoint, app)
    app.include_router(router)

    @app.get("/alive", tags=["health"])
    def alive():
        return {"status": "Healthy"}

    @app.get("/health", tags=["health"])
    def health():
        checks = []
        try:
            with app.state.db.engine.connect() as conn:
                conn.execute(text("SELECT 1"))
            checks.append({"name": "database", "status": "Healthy"})
        except Exception as exc:  # noqa: BLE001
            checks.append({"name": "database", "status": "Unhealthy", "error": str(exc)})
        status = "Healthy" if all(c["status"] == "Healthy" for c in checks) else "Unhealthy"
        return JSONResponse({"status": status, "checks": checks}, status_code=200 if status == "Healthy" else 503)

    return app


app = create_app()
