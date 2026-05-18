// Named pipe client — talks to JinGuCheats BepInEx plugin.
// Mirrors the wire format used by cheatengine-mcp-rs: 4-byte LE length + UTF-8 JSON body.
//
// Connection model: one logical connection per request. The plugin server accepts one client
// at a time, services it, then loops. This keeps the protocol simple and avoids long-lived
// state. For our request rate (~1 poll/600ms + occasional clicks) the connect overhead is
// negligible (<0.5 ms on Windows).

use anyhow::{anyhow, Context, Result};
use serde_json::Value;
use std::time::Duration;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::windows::named_pipe::ClientOptions;
use tokio::sync::Mutex;
use tokio::time::timeout;

const PIPE_NAME: &str = r"\\.\pipe\JinGuCheats.v1";
const PROTOCOL: u32 = 1;
const MAX_MESSAGE_BYTES: usize = 16 * 1024 * 1024;
const CONNECT_TIMEOUT: Duration = Duration::from_secs(2);
const IO_TIMEOUT: Duration = Duration::from_secs(5);

// Serialize all pipe traffic — the server is single-client by design.
// Tauri command handlers can fire concurrently (poll + button click) so we gate them here.
static PIPE_LOCK: Mutex<()> = Mutex::const_new(());

pub async fn call(request: Value) -> Result<Value> {
    let _guard = PIPE_LOCK.lock().await;

    let req_bytes = serde_json::to_vec(&request)?;
    if req_bytes.len() > MAX_MESSAGE_BYTES {
        return Err(anyhow!("request too large: {} bytes", req_bytes.len()));
    }
    let header = (req_bytes.len() as u32).to_le_bytes();

    // Connect — retry on ERROR_PIPE_BUSY (231) until CONNECT_TIMEOUT elapses
    let mut pipe = timeout(CONNECT_TIMEOUT, async {
        loop {
            match ClientOptions::new().open(PIPE_NAME) {
                Ok(p) => return Ok(p),
                Err(e) if e.raw_os_error() == Some(231) => {
                    tokio::time::sleep(Duration::from_millis(20)).await;
                }
                Err(e) => return Err(e),
            }
        }
    })
    .await
    .map_err(|_| anyhow!("connect timed out — is the game running with the plugin loaded?"))?
    .with_context(|| format!("could not connect to {PIPE_NAME}"))?;

    // Write
    timeout(IO_TIMEOUT, async {
        pipe.write_all(&header).await?;
        pipe.write_all(&req_bytes).await?;
        Ok::<_, std::io::Error>(())
    })
    .await
    .map_err(|_| anyhow!("write timed out"))??;

    // Read header
    let mut resp_header = [0u8; 4];
    timeout(IO_TIMEOUT, pipe.read_exact(&mut resp_header))
        .await
        .map_err(|_| anyhow!("read header timed out"))??;
    let len = u32::from_le_bytes(resp_header) as usize;
    if len == 0 || len > MAX_MESSAGE_BYTES {
        return Err(anyhow!("bad response length: {len}"));
    }

    // Read body
    let mut body = vec![0u8; len];
    timeout(IO_TIMEOUT, pipe.read_exact(&mut body))
        .await
        .map_err(|_| anyhow!("read body timed out"))??;

    let v: Value = serde_json::from_slice(&body)
        .with_context(|| format!("invalid JSON from plugin (first 200 bytes: {:?})",
            String::from_utf8_lossy(&body[..body.len().min(200)])))?;
    Ok(v)
}

pub async fn hello() -> Result<Value> {
    let v = call(serde_json::json!({"cmd": "hello"})).await?;
    if let Some(p) = v.get("protocol").and_then(Value::as_u64) {
        if p as u32 != PROTOCOL {
            return Err(anyhow!(
                "protocol mismatch: UI expects v{PROTOCOL}, plugin reports v{p}. Update one of them."
            ));
        }
    }
    Ok(v)
}
