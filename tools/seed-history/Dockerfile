# Build context is the repository root; reuses the analytics models so the schema can't drift.
FROM python:3.13.3-slim-bookworm

ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 PIP_NO_CACHE_DIR=1 PIP_DISABLE_PIP_VERSION_CHECK=1

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl gnupg2 ca-certificates unixodbc \
    && curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg \
    && curl -fsSL https://packages.microsoft.com/config/debian/12/prod.list -o /etc/apt/sources.list.d/mssql-release.list \
    && apt-get update \
    && ACCEPT_EULA=Y apt-get install -y --no-install-recommends msodbcsql18=18.5.1.1-1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /opt/analytics
COPY src/services/analytics/requirements.txt .
RUN pip install -r requirements.txt
COPY src/services/analytics/app/ app/
COPY tools/seed-history/seed_history.py /opt/seed/seed_history.py

ENV ANALYTICS_PATH=/opt/analytics
RUN useradd --system --uid 10001 --no-create-home seed
USER 10001
ENTRYPOINT ["python", "/opt/seed/seed_history.py"]
