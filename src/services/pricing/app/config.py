from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    service_name: str = "pricing"
    database_url: str = "sqlite:///./pricing.db"
    auto_migrate: bool = True
    kafka_enabled: bool = True
    kafka_bootstrap_servers: str = "localhost:9092"
    kafka_group_id: str = "pricing"
    kafka_max_attempts: int = 3
    recompute_interval_seconds: int = 300
    # changes smaller than this are noise and would just spam price-updated events
    min_change_ratio: float = 0.02
    otel_exporter_otlp_endpoint: str | None = None


@lru_cache
def get_settings() -> Settings:
    return Settings()
