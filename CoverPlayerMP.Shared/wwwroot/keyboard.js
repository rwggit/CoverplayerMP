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
        if (key !== "n" && key !== "p" && key !== "+" && key !== "-" && key !== "delete" && event.key !== " ") {
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
