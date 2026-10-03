window.coverPlayerKeyboard = (() => {
    let callback;

    const shortcuts = new Set(["n", "p", "c", "i", "s", "o", "l", "+", "-", "delete", "arrowleft", "arrowright"]);

    function isTextInput(target) {
        if (target instanceof HTMLTextAreaElement || target instanceof HTMLSelectElement || target?.isContentEditable) {
            return true;
        }
        return target instanceof HTMLInputElement &&
            !["range", "checkbox", "radio", "button"].includes(target.type);
    }

    function onKeyDown(event) {
        if (event.repeat || event.ctrlKey || event.altKey || event.metaKey) {
            return;
        }

        const target = event.target;
        if (event.key === "Escape") {
            if (!isTextInput(target)) {
                callback?.invokeMethodAsync("HandleKeyboardShortcut", "escape");
            }
            return;
        }

        if (isTextInput(target)) {
            return;
        }

        const key = event.key.toLowerCase();
        if (!shortcuts.has(key) && event.key !== " ") {
            return;
        }

        event.preventDefault();
        callback?.invokeMethodAsync("HandleKeyboardShortcut", event.key === " " ? " " : key);
    }

    function onKeyUp(event) {
        if (event.key === " " && !isTextInput(event.target)) {
            event.preventDefault();
        }
    }

    return {
        register(dotNetCallback) {
            callback = dotNetCallback;
            document.addEventListener("keydown", onKeyDown);
            document.addEventListener("keyup", onKeyUp);
        },
        unregister() {
            document.removeEventListener("keydown", onKeyDown);
            document.removeEventListener("keyup", onKeyUp);
            callback = undefined;
        }
    };
})();
window.coverPlayerDragScroll = (() => {
    const edge = 60;
    const maxSpeed = 18;
    let container = null;
    let speed = 0;
    let frame = 0;

    function step() {
        if (container && speed !== 0) {
            container.scrollTop += speed;
            frame = requestAnimationFrame(step);
        } else {
            frame = 0;
        }
    }

    function stop() {
        speed = 0;
        container = null;
        if (frame) {
            cancelAnimationFrame(frame);
            frame = 0;
        }
    }

    document.addEventListener("dragover", event => {
        const wrapper = event.target instanceof Element
            ? event.target.closest(".library-table-wrapper")
            : null;
        if (!wrapper) {
            stop();
            return;
        }

        const rect = wrapper.getBoundingClientRect();
        const y = event.clientY;
        if (y < rect.top + edge) {
            speed = -Math.ceil(maxSpeed * Math.min(1, (rect.top + edge - y) / edge));
        } else if (y > rect.bottom - edge) {
            speed = Math.ceil(maxSpeed * Math.min(1, (y - (rect.bottom - edge)) / edge));
        } else {
            speed = 0;
        }

        container = wrapper;
        if (speed !== 0 && !frame) {
            frame = requestAnimationFrame(step);
        }
    });

    document.addEventListener("dragend", stop);
    document.addEventListener("drop", stop);

    return { stop };
})();
