mergeInto(LibraryManager.library, {
    // Alerte le site web Killtime lors d'un événement de combat
    NotifyCodexCombatEvent: function(eventJsonPtr) {
        var eventJson = UTF8ToString(eventJsonPtr);
        console.log("[Killtime Unity WebGL] Événement de combat :", eventJson);

        if (window.dispatchEvent) {
            var customEvent = new CustomEvent("killtime:combatevent", { detail: JSON.parse(eventJson) });
            window.dispatchEvent(customEvent);
        }

        if (window.KilltimeBridge && typeof window.KilltimeBridge.onCombatEvent === "function") {
            window.KilltimeBridge.onCombatEvent(JSON.parse(eventJson));
        }
    },

    // Synchronise les points de vie, PA et essoufflement avec la fiche PJ du site web
    SyncCharacterStatsToWeb: function(characterJsonPtr) {
        var characterJson = UTF8ToString(characterJsonPtr);
        console.log("[Killtime Unity WebGL] Synchronisation PJ :", characterJson);

        if (window.KilltimeBridge && typeof window.KilltimeBridge.onCharacterSync === "function") {
            window.KilltimeBridge.onCharacterSync(JSON.parse(characterJson));
        }
    }
});
