mod pipe;

use serde_json::Value;

#[tauri::command]
async fn api_hello() -> Result<Value, String> {
    pipe::hello().await.map_err(|e| e.to_string())
}

#[tauri::command]
async fn api_state() -> Result<Value, String> {
    pipe::call(serde_json::json!({"cmd": "state"}))
        .await
        .map_err(|e| e.to_string())
}

#[tauri::command]
async fn api_toggle(name: String, value: bool) -> Result<Value, String> {
    pipe::call(serde_json::json!({"cmd": "toggle", "name": name, "value": value}))
        .await
        .map_err(|e| e.to_string())
}

// Generic command pass-through. Frontend sends { cmd: "give_money", amount: 1000 } and we
// forward unchanged. Frontend never builds raw JSON — the args are merged into the payload.
#[tauri::command]
async fn api_cmd(payload: Value) -> Result<Value, String> {
    pipe::call(payload).await.map_err(|e| e.to_string())
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            api_hello,
            api_state,
            api_toggle,
            api_cmd,
        ])
        .setup(|app| {
            #[cfg(debug_assertions)]
            {
                use tauri::Manager;
                if let Some(window) = app.get_webview_window("main") {
                    window.open_devtools();
                }
            }
            let _ = app;
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
