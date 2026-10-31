window.coverPlayerYouTube = (() => {
    let apiPromise;

    function loadApi() {
        if (window.YT && window.YT.Player) {
            return Promise.resolve();
        }
        if (apiPromise) {
            return apiPromise;
        }
        apiPromise = new Promise((resolve, reject) => {
            const previousReady = window.onYouTubeIframeAPIReady;
            window.onYouTubeIframeAPIReady = () => {
                if (previousReady) {
                    previousReady();
                }
                resolve();
            };
            const script = document.createElement("script");
            script.src = "https://www.youtube.com/iframe_api";
            script.onerror = reject;
            document.head.appendChild(script);
        });
        return apiPromise;
    }

    return {
        initialize(element, dotnet) {
            if (!element) {
                return;
            }
            loadApi().then(() => {
                if (element._coverPlayerYouTube) {
                    element._coverPlayerYouTube.destroy();
                }
                let ready = false;
                const unavailableTimer = window.setTimeout(() => {
                    if (!ready) {
                        dotnet.invokeMethodAsync("HandleYouTubeError");
                    }
                }, 12000);
                element._coverPlayerYouTube = new YT.Player(element, {
                    events: {
                        onReady: () => {
                            ready = true;
                            window.clearTimeout(unavailableTimer);
                        },
                        onStateChange: event => {
                            if (event.data === YT.PlayerState.ENDED) {
                                dotnet.invokeMethodAsync("HandleYouTubeEnded");
                            }
                        },
                        onError: () => dotnet.invokeMethodAsync("HandleYouTubeError")
                    }
                });
            }).catch(() => dotnet.invokeMethodAsync("HandleYouTubeError"));
        }
    };
})();
