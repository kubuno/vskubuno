//! $safeprojectname$ - a Kubuno module: a standalone Axum/Tokio process the core connects to at
//! startup, proxies routes for, and supervises (CLAUDE.md sections 1 and 4). This is a minimal,
//! buildable skeleton - fill in real routes/tables as the module grows.
//!
//! Conventions this file follows (CLAUDE.md section 7):
//! - zero `unwrap()` outside tests (`?`/`expect()` only at bootstrap, below);
//! - every DB error is logged with `tracing::error!` before being turned into a response;
//! - `/internal/*` refuses any request without a valid `X-Internal-Secret` header;
//! - security response headers are set on every response, not just the successful ones.

use std::net::SocketAddr;
use std::sync::Arc;

use axum::extract::State;
use axum::http::{HeaderName, HeaderValue, StatusCode};
use axum::middleware::{self, Next};
use axum::response::{IntoResponse, Response};
use axum::routing::get;
use axum::{Json, Router};
use serde::Serialize;
use sqlx::postgres::PgPoolOptions;
use sqlx::PgPool;
use tower_http::set_header::SetResponseHeaderLayer;
use tower_http::trace::TraceLayer;

/// Stable module id - also `module.toml`'s own `[module] id`, the Postgres schema this module owns
/// exclusively (CLAUDE.md: "Schema <module> uniquement"), and what `kubuno_seccomp` logs under.
const MODULE_ID: &str = "$moduleid$";

#[derive(Clone)]
struct AppState {
    db: PgPool,
    internal_secret: Arc<str>,
}

#[tokio::main]
async fn main() -> anyhow::Result<()> {
    // Loads .env in local dev; a no-op (not an error) if the file is absent - the real
    // configuration in production comes from the environment the core's supervisor sets.
    dotenvy::dotenv().ok();
    tracing_subscriber::fmt()
        .with_env_filter(tracing_subscriber::EnvFilter::from_default_env())
        .init();

    // No spawned processes: CLAUDE.md section 7, "Zero execution on the host" - modules that never
    // shell out call this at startup so a future regression cannot reintroduce one.
    kubuno_seccomp::lock_down_process_execution(MODULE_ID);

    let database_url = std::env::var("DATABASE_URL")
        .expect("DATABASE_URL must be set (bootstrap-only expect, CLAUDE.md section 7)");
    let internal_secret: Arc<str> = std::env::var("INTERNAL_SECRET")
        .expect("INTERNAL_SECRET must be set (bootstrap-only expect, CLAUDE.md section 7)")
        .into();

    let db = PgPoolOptions::new()
        .max_connections(10)
        .connect(&database_url)
        .await?;
    sqlx::migrate!("./migrations/postgres").run(&db).await?;

    let state = AppState { db, internal_secret };

    let internal_routes = Router::new()
        .route("/ping", get(internal_ping))
        .layer(middleware::from_fn_with_state(state.clone(), require_internal_secret));

    // Standard security response headers (CLAUDE.md section 7), applied uniformly - including to
    // error responses, since the layer wraps the whole router rather than each handler.
    let security_headers = tower::ServiceBuilder::new()
        .layer(SetResponseHeaderLayer::if_not_present(
            HeaderName::from_static("x-frame-options"),
            HeaderValue::from_static("DENY"),
        ))
        .layer(SetResponseHeaderLayer::if_not_present(
            HeaderName::from_static("x-content-type-options"),
            HeaderValue::from_static("nosniff"),
        ));

    let app = Router::new()
        .route("/health", get(health))
        .nest("/internal", internal_routes)
        .layer(security_headers)
        .layer(TraceLayer::new_for_http())
        .with_state(state);

    // Falls back to module.toml's own [server] port (3199, a placeholder - see its own comment)
    // when PORT is not set, matching how the core's supervisor is expected to pass it in production.
    let port: u16 = std::env::var("PORT").ok().and_then(|value| value.parse().ok()).unwrap_or(3199);
    let addr = SocketAddr::from(([127, 0, 0, 1], port));
    tracing::info!(%addr, module = MODULE_ID, "listening");
    let listener = tokio::net::TcpListener::bind(addr).await?;
    axum::serve(listener, app).await?;
    Ok(())
}

#[derive(Serialize)]
struct HealthBody {
    status: &'static str,
    module: &'static str,
}

async fn health() -> Json<HealthBody> {
    Json(HealthBody { status: "ok", module: MODULE_ID })
}

async fn internal_ping(State(state): State<AppState>) -> impl IntoResponse {
    match sqlx::query_scalar::<_, i64>("SELECT count(*) FROM pg_catalog.pg_tables")
        .fetch_one(&state.db)
        .await
    {
        Ok(count) => (StatusCode::OK, Json(serde_json::json!({ "pong": true, "tables": count }))).into_response(),
        Err(error) => {
            // Every DB error is logged before being turned into a response (CLAUDE.md section 7).
            tracing::error!(%error, "internal_ping: database query failed");
            StatusCode::INTERNAL_SERVER_ERROR.into_response()
        }
    }
}

/// `/internal/*` refuses any request without a valid `X-Internal-Secret` header (CLAUDE.md section
/// 7). Constant-time comparison would be stronger still; this is the same shape as the header check,
/// left simple for a starter template - tighten it if this module handles anything sensitive.
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
        Some(secret) if secret == state.internal_secret.as_ref() => next.run(request).await,
        _ => StatusCode::UNAUTHORIZED.into_response(),
    }
}
