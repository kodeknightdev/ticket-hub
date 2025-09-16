from functools import lru_cache

import os

from pydantic import AliasChoices, Field
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
    # Same switches as the .NET services (Messaging__Provider, Secrets__Provider); local options are the default.
    messaging_provider: str = Field("kafka", validation_alias=AliasChoices("MESSAGING__PROVIDER", "messaging_provider"))
    eventhubs_connection_string: str | None = Field(
        None, validation_alias=AliasChoices("MESSAGING__EVENTHUBS__CONNECTIONSTRING", "eventhubs_connection_string"))

    def kafka_config(self) -> dict[str, str]:
        config = {"bootstrap.servers": self.kafka_bootstrap_servers}
        if self.messaging_provider.lower() == "eventhubs":
            if not self.eventhubs_connection_string:
                raise ValueError("MESSAGING__EVENTHUBS__CONNECTIONSTRING is required for the eventhubs provider")
            # Event Hubs' Kafka endpoint authenticates with the namespace connection string over SASL_SSL.
            config.update({"security.protocol": "SASL_SSL", "sasl.mechanisms": "PLAIN",
                           "sasl.username": "$ConnectionString", "sasl.password": self.eventhubs_connection_string})
        return config


def load_settings() -> Settings:
    """SECRETS__PROVIDER=keyvault also reads secrets from the files the Key Vault CSI driver mounts."""
    provider = os.getenv("SECRETS__PROVIDER", "environment").lower()
    if provider == "keyvault":
        return Settings(_secrets_dir=os.getenv("SECRETS__PATH", "/mnt/secrets-store"))
    if provider != "environment":
        raise ValueError(f"Unknown SECRETS__PROVIDER '{provider}'")
    return Settings()


@lru_cache
def get_settings() -> Settings:
    return load_settings()
