from datetime import UTC, date, datetime


def utc(dt: datetime) -> datetime:
    """Naive UTC everywhere: SQL Server DATETIME2 via pyodbc does not round-trip offsets."""
    return dt if dt.tzinfo is None else dt.astimezone(UTC).replace(tzinfo=None)


def parse_dt(value: str) -> datetime:
    return utc(datetime.fromisoformat(value.replace("Z", "+00:00")))


def now() -> datetime:
    return datetime.now(UTC).replace(tzinfo=None)


def today() -> date:
    return now().date()


def date_key(d: date | datetime) -> int:
    return d.year * 10000 + d.month * 100 + d.day
