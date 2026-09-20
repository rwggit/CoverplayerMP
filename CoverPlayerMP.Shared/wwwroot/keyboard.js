window.coverPlayerKeyboard = (() => {
    let callback;

    function onKeyDown(event) {
        if (event.repeat || event.ctrlKey || event.altKey || event.metaKey) {
            return;
        }

        const target = event.target;
        if (target instanceof HTMLInputElement ||
            target instanceof HTMLTextAreaElement ||
            target instanceof HTMLSelectElement ||
            target?.isContentEditable) {
            return;
        }

        const key = event.key.toLowerCase();
        if (key !== "n" && key !== "p" && key !== "+" && key !== "-" && event.key !== " ") {
            return;
        }

        event.preventDefault();
        callback?.invokeMethodAsync("HandleKeyboardShortcut", event.key === " " ? " " : key);
    }

    return {
        register(dotNetCallback) {
            callback = dotNetCallback;
            document.addEventListener("keydown", onKeyDown);
        },
        unregister() {
            document.removeEventListener("keydown", onKeyDown);
            callback = undefined;
        }
    };
})();
