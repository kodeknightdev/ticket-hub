import logging
from contextlib import asynccontextmanager
from datetime import timedelta

from fastapi import FastAPI
from fastapi.responses import JSONResponse
from sqlalchemy import text

from . import jobs, telemetry
from .api import router
from .config import Settings, get_settings
from .db import Base, Database
from .ingest import Ingestor
from .messaging import ConsumerLoop
from .timeutil import now

log = logging.getLogger("analytics")


def run_migrations(settings: Settings, db: Database) -> None:
    if settings.database_url.startswith("sqlite"):
        Base.metadata.create_all(db.engine)
        return
    from alembic import command
    from alembic.config import Config

    cfg = Config("alembic.ini")
    cfg.set_main_option("sqlalchemy.url", settings.database_url.replace("%", "%%"))
    command.upgrade(cfg, "head")


def start_scheduler(settings: Settings, db: Database):
    from apscheduler.schedulers.background import BackgroundScheduler

    scheduler = BackgroundScheduler(timezone="UTC")
    first = now() + timedelta(seconds=30)
    scheduler.add_job(jobs.refresh, "interval", args=[db], minutes=settings.rollup_interval_minutes, next_run_time=first,
                      id="refresh-rollups", max_instances=1, coalesce=True)
    scheduler.add_job(jobs.retrain_all, "interval", args=[db], minutes=settings.retrain_interval_minutes,
                      next_run_time=first + timedelta(seconds=30), id="retrain-forecasts", max_instances=1, coalesce=True)
    scheduler.start()
    return scheduler


def create_app(settings: Settings | None = None) -> FastAPI:
    settings = settings or get_settings()

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        db = Database(settings.database_url)
        if settings.auto_migrate:
            run_migrations(settings, db)
        ingestor = Ingestor(db)
        app.state.db, app.state.ingestor = db, ingestor

        consumer = None
        if settings.kafka_enabled:
            consumer = ConsumerLoop(settings.kafka_bootstrap_servers, settings.kafka_group_id, ingestor.consumer_handlers(), settings.kafka_max_attempts)
            consumer.start()
        scheduler = start_scheduler(settings, db) if settings.scheduler_enabled else None
        yield
        if scheduler:
            scheduler.shutdown(wait=False)
        if consumer:
            consumer.stop()

    app = FastAPI(title="ticket-hub analytics", version="1.0.0", lifespan=lifespan)
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
