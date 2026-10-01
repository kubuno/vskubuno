//! $moduletitle$ - a Kubuno module: a process of its own that the core starts, supervises and proxies
//! (the core strips `/api/v1/$moduleid$` from `/api/v1/$moduleid$/...` and forwards the rest to its `[server] port`:
//! `/api/v1/$moduleid$/hello` reaches the `/hello` route below).
//!
//! Under a core, everything comes from the environment the core gives the module: `KUBUNO_INTERNAL_SECRET`,
//! `KUBUNO_DB_HOST`/`PORT`/`USER`/`PASSWORD`/`NAME`, `KUBUNO_MODULE_DIR`... Run alone (`cargo run`),
//! `DATABASE_URL` and `PORT` are accepted instead (see config.toml.example).
//!
//! Conventions: no `unwrap()` outside tests (`?`, `expect()` at bootstrap only); every database error is logged
//! before it becomes a response; `/internal/*` refuses any request without the right `X-Internal-Secret`; security
//! headers on every response; every table in the module's own schema; no process is ever spawned.

use std::net::SocketAddr;
use std::sync::Arc;
use std::time::Duration;

use axum::extract::State;
use axum::http::{HeaderName, HeaderValue, StatusCode};
use axum::middleware::{self, Next};
use axum::response::{IntoResponse, Response};
use axum::routing::get;
use axum::{Json, Router};
use serde::{Deserialize, Serialize};
use sqlx::postgres::{PgConnectOptions, PgPoolOptions};
use sqlx::PgPool;
use tower_http::set_header::SetResponseHeaderLayer;
use tower_http::trace::TraceLayer;

/// The module id: module.toml's `[module] id`, and the PostgreSQL schema this module owns.
const MODULE_ID: &str = "$moduleid$";

/// module.toml's `[server] port`: where the core expects the module.
const DEFAULT_PORT: u16 = 3199;

#[derive(Clone)]
struct AppState {
    db: PgPool,
    internal_secret: Arc<str>,
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new("info")),
        )
        .init();

    // No process is ever spawned by a module: the seccomp filter makes a regression impossible on Linux.
    kubuno_seccomp::lock_down_process_execution(MODULE_ID);

    let internal_secret: Arc<str> = std::env::var("KUBUNO_INTERNAL_SECRET")
        .expect("KUBUNO_INTERNAL_SECRET must be set (the core sets it)")
        .into();

    let db = PgPoolOptions::new()
        .max_connections(10)
        .connect_with(connect_options()?)
        .await?;
    // The schema first, so sqlx's own migration table lands in it too (search_path, below).
    sqlx::query("CREATE SCHEMA IF NOT EXISTS \"$moduleid$\"")
        .execute(&db)
        .await?;
    sqlx::migrate!("./migrations").run(&db).await?;

    let core_secret = Arc::clone(&internal_secret);
    let state = AppState { db, internal_secret };

    let internal_routes = Router::new()
        .route("/ping", get(internal_ping))
        .layer(middleware::from_fn_with_state(state.clone(), require_internal_secret));

    let security_headers = tower::ServiceBuilder::new()
        .layer(SetResponseHeaderLayer::if_not_present(
            HeaderName::from_static("x-frame-options"),
            HeaderValue::from_static("DENY"),
        ))
        .layer(SetResponseHeaderLayer::if_not_present(
            HeaderName::from_static("x-content-type-options"),
            HeaderValue::from_static("nosniff"),
        ))
        .layer(SetResponseHeaderLayer::if_not_present(
            HeaderName::from_static("strict-transport-security"),
            HeaderValue::from_static("max-age=31536000; includeSubDomains"),
        ));

    let app = Router::new()
        .route("/health", get(health))
        .route("/hello", get(hello))
        .nest("/internal", internal_routes)
        .layer(security_headers)
        .layer(TraceLayer::new_for_http())
        .with_state(state);

    let port = std::env::var("PORT")
        .ok()
        .and_then(|value| value.parse().ok())
        .unwrap_or(DEFAULT_PORT);
    let addr = SocketAddr::from(([127, 0, 0, 1], port));
    tracing::info!(%addr, module = MODULE_ID, "listening");
    let listener = tokio::net::TcpListener::bind(addr).await?;

    // Under a core: announce the module (its routes, sidebar entries...) so the core proxies /api/v1/$moduleid$/...
    // to it, then keep the registration alive. Run alone (no KUBUNO_CORE_URL), there is nothing to register with.
    if let Ok(core_url) = std::env::var("KUBUNO_CORE_URL") {
        tokio::spawn(stay_registered(core_url, core_secret, port));
    }
    axum::serve(listener, app).await?;
    Ok(())
}

/// The connection the core describes (`KUBUNO_DB_*`), else `DATABASE_URL` for a module run alone.
fn connect_options() -> anyhow::Result<PgConnectOptions> {
    let options = match std::env::var("KUBUNO_DB_HOST") {
        Ok(host) if !host.is_empty() => {
            let port = std::env::var("KUBUNO_DB_PORT")
                .ok()
                .and_then(|value| value.parse().ok())
                .unwrap_or(5432);
            PgConnectOptions::new()
                .host(&host)
                .port(port)
                .username(&std::env::var("KUBUNO_DB_USER").unwrap_or_default())
                .password(&std::env::var("KUBUNO_DB_PASSWORD").unwrap_or_default())
                .database(&std::env::var("KUBUNO_DB_NAME").unwrap_or_default())
        }
        _ => {
            let url = std::env::var("DATABASE_URL").map_err(|_| {
                anyhow::anyhow!("no database: run under a Kubuno core, or set DATABASE_URL")
            })?;
            url.parse::<PgConnectOptions>()?
        }
    };
    Ok(options.options([("search_path", MODULE_ID)]))
}

/// module.toml, built into the executable: what the module announces to the core.
const MANIFEST: &str = include_str!("../module.toml");

#[derive(Deserialize)]
struct Manifest {
    module: ManifestModule,
    #[serde(default)]
    routes: Option<ManifestRoutes>,
    #[serde(default)]
    sidebar_items: Vec<serde_json::Value>,
}

#[derive(Deserialize)]
struct ManifestModule {
    display_name: Option<String>,
    description: Option<String>,
    version: String,
}

#[derive(Deserialize)]
struct ManifestRoutes {
    #[serde(default)]
    patterns: Vec<serde_json::Value>,
}

/// The body of `POST /internal/modules/register`, from module.toml.
fn registration(port: u16) -> anyhow::Result<serde_json::Value> {
    let manifest: Manifest = toml::from_str(MANIFEST)?;
    Ok(serde_json::json!({
        "module_id": MODULE_ID,
        "display_name": manifest.module.display_name,
        "description": manifest.module.description,
        "base_url": format!("http://127.0.0.1:{port}"),
        "version": manifest.module.version,
        "routes": manifest.routes.map(|routes| routes.patterns).unwrap_or_default(),
        "sidebar_items": manifest.sidebar_items,
        "subscribed_events": [],
    }))
}

/// Registers with the core (retrying until it answers), then sends a heartbeat every 30 s and registers again
/// when the core no longer knows the module (it restarted). Without the registration the core does not proxy the
/// module's API.
async fn stay_registered(core_url: String, secret: Arc<str>, port: u16) {
    let payload = match registration(port) {
        Ok(payload) => payload,
        Err(error) => {
            tracing::error!(%error, "module.toml could not be read: the module is not registered with the core");
            return;
        }
    };
    let http = reqwest::Client::new();
    let core_url = core_url.trim_end_matches('/').to_owned();
    register(&http, &core_url, &secret, &payload).await;
    let heartbeat = format!("{core_url}/internal/modules/{MODULE_ID}/heartbeat");
    loop {
        tokio::time::sleep(Duration::from_secs(30)).await;
        match http.post(&heartbeat).header("X-Internal-Secret", &*secret).send().await {
            Ok(response) if response.status() == reqwest::StatusCode::NOT_FOUND => {
                register(&http, &core_url, &secret, &payload).await;
            }
            Ok(response) if !response.status().is_success() => {
                tracing::warn!(status = %response.status(), "heartbeat refused by the core");
            }
            Ok(_) => {}
            Err(error) => tracing::warn!(%error, "heartbeat: core unreachable"),
        }
    }
}

async fn register(http: &reqwest::Client, core_url: &str, secret: &str, payload: &serde_json::Value) {
    let url = format!("{core_url}/internal/modules/register");
    for attempt in 1u64.. {
        match http.post(&url).header("X-Internal-Secret", secret).json(payload).send().await {
            Ok(response) if response.status().is_success() => {
                tracing::info!(module = MODULE_ID, "registered with the core");
                return;
            }
            Ok(response) => tracing::warn!(status = %response.status(), attempt, "registration refused by the core, retrying"),
            Err(error) => tracing::warn!(%error, attempt, "core unreachable, retrying the registration"),
        }
        tokio::time::sleep(Duration::from_secs((attempt * 2).min(30))).await;
    }
}

#[derive(Serialize)]
struct HealthBody {
    status: &'static str,
    module: &'static str,
}

async fn health() -> Json<HealthBody> {
    Json(HealthBody {
        status: "ok",
        module: MODULE_ID,
    })
}

#[derive(Serialize)]
struct HelloBody {
    message: String,
    items: i64,
}

async fn hello(State(state): State<AppState>) -> Response {
    match sqlx::query_scalar::<_, i64>("SELECT count(*) FROM items")
        .fetch_one(&state.db)
        .await
    {
        Ok(items) => Json(HelloBody {
            message: format!("Hello from {MODULE_ID}"),
            items,
        })
        .into_response(),
        Err(error) => {
            tracing::error!(%error, "hello: database query failed");
            StatusCode::INTERNAL_SERVER_ERROR.into_response()
        }
    }
}

async fn internal_ping(State(state): State<AppState>) -> Response {
    match sqlx::query_scalar::<_, i32>("SELECT 1").fetch_one(&state.db).await {
        Ok(_) => (StatusCode::OK, Json(serde_json::json!({ "pong": true }))).into_response(),
        Err(error) => {
            tracing::error!(%error, "internal_ping: database query failed");
            StatusCode::INTERNAL_SERVER_ERROR.into_response()
        }
    }
}

/// `/internal/*` refuses any request without the core's `X-Internal-Secret`.
async fn require_internal_secret(
    State(state): State<AppState>,
    request: axum::extract::Request,
    next: Next,
) -> Response {
    let provided = request
        .headers()
        .get("x-internal-secret")
        .and_then(|value| value.to_str().ok());

    match provided {
        Some(secret) if constant_time_eq(secret.as_bytes(), state.internal_secret.as_bytes()) => {
            next.run(request).await
        }
        _ => StatusCode::UNAUTHORIZED.into_response(),
    }
}

/// Compares two secrets without leaking where they differ through timing.
fn constant_time_eq(a: &[u8], b: &[u8]) -> bool {
    if a.len() != b.len() {
        return false;
    }
    a.iter().zip(b).fold(0u8, |acc, (x, y)| acc | (x ^ y)) == 0
}

#[cfg(test)]
mod tests {
    use super::constant_time_eq;

    #[test]
    fn secrets_compare_exactly() {
        assert!(constant_time_eq(b"secret", b"secret"));
        assert!(!constant_time_eq(b"secret", b"secreT"));
        assert!(!constant_time_eq(b"secret", b"secrets"));
    }
}
