# WebApiPerformanceTesting

ASP.NET Web API with emulated load, **local Docker gateway with APIM-style subscription-key auth**, and NBomber load tests. No Terraform or Azure required for local runs.

## Contents

- **src/LoadDemoApi** – ASP.NET Core Web API with a single endpoint that emulates load (delay + optional CPU work).
- **src/LoadDemoApi.Gateway** – Reverse proxy (YARP) with **APIM-style authentication**: requires `Ocp-Apim-Subscription-Key` header before forwarding to the API.
- **docker-compose.yml** – Runs API + gateway locally with subscription-key auth enabled.
- **tests/LoadDemoApi.LoadTests** – NBomber load tests (direct to API or via gateway).

---

## Run locally (docker-compose + NBomber)

1. **Start the API and gateway** with docker-compose:

```bash
docker compose up --build
```

2. **In another terminal**, run the NBomber load tests against the gateway:

```bash
cd tests/LoadDemoApi.LoadTests
set LOAD_BASE_URL=http://localhost:8080
set APIM_SUBSCRIPTION_KEY=dev-key
dotnet run -c Release
```

Or run the script that does both (start compose, then run the test):

```powershell
.\run-load-test.ps1
```

Reports are written to `nbomber_report/` in the test project folder.

---

## Run with Docker (recommended: API + gateway with APIM auth)

Runs the API and a gateway that enforces **subscription-key auth at the API level** (same header as Azure APIM: `Ocp-Apim-Subscription-Key`). No Terraform.

```bash
docker compose up --build
```

- **API (direct):** http://localhost:5000/api/load — requires `Ocp-Apim-Subscription-Key: dev-key` (same as gateway when run via Docker).
- **Via gateway:** http://localhost:8080/api/load — requires `Ocp-Apim-Subscription-Key: dev-key`.

Default key is `dev-key`. Override:

```bash
set SUBSCRIPTION_KEY=my-secret-key
docker compose up --build
```

Run NBomber against the gateway (validates auth under load):

```bash
cd tests/LoadDemoApi.LoadTests
set LOAD_BASE_URL=http://localhost:8080
set APIM_SUBSCRIPTION_KEY=dev-key
dotnet run -c Release
```

---

## Run API and tests without Docker

**API only:**

```bash
cd src/LoadDemoApi
dotnet run
```

- Endpoint: `GET https://localhost:7xxx/api/load` (see launchSettings for port).
- Query params: `delayMs` (default 50), `workIterations` (default 1000).
- When run without Docker, the API does **not** require a subscription key (SubscriptionKey is empty). In Docker, both the API and the gateway require `Ocp-Apim-Subscription-Key: dev-key`.

**NBomber** (point at API or gateway):

```bash
cd tests/LoadDemoApi.LoadTests
set LOAD_BASE_URL=https://localhost:7xxx   # or http://localhost:8080 for gateway
set APIM_SUBSCRIPTION_KEY=dev-key        # only when targeting gateway
dotnet run -c Release
```

Load profiles (env `LOAD_PROFILE`): `low`, `medium`, `high`, `spike`, `constant`. Reports in `nbomber_report/`.

---

## Summary

| Mode              | API auth              | How |
|-------------------|------------------------|-----|
| **Docker**        | API and gateway require key | `docker compose up` → call :5000 or :8080 with `Ocp-Apim-Subscription-Key: dev-key` |
| **Local API only** | None (key not set)    | `dotnet run` in `src/LoadDemoApi`; no subscription key required |
