import pytest

from app.config import Settings, load_settings


def test_kafka_is_the_default_transport(monkeypatch):
    monkeypatch.delenv("MESSAGING__PROVIDER", raising=False)
    config = Settings(kafka_bootstrap_servers="kafka:9092").kafka_config()
    assert config == {"bootstrap.servers": "kafka:9092"}


def test_eventhubs_adds_sasl(monkeypatch):
    monkeypatch.setenv("MESSAGING__PROVIDER", "eventhubs")
    monkeypatch.setenv("MESSAGING__EVENTHUBS__CONNECTIONSTRING", "Endpoint=sb://placeholder.servicebus.windows.net/;SharedAccessKey=x")
    config = Settings(kafka_bootstrap_servers="placeholder.servicebus.windows.net:9093").kafka_config()
    assert config["security.protocol"] == "SASL_SSL"
    assert config["sasl.username"] == "$ConnectionString"


def test_eventhubs_without_connection_string_fails(monkeypatch):
    monkeypatch.setenv("MESSAGING__PROVIDER", "eventhubs")
    monkeypatch.delenv("MESSAGING__EVENTHUBS__CONNECTIONSTRING", raising=False)
    with pytest.raises(ValueError):
        Settings().kafka_config()


def test_keyvault_secrets_are_read_from_the_mounted_files(monkeypatch, tmp_path):
    (tmp_path / "DATABASE_URL").write_text("sqlite:///from-key-vault.db")
    monkeypatch.setenv("SECRETS__PROVIDER", "keyvault")
    monkeypatch.setenv("SECRETS__PATH", str(tmp_path))
    monkeypatch.delenv("DATABASE_URL", raising=False)
    assert load_settings().database_url == "sqlite:///from-key-vault.db"


def test_unknown_secrets_provider_is_rejected(monkeypatch):
    monkeypatch.setenv("SECRETS__PROVIDER", "vault9000")
    with pytest.raises(ValueError):
        load_settings()
