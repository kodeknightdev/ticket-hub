from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    service_name: str = "analytics"
    database_url: str = "sqlite:///./analytics.db"
    auto_migrate: bool = True
    kafka_enabled: bool = True
    kafka_bootstrap_servers: str = "localhost:9092"
    kafka_group_id: str = "analytics"
    kafka_max_attempts: int = 3
    scheduler_enabled: bool = True
    rollup_interval_minutes: int = 5
    retrain_interval_minutes: int = 60
    otel_exporter_otlp_endpoint: str | None = None


@lru_cache
def get_settings() -> Settings:
    return Settings()
