mergeInto(LibraryManager.library, {
    // Pont WebSocket VTT pour WebGL (le C# System.Net.WebSockets n'existe pas dans le browser).
    // Usage C# : VTTWS_Connect(url, goName) puis SendMessage(goName, "OnVTTWSMessage", data).
    VTTWS_Connect: function(urlPtr, goNamePtr) {
        var url = UTF8ToString(urlPtr);
        var goName = UTF8ToString(goNamePtr);
        try { if (window.__vttWS) { try { window.__vttWS.close(); } catch (e) {} } } catch (e) {}
        window.__vttWSGo = goName;
        console.log("[VTT WebGL] Connexion WS :", url);
        var ws;
        try {
            ws = new WebSocket(url);
        } catch (e) {
            console.error("[VTT WebGL] WebSocket ctor failed:", e);
            SendMessage(goName, "OnVTTWSError", "ctor_failed:" + (e && e.message ? e.message : e));
            return;
        }
        window.__vttWS = ws;
        ws.onopen = function() {
            console.log("[VTT WebGL] WS ouverte.");
            SendMessage(window.__vttWSGo, "OnVTTWSOpen", "");
        };
        ws.onmessage = function(ev) {
            var data = (typeof ev.data === "string") ? ev.data : "";
            SendMessage(window.__vttWSGo, "OnVTTWSMessage", data);
        };
        ws.onclose = function(ev) {
            console.log("[VTT WebGL] WS fermée:", ev && ev.code);
            SendMessage(window.__vttWSGo, "OnVTTWSClose", "close_" + (ev ? ev.code : "?"));
        };
        ws.onerror = function(ev) {
            console.error("[VTT WebGL] WS erreur.");
            SendMessage(window.__vttWSGo, "OnVTTWSError", "ws_error");
        };
    },

    VTTWS_Send: function(msgPtr) {
        var msg = UTF8ToString(msgPtr);
        var ws = window.__vttWS;
        if (!ws || ws.readyState !== 1) {
            console.warn("[VTT WebGL] Envoi ignoré : WS non ouverte.");
            return;
        }
        ws.send(msg);
    },

    VTTWS_Close: function() {
        try { if (window.__vttWS) window.__vttWS.close(1000, "client_leave"); } catch (e) {}
        window.__vttWS = null;
    }
});
