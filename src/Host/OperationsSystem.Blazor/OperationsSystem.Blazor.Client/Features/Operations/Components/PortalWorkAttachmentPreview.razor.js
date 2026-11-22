export function createAttachmentUrl(bytes, contentType) {
    return URL.createObjectURL(new Blob([bytes], { type: contentType }));
}

export function revokeAttachmentUrl(url) {
    URL.revokeObjectURL(url);
}

export function reserveAttachmentTab() {
    const tab = window.open("about:blank", "_blank");
    if (!tab) {
        return null;
    }

    tab.opener = null;
    tab.document.title = "Opening attachment";
    tab.document.body.textContent = "Opening attachment…";

    return {
        show(url) {
            if (tab.closed) {
                revokeAttachmentUrl(url);
                return;
            }

            tab.location.replace(url);
            const timer = window.setInterval(() => {
                if (tab.closed) {
                    window.clearInterval(timer);
                    revokeAttachmentUrl(url);
                }
            }, 1000);
        },
        close() {
            if (!tab.closed) {
                tab.close();
            }
        }
    };
}
